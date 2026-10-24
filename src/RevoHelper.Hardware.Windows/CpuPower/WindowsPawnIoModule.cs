using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace RevoHelper.Hardware.Windows.CpuPower;

/// <summary>
/// Loads an already signed PawnIO module and executes its deliberately narrow entry points.
/// It neither installs nor changes the PawnIO driver.
/// </summary>
public sealed class WindowsPawnIoModule : IPawnIoModule
{
    private const uint DeviceType = 41394u << 16;
    private const uint IoctlLoadBinary = DeviceType | (0x821u << 2);
    private const uint IoctlExecute = DeviceType | (0x841u << 2);
    private const int FunctionNameLength = 32;
    private const string DevicePath = @"\\?\GLOBALROOT\Device\PawnIO";
    private const string IntelMsrResource = "RevoHelper.Resources.PawnIo.IntelMSR-0.2.10.bin";
    private const string IntelMchbarResource = "RevoHelper.Resources.PawnIo.IntelMCHBAR-0.2.10.bin";

    private readonly SafeFileHandle? _handle;

    private WindowsPawnIoModule(SafeFileHandle? handle, string availabilityReason)
    {
        _handle = handle;
        AvailabilityReason = availabilityReason;
    }

    public bool IsAvailable => _handle is { IsInvalid: false, IsClosed: false };

    public string AvailabilityReason { get; }

    public static WindowsPawnIoModule LoadSignedIntelMsrModule() =>
        LoadSignedModule(IntelMsrResource, "IntelMSR");

    public static WindowsPawnIoModule LoadSignedIntelMchbarModule() =>
        LoadSignedModule(IntelMchbarResource, "IntelMCHBAR");

    private static WindowsPawnIoModule LoadSignedModule(string resourceName, string moduleName)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new WindowsPawnIoModule(null, "PawnIO 仅可在 Windows 上使用。");
        }

        var handle = CreateFileW(
            DevicePath,
            0xC0000000,
            0x00000001 | 0x00000002,
            IntPtr.Zero,
            3,
            0x80,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            return new WindowsPawnIoModule(
                null,
                $"无法打开 PawnIO 设备：{new Win32Exception(error).Message} ({error})");
        }

        try
        {
            using var stream = typeof(WindowsPawnIoModule).Assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                handle.Dispose();
                return new WindowsPawnIoModule(null, $"RevoHelper 中未找到签名 {moduleName} 模块。");
            }

            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var module = memory.ToArray();
            if (!DeviceIoControl(
                    handle,
                    IoctlLoadBinary,
                    module,
                    module.Length,
                    null,
                    0,
                    out _,
                    IntPtr.Zero))
            {
                var error = Marshal.GetLastWin32Error();
                handle.Dispose();
                return new WindowsPawnIoModule(
                    null,
                    $"PawnIO 拒绝加载签名 {moduleName} 模块：{new Win32Exception(error).Message} ({error})");
            }

            return new WindowsPawnIoModule(handle, $"PawnIO {moduleName} 已加载。");
        }
        catch (Exception exception)
        {
            handle.Dispose();
            return new WindowsPawnIoModule(null, $"加载 PawnIO {moduleName} 失败：{exception.Message}");
        }
    }

    public bool TryExecute(
        string function,
        IReadOnlyList<ulong> input,
        int outputLength,
        out ulong[] output)
    {
        output = [];
        if (!IsAvailable || string.IsNullOrWhiteSpace(function) || outputLength < 0)
        {
            return false;
        }

        var inputBytes = new byte[FunctionNameLength + (input.Count * sizeof(ulong))];
        var functionBytes = Encoding.ASCII.GetBytes(function);
        Buffer.BlockCopy(functionBytes, 0, inputBytes, 0, Math.Min(FunctionNameLength - 1, functionBytes.Length));
        for (var index = 0; index < input.Count; index++)
        {
            BitConverter.TryWriteBytes(
                inputBytes.AsSpan(FunctionNameLength + (index * sizeof(ulong)), sizeof(ulong)),
                input[index]);
        }

        var outputBytes = new byte[outputLength * sizeof(ulong)];
        if (!DeviceIoControl(
                _handle!,
                IoctlExecute,
                inputBytes,
                inputBytes.Length,
                outputBytes,
                outputBytes.Length,
                out var bytesReturned,
                IntPtr.Zero) ||
            bytesReturned % sizeof(ulong) != 0)
        {
            return false;
        }

        output = new ulong[bytesReturned / sizeof(ulong)];
        Buffer.BlockCopy(outputBytes, 0, output, 0, bytesReturned);
        return output.Length == outputLength;
    }

    public void Dispose() => _handle?.Dispose();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle device,
        uint controlCode,
        byte[]? inputBuffer,
        int inputBufferSize,
        byte[]? outputBuffer,
        int outputBufferSize,
        out int bytesReturned,
        IntPtr overlapped);
}
