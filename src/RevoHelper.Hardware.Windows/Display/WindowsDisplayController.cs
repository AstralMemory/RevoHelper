using System.Runtime.InteropServices;
using RevoHelper.Core.Control;

namespace RevoHelper.Hardware.Windows.Display;

public sealed record WindowsDisplayMode(
    string DeviceName,
    string DisplayName,
    int Width,
    int Height,
    int RefreshRateHz);

public sealed record WindowsDisplayPathRefresh(
    int DesktopRefreshRateHz,
    int PhysicalRefreshRateHz);

public interface IWindowsDisplayModeApi
{
    WindowsDisplayMode ReadCurrent();
    WindowsDisplayMode ReadSaved(string deviceName);
    IReadOnlyList<WindowsDisplayMode> EnumerateModes(string deviceName);
    WindowsDisplayPathRefresh? ReadActivePathRefresh(string deviceName);
    bool TryApplyRefreshRate(string deviceName, int width, int height, int refreshRateHz, bool persist);
}

public sealed class WindowsDisplayController(IWindowsDisplayModeApi? api = null) : IDisplayController
{
    private readonly IWindowsDisplayModeApi api = api ?? new Win32DisplayModeApi();
    private readonly SemaphoreSlim gate = new(1, 1);
    private DisplaySnapshot? temporaryBaseline;
    private int temporaryExpectedHz;
    private volatile bool stopping;

    public ValueTask<DisplaySnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var current = api.ReadCurrent();
        var activePath = api.ReadActivePathRefresh(current.DeviceName);
        var desktopRefreshRate = activePath?.DesktopRefreshRateHz > 0
            ? activePath.DesktopRefreshRateHz
            : current.RefreshRateHz;
        var physicalRefreshRate = activePath?.PhysicalRefreshRateHz > 0
            ? activePath.PhysicalRefreshRateHz
            : current.RefreshRateHz;
        var rates = api.EnumerateModes(current.DeviceName)
            .Where(mode => mode.Width == current.Width && mode.Height == current.Height && mode.RefreshRateHz > 0)
            .Select(mode => mode.RefreshRateHz)
            .Append(physicalRefreshRate)
            .Where(rate => rate > 0)
            .Distinct()
            .Order()
            .ToArray();
        return ValueTask.FromResult(new DisplaySnapshot(
            current.DeviceName,
            string.IsNullOrWhiteSpace(current.DisplayName) ? "当前显示器" : current.DisplayName,
            current.Width,
            current.Height,
            physicalRefreshRate,
            rates,
            desktopRefreshRate));
    }

    public ValueTask<ControlResult> SetRefreshRateAsync(int refreshRateHz, CancellationToken cancellationToken) =>
        SetAsync(refreshRateHz, persist: true, cancellationToken);

    public ValueTask<ControlResult> SetTemporaryRefreshRateAsync(int refreshRateHz, CancellationToken cancellationToken) =>
        SetAsync(refreshRateHz, persist: false, cancellationToken);

    private async ValueTask<ControlResult> SetAsync(int refreshRateHz, bool persist, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (stopping) return ControlResult.Rejected("显示控制会话正在结束。");
            var before = await ReadAsync(cancellationToken).ConfigureAwait(false);
            if (temporaryBaseline is not null && !OwnsCurrent(before)) temporaryBaseline = null;
            if (!before.AvailableRefreshRatesHz.Contains(refreshRateHz))
                return ControlResult.Rejected($"当前显示路径未提供 {refreshRateHz} Hz，未更改显示设置。");
            var currentMatches = before.CurrentRefreshRateHz == refreshRateHz && !before.RequiresRefreshRateSynchronization;
            // Manual Apply must update the saved mode even if a temporary high rate is already active.
            if (!persist && currentMatches) return new(true, $"当前已经是 {refreshRateHz} Hz。");
            var result = await ApplyAndVerifyAsync(before, refreshRateHz, persist, cancellationToken, () =>
            {
                if (!persist)
                {
                    temporaryBaseline ??= before;
                    temporaryExpectedHz = refreshRateHz;
                }
            }).ConfigureAwait(false);
            if (persist && result.Succeeded) temporaryBaseline = null;
            return result;
        }
        finally { gate.Release(); }
    }

    public async ValueTask<ControlResult> RestoreTemporaryRefreshRateAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (temporaryBaseline is null) return new(true, "没有需要恢复的临时刷新率。");
            var current = await ReadAsync(cancellationToken).ConfigureAwait(false);
            if (!OwnsCurrent(current))
            {
                temporaryBaseline = null;
                return new(true, "显示设置已由其它操作更改，保留当前状态。");
            }
            if (!current.AvailableRefreshRatesHz.Contains(temporaryBaseline.CurrentRefreshRateHz))
                return ControlResult.Rejected("原刷新率在当前路径已不可用，未强制恢复。");
            var result = await ApplyAndVerifyAsync(current, temporaryBaseline.CurrentRefreshRateHz,
                persist: false, cancellationToken).ConfigureAwait(false);
            if (result.Succeeded) temporaryBaseline = null;
            return result;
        }
        finally { gate.Release(); }
    }

    public ValueTask<ControlResult> StopAndRestoreAsync(CancellationToken cancellationToken)
    {
        stopping = true;
        return RestoreTemporaryRefreshRateAsync(cancellationToken);
    }

    private bool OwnsCurrent(DisplaySnapshot current) => temporaryBaseline is not null &&
        current.DeviceName == temporaryBaseline.DeviceName && current.Width == temporaryBaseline.Width &&
        current.Height == temporaryBaseline.Height && current.CurrentRefreshRateHz == temporaryExpectedHz &&
        !current.RequiresRefreshRateSynchronization;

    private async ValueTask<ControlResult> ApplyAndVerifyAsync(
        DisplaySnapshot before, int refreshRateHz, bool persist, CancellationToken cancellationToken, Action? onApplied = null)
    {
        if (!api.TryApplyRefreshRate(before.DeviceName, before.Width, before.Height, refreshRateHz, persist))
            return ControlResult.Rejected($"Windows 拒绝应用 {refreshRateHz} Hz。");
        onApplied?.Invoke();
        var after = await ReadAsync(cancellationToken).ConfigureAwait(false);
        if (after.CurrentRefreshRateHz != refreshRateHz)
            return ControlResult.Rejected($"Windows 已接受设置，但面板路径回读仍为 {after.CurrentRefreshRateHz} Hz。");
        if (after.RequiresRefreshRateSynchronization)
            return ControlResult.Rejected(
                $"面板路径为 {after.CurrentRefreshRateHz} Hz，但桌面路径仍为 {after.DesktopRefreshRateHz} Hz。");
        if (persist)
        {
            var saved = api.ReadSaved(before.DeviceName);
            if (saved.RefreshRateHz != refreshRateHz || saved.Width != before.Width || saved.Height != before.Height)
                return ControlResult.Rejected("当前刷新率已更新，但 Windows 保存值回读不一致。");
        }
        return new(true, persist
            ? $"刷新率已设为 {refreshRateHz} Hz，并保存到 Windows；重启后仍有效。"
            : $"临时刷新率已切换为 {refreshRateHz} Hz，未更改 Windows 保存值。");
    }
}

public sealed class Win32DisplayModeApi : IWindowsDisplayModeApi
{
    private const int CurrentSettings = -1;
    private const int ErrorSuccess = 0;
    private const int ErrorInsufficientBuffer = 122;
    private const int DisplayDeviceAttachedToDesktop = 0x1;
    private const int DisplayDevicePrimaryDevice = 0x4;
    private const uint QdcOnlyActivePaths = 0x00000002;
    private const uint QdcVirtualModeAware = 0x00000010;
    private const uint QdcVirtualRefreshRateAware = 0x00000040;
    private const uint DisplayConfigPathActive = 0x00000001;
    private const uint DisplayConfigPathSupportVirtualMode = 0x00000008;
    private const uint DisplayConfigModeInfoTypeTarget = 2;
    private const uint DisplayConfigDeviceInfoGetSourceName = 1;
    private const uint DisplayConfigPathModeIndexInvalid = 0xffffffff;
    private const uint DisplayConfigPathTargetModeIndexInvalid = 0xffff;
    private const int DmPelsWidth = 0x00080000;
    private const int DmPelsHeight = 0x00100000;
    private const int DmDisplayFrequency = 0x00400000;

    public WindowsDisplayMode ReadCurrent()
    {
        var device = FindPrimaryDevice();
        var mode = CreateDevMode();
        if (!EnumDisplaySettings(device.DeviceName, CurrentSettings, ref mode))
            throw new InvalidOperationException("Windows 未返回当前显示模式。");
        return ToMode(device.DeviceName, ReadFriendlyName(device.DeviceName), mode);
    }

    public IReadOnlyList<WindowsDisplayMode> EnumerateModes(string deviceName)
    {
        var result = new List<WindowsDisplayMode>();
        var friendlyName = ReadFriendlyName(deviceName);
        for (var index = 0; ; index++)
        {
            var mode = CreateDevMode();
            if (!EnumDisplaySettings(deviceName, index, ref mode)) break;
            result.Add(ToMode(deviceName, friendlyName, mode));
        }
        return result;
    }

    public WindowsDisplayPathRefresh? ReadActivePathRefresh(string deviceName)
    {
        var awareFlags = QdcOnlyActivePaths | QdcVirtualModeAware | QdcVirtualRefreshRateAware;
        return TryReadActivePathRefresh(deviceName, awareFlags)
            ?? TryReadActivePathRefresh(deviceName, QdcOnlyActivePaths | QdcVirtualModeAware);
    }

    public WindowsDisplayMode ReadSaved(string deviceName)
    {
        var mode = CreateDevMode();
        if (!EnumDisplaySettings(deviceName, -2, ref mode))
            throw new InvalidOperationException("Windows 未返回已保存的显示模式。");
        return ToMode(deviceName, ReadFriendlyName(deviceName), mode);
    }

    public bool TryApplyRefreshRate(string deviceName, int width, int height, int refreshRateHz, bool persist)
    {
        var mode = CreateDevMode();
        if (!EnumDisplaySettings(deviceName, CurrentSettings, ref mode)) return false;
        mode.dmPelsWidth = width;
        mode.dmPelsHeight = height;
        mode.dmDisplayFrequency = refreshRateHz;
        mode.dmFields = DmPelsWidth | DmPelsHeight | DmDisplayFrequency;
        // CDS_UPDATEREGISTRY belongs only to explicit user Apply, never power-source automation.
        return ChangeDisplaySettingsEx(deviceName, ref mode, IntPtr.Zero, persist ? 1u : 0u, IntPtr.Zero) == 0;
    }

    private static DISPLAY_DEVICE FindPrimaryDevice()
    {
        for (uint index = 0; ; index++)
        {
            var device = CreateDisplayDevice();
            if (!EnumDisplayDevices(null, index, ref device, 0)) break;
            if ((device.StateFlags & DisplayDeviceAttachedToDesktop) != 0 &&
                (device.StateFlags & DisplayDevicePrimaryDevice) != 0) return device;
        }
        throw new InvalidOperationException("Windows 未找到活动的主显示器。");
    }

    private static string ReadFriendlyName(string deviceName)
    {
        var monitor = CreateDisplayDevice();
        return EnumDisplayDevices(deviceName, 0, ref monitor, 0) && !string.IsNullOrWhiteSpace(monitor.DeviceString)
            ? monitor.DeviceString
            : "内置显示器";
    }

    private static WindowsDisplayMode ToMode(string deviceName, string displayName, DEVMODE mode) =>
        new(deviceName, displayName, mode.dmPelsWidth, mode.dmPelsHeight, mode.dmDisplayFrequency);

    private static WindowsDisplayPathRefresh? TryReadActivePathRefresh(string deviceName, uint flags)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var result = GetDisplayConfigBufferSizes(flags, out var pathCount, out var modeCount);
            if (result != ErrorSuccess) return null;

            var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
            result = QueryDisplayConfig(
                flags, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
            if (result == ErrorInsufficientBuffer) continue;
            if (result != ErrorSuccess) return null;

            for (var index = 0; index < pathCount; index++)
            {
                var path = paths[index];
                if ((path.flags & DisplayConfigPathActive) == 0 ||
                    !string.Equals(ReadSourceDeviceName(path.sourceInfo), deviceName,
                        StringComparison.OrdinalIgnoreCase)) continue;

                var desktopRate = ToRefreshRate(path.targetInfo.refreshRate);
                var targetModeIndex = TargetModeIndex(path);
                var physicalRate = targetModeIndex < modeCount &&
                    modes[targetModeIndex].infoType == DisplayConfigModeInfoTypeTarget
                        ? ToRefreshRate(modes[targetModeIndex].modeInfo.targetMode
                            .targetVideoSignalInfo.vSyncFreq)
                        : 0;
                if (desktopRate <= 0 && physicalRate <= 0) return null;
                if (desktopRate <= 0) desktopRate = physicalRate;
                if (physicalRate <= 0) physicalRate = desktopRate;
                return new(desktopRate, physicalRate);
            }

            return null;
        }

        return null;
    }

    private static string? ReadSourceDeviceName(DISPLAYCONFIG_PATH_SOURCE_INFO source)
    {
        var request = new DISPLAYCONFIG_SOURCE_DEVICE_NAME
        {
            header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
            {
                type = DisplayConfigDeviceInfoGetSourceName,
                size = (uint)Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>(),
                adapterId = source.adapterId,
                id = source.id
            }
        };
        return DisplayConfigGetDeviceInfo(ref request) == ErrorSuccess
            ? request.viewGdiDeviceName
            : null;
    }

    private static uint TargetModeIndex(DISPLAYCONFIG_PATH_INFO path)
    {
        var packed = path.targetInfo.modeInfoIdx;
        if (packed == DisplayConfigPathModeIndexInvalid) return packed;
        return (path.flags & DisplayConfigPathSupportVirtualMode) != 0
            ? (packed >> 16) & DisplayConfigPathTargetModeIndexInvalid
            : packed;
    }

    private static int ToRefreshRate(DISPLAYCONFIG_RATIONAL rate)
    {
        if (rate.Numerator == 0 || rate.Denominator == 0) return 0;
        return (int)Math.Round(rate.Numerator / (double)rate.Denominator,
            MidpointRounding.AwayFromZero);
    }

    private static DEVMODE CreateDevMode() => new() { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
    private static DISPLAY_DEVICE CreateDisplayDevice() => new() { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsEx(
        string deviceName, ref DEVMODE devMode, IntPtr hwnd, uint flags, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevices(
        string? device, uint devNum, ref DISPLAY_DEVICE displayDevice, uint flags);

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(
        uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(
        uint flags,
        ref uint numPathArrayElements,
        [Out] DISPLAYCONFIG_PATH_INFO[] pathInfoArray,
        ref uint numModeInfoArrayElements,
        [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray,
        IntPtr currentTopologyId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int DisplayConfigGetDeviceInfo(
        ref DISPLAYCONFIG_SOURCE_DEVICE_NAME requestPacket);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_RATIONAL
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_2DREGION
    {
        public uint cx;
        public uint cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_SOURCE_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_TARGET_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public int outputTechnology;
        public int rotation;
        public int scaling;
        public DISPLAYCONFIG_RATIONAL refreshRate;
        public int scanLineOrdering;
        public uint targetAvailable;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_INFO
    {
        public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
        public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_VIDEO_SIGNAL_INFO
    {
        public ulong pixelRate;
        public DISPLAYCONFIG_RATIONAL hSyncFreq;
        public DISPLAYCONFIG_RATIONAL vSyncFreq;
        public DISPLAYCONFIG_2DREGION activeSize;
        public DISPLAYCONFIG_2DREGION totalSize;
        public uint videoStandard;
        public int scanLineOrdering;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_TARGET_MODE
    {
        public DISPLAYCONFIG_VIDEO_SIGNAL_INFO targetVideoSignalInfo;
    }

    [StructLayout(LayoutKind.Explicit, Size = 48)]
    private struct DISPLAYCONFIG_MODE_INFO_UNION
    {
        [FieldOffset(0)] public DISPLAYCONFIG_TARGET_MODE targetMode;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_MODE_INFO
    {
        public uint infoType;
        public uint id;
        public LUID adapterId;
        public DISPLAYCONFIG_MODE_INFO_UNION modeInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_DEVICE_INFO_HEADER
    {
        public uint type;
        public uint size;
        public LUID adapterId;
        public uint id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string viewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }
}
