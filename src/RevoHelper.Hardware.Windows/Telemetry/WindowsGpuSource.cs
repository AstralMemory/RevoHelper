using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using RevoHelper.Core.Dashboard;
using RevoHelper.Hardware.Windows.Discovery;

namespace RevoHelper.Hardware.Windows.Telemetry;

/// <summary>A display adapter as Plug and Play lists it, with the row kind its PCI facts give.</summary>
public sealed record WindowsGpuAdapter(string InstanceId, string Name, HardwareVendor Vendor, DeviceKind Kind);

/// <summary>What the graphics driver reports through Windows; null where it reports nothing.</summary>
public sealed record WindowsGpuDriverReading(double? TemperatureC, double? FrequencyMhz)
{
    public static WindowsGpuDriverReading Empty { get; } = new(null, null);
}

/// <summary>
/// Windows' own view of the GPUs, the data Task Manager shows. The adapter list, the power state
/// and the engine usage come from Plug and Play and the graphics kernel's own bookkeeping and never
/// wake a device; <see cref="ReadDriver"/> and <see cref="ReadDedicatedMemoryMegabytes"/> go to the
/// adapter itself.
/// </summary>
public interface IWindowsGpuSource : IDisposable
{
    IReadOnlyList<WindowsGpuAdapter> Adapters();

    /// <summary>true = working, false = powered down, null = unknown.</summary>
    bool? ReadPowered(WindowsGpuAdapter adapter);

    /// <summary>The busiest engine of each adapter since the previous call, by instance ID; rates need two calls.</summary>
    IReadOnlyDictionary<string, double> ReadUsage(IReadOnlyList<WindowsGpuAdapter> adapters);

    WindowsGpuDriverReading ReadDriver(WindowsGpuAdapter adapter);

    double? ReadDedicatedMemoryMegabytes(WindowsGpuAdapter adapter);
}

/// <summary>One adapter's engines: the busiest one, as Task Manager shows it, and where its 3D engines are.</summary>
public sealed record GpuEngineSummary(double UsagePercent, IReadOnlyList<(int Physical, int Node)> ThreeDNodes);

public static class GpuEngineUsage
{
    // pid_1234_luid_0x00000000_0x000108F0_phys_0_eng_4_engtype_Copy
    private static readonly Regex InstanceName = new(
        @"luid_0x(?<high>[0-9a-f]{8})_0x(?<low>[0-9a-f]{8})_phys_(?<phys>\d+)_eng_(?<eng>\d+)_engtype_(?<type>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The adapter's LUID as the "GPU Engine" counters spell it.</summary>
    public static string LuidKey(uint low, int high) => $"luid_0x{(uint)high:X8}_0x{low:X8}";

    /// <summary>Each engine adds up the processes using it; an adapter's usage is its busiest engine.</summary>
    public static IReadOnlyDictionary<string, GpuEngineSummary> Aggregate(IEnumerable<(string Name, double Value)> instances)
    {
        var engines = new Dictionary<(string Luid, int Physical, int Node), (double Sum, bool ThreeD)>();
        foreach (var (name, value) in instances)
        {
            if (!double.IsFinite(value) || value < 0) continue;
            var match = InstanceName.Match(name);
            if (!match.Success ||
                !int.TryParse(match.Groups["phys"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var physical) ||
                !int.TryParse(match.Groups["eng"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var node))
                continue;
            var luid = $"luid_0x{match.Groups["high"].Value.ToUpperInvariant()}_0x{match.Groups["low"].Value.ToUpperInvariant()}";
            var threeD = string.Equals(match.Groups["type"].Value, "3D", StringComparison.OrdinalIgnoreCase);
            var key = (luid, physical, node);
            engines[key] = engines.TryGetValue(key, out var engine) ? (engine.Sum + value, engine.ThreeD || threeD) : (value, threeD);
        }

        return engines
            .GroupBy(pair => pair.Key.Luid, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => new GpuEngineSummary(
                    // Process times are sampled separately, so a busy engine can add up to a little over 100.
                    Math.Round(Math.Min(group.Max(pair => pair.Value.Sum), 100), 1),
                    group.Where(pair => pair.Value.ThreeD)
                        .Select(pair => (pair.Key.Physical, pair.Key.Node))
                        .OrderBy(node => node.Physical).ThenBy(node => node.Node)
                        .ToArray()),
                StringComparer.OrdinalIgnoreCase);
    }
}

/// <summary>
/// <see cref="IWindowsGpuSource"/> through Plug and Play, the "GPU Engine" performance counters and
/// the graphics kernel's adapter queries (D3DKMT), with no vendor library and no driver of our own.
/// </summary>
public sealed class WddmGpuSource : IWindowsGpuSource
{
    private const long AdapterListLifetimeMs = 60_000;
    // A rate over a longer gap is an average of the past, not the current load.
    private const long MaximumSampleGapMs = 3_000;
    private static readonly IReadOnlyList<(int Physical, int Node)> FirstNode = [(0, 0)];
    private static readonly IReadOnlyDictionary<string, GpuEngineSummary> NoEngines = new Dictionary<string, GpuEngineSummary>();
    private readonly object gate = new();
    private readonly Dictionary<string, (uint Low, int High)?> luids = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, double> memory = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<WindowsGpuAdapter> adapters = [];
    private IReadOnlyDictionary<string, GpuEngineSummary> engines = NoEngines;
    private long adaptersReadAt, lastCollectAt;
    private IntPtr query, engineCounter;
    private bool listed, queryOpened, disposed;

    public IReadOnlyList<WindowsGpuAdapter> Adapters()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var now = Environment.TickCount64;
            if (listed && now - adaptersReadAt < AdapterListLifetimeMs) return adapters;
            listed = true;
            adaptersReadAt = now;
            luids.Clear(); // an adapter that restarted comes back under a new LUID
            adapters = PlugAndPlayDevices.PresentDisplayAdapters()
                .Select(device => Describe(device.InstanceId, device.Name))
                .ToArray();
            return adapters;
        }
    }

    /// <summary>The same facts and rule that decide the adapter's row on the home page.</summary>
    public static WindowsGpuAdapter Describe(string instanceId, string name, Func<string, PciLocation?>? readPciLocation = null)
    {
        var adapter = WindowsDisplayAdapterReader.Describe(instanceId, name, readPciLocation ?? PlugAndPlayDevices.ReadPciLocation);
        return new(instanceId, name, adapter.Vendor, HomeDashboardFactory.ClassifyAdapter(adapter));
    }

    public bool? ReadPowered(WindowsGpuAdapter adapter) => PlugAndPlayDevices.ReadPowered(adapter.InstanceId);

    public IReadOnlyDictionary<string, double> ReadUsage(IReadOnlyList<WindowsGpuAdapter> wanted)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            if (!queryOpened) OpenQuery();
            var now = Environment.TickCount64;
            var fresh = lastCollectAt != 0 && now - lastCollectAt <= MaximumSampleGapMs;
            if (!PdhNative.Collect(query))
            {
                lastCollectAt = 0;
                return result;
            }

            lastCollectAt = now;
            engines = GpuEngineUsage.Aggregate(PdhNative.Items(engineCounter));
            if (!fresh) return result;
            foreach (var adapter in wanted)
            {
                if (Luid(adapter) is { } luid &&
                    engines.TryGetValue(GpuEngineUsage.LuidKey(luid.Low, luid.High), out var summary))
                    result[adapter.InstanceId] = summary.UsagePercent;
            }
            return result;
        }
    }

    public WindowsGpuDriverReading ReadDriver(WindowsGpuAdapter adapter)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (Luid(adapter) is not { } luid) return WindowsGpuDriverReading.Empty;
            // The clock of the 3D engine; drivers put it at node 0 when the counters have not said otherwise.
            var nodes = engines.TryGetValue(GpuEngineUsage.LuidKey(luid.Low, luid.High), out var summary) &&
                        summary.ThreeDNodes.Count > 0
                ? summary.ThreeDNodes
                : FirstNode;
            var reading = D3dkmt.ReadPerformance(luid.Low, luid.High, nodes);
            if (reading is null) luids.Remove(adapter.InstanceId); // gone or restarted: look it up again
            return reading ?? WindowsGpuDriverReading.Empty;
        }
    }

    public double? ReadDedicatedMemoryMegabytes(WindowsGpuAdapter adapter)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (memory.TryGetValue(adapter.InstanceId, out var known)) return known;
            if (Luid(adapter) is not { } luid) return null;
            var bytes = D3dkmt.ReadDedicatedVideoMemory(luid.Low, luid.High) ?? 0;
            if (bytes == 0) return null;
            var megabytes = Math.Round(bytes / 1048576d);
            memory[adapter.InstanceId] = megabytes;
            return megabytes;
        }
    }

    /// <summary>D3DKMT_ADAPTER_PERFDATA.Temperature is in tenths of a degree; 0 means the driver reports none.</summary>
    public static double? TemperatureFromTenths(uint tenths)
    {
        var celsius = tenths / 10d;
        return celsius is > 0 and < 150 ? Math.Round(celsius, 1) : null;
    }

    /// <summary>D3DKMT_NODE_PERFDATA.Frequency is in hertz; 0 means the driver reports none.</summary>
    public static double? FrequencyFromHertz(ulong hertz)
    {
        var megahertz = hertz / 1_000_000d;
        return megahertz is > 0 and < 20000 ? Math.Round(megahertz) : null;
    }

    private (uint Low, int High)? Luid(WindowsGpuAdapter adapter)
    {
        if (luids.TryGetValue(adapter.InstanceId, out var known)) return known;
        (uint Low, int High)? luid = null;
        try
        {
            if (PlugAndPlayDevices.DisplayInterfacePath(adapter.InstanceId) is { } path) luid = D3dkmt.OpenLuid(path);
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            luid = null;
        }
        luids[adapter.InstanceId] = luid; // kept until the list is read again
        return luid;
    }

    private void OpenQuery()
    {
        queryOpened = true;
        try
        {
            query = PdhNative.OpenQuery();
            engineCounter = PdhNative.Add(query, @"\GPU Engine(*)\Utilization Percentage");
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            query = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            PdhNative.Close(query);
            query = IntPtr.Zero;
        }
    }
}

/// <summary>The graphics kernel's adapter calls in gdi32, the ones Task Manager makes.</summary>
internal static class D3dkmt
{
    // KMTQUERYADAPTERINFOTYPE (d3dkmthk.h): KMTQAITYPE_GETSEGMENTSIZE, _NODEPERFDATA, _ADAPTERPERFDATA.
    private const int GetSegmentSize = 3, NodePerfData = 61, AdapterPerfData = 62;
    // x64 sizes of D3DKMT_ADAPTER_PERFDATA, D3DKMT_NODE_PERFDATA and D3DKMT_SEGMENTSIZEINFO.
    private const int AdapterPerfDataSize = 64, NodePerfDataSize = 56, SegmentSizeInfoSize = 24;
    private const int TemperatureOffset = 56, FrequencyOffset = 8;

    public static (uint Low, int High)? OpenLuid(string devicePath)
    {
        var name = Marshal.StringToHGlobalUni(devicePath);
        try
        {
            var open = new OpenAdapterFromDeviceName { DeviceName = name };
            if (D3DKMTOpenAdapterFromDeviceName(ref open) != 0) return null;
            Close(open.Adapter);
            return (open.LuidLow, open.LuidHigh);
        }
        finally { Marshal.FreeHGlobal(name); }
    }

    /// <summary>null when the adapter can no longer be opened by this LUID.</summary>
    public static WindowsGpuDriverReading? ReadPerformance(uint low, int high, IReadOnlyList<(int Physical, int Node)> nodes)
    {
        if (Open(low, high) is not { } adapter) return null;
        try
        {
            double? temperature = null;
            var adapterData = new byte[AdapterPerfDataSize]; // physical adapter 0
            if (Query(adapter, AdapterPerfData, adapterData) == 0)
                temperature = WddmGpuSource.TemperatureFromTenths(BitConverter.ToUInt32(adapterData, TemperatureOffset));

            double? frequency = null;
            foreach (var (physical, node) in nodes)
            {
                var nodeData = new byte[NodePerfDataSize];
                BitConverter.TryWriteBytes(nodeData.AsSpan(0, 4), (uint)node);
                BitConverter.TryWriteBytes(nodeData.AsSpan(4, 4), (uint)physical);
                if (Query(adapter, NodePerfData, nodeData) != 0) continue;
                if (WddmGpuSource.FrequencyFromHertz(BitConverter.ToUInt64(nodeData, FrequencyOffset)) is { } megahertz &&
                    (frequency is null || megahertz > frequency))
                    frequency = megahertz;
            }
            return new(temperature, frequency);
        }
        finally { Close(adapter); }
    }

    public static ulong? ReadDedicatedVideoMemory(uint low, int high)
    {
        if (Open(low, high) is not { } adapter) return null;
        try
        {
            var data = new byte[SegmentSizeInfoSize]; // dedicated video memory comes first
            return Query(adapter, GetSegmentSize, data) == 0 ? BitConverter.ToUInt64(data, 0) : null;
        }
        finally { Close(adapter); }
    }

    private static uint? Open(uint low, int high)
    {
        var open = new OpenAdapterFromLuid { LuidLow = low, LuidHigh = high };
        return D3DKMTOpenAdapterFromLuid(ref open) == 0 ? open.Adapter : null;
    }

    private static void Close(uint adapter)
    {
        var close = new CloseAdapter { Adapter = adapter };
        D3DKMTCloseAdapter(ref close);
    }

    private static int Query(uint adapter, int type, byte[] buffer)
    {
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            var info = new QueryAdapterInfo
            {
                Adapter = adapter,
                Type = type,
                PrivateData = handle.AddrOfPinnedObject(),
                PrivateDataSize = (uint)buffer.Length
            };
            return D3DKMTQueryAdapterInfo(ref info);
        }
        finally { handle.Free(); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct OpenAdapterFromDeviceName { public IntPtr DeviceName; public uint Adapter; public uint LuidLow; public int LuidHigh; }

    [StructLayout(LayoutKind.Sequential)]
    private struct OpenAdapterFromLuid { public uint LuidLow; public int LuidHigh; public uint Adapter; }

    [StructLayout(LayoutKind.Sequential)]
    private struct CloseAdapter { public uint Adapter; }

    [StructLayout(LayoutKind.Sequential)]
    private struct QueryAdapterInfo { public uint Adapter; public int Type; public IntPtr PrivateData; public uint PrivateDataSize; }

    [DllImport("gdi32.dll")]
    private static extern int D3DKMTOpenAdapterFromDeviceName(ref OpenAdapterFromDeviceName data);

    [DllImport("gdi32.dll")]
    private static extern int D3DKMTOpenAdapterFromLuid(ref OpenAdapterFromLuid data);

    [DllImport("gdi32.dll")]
    private static extern int D3DKMTCloseAdapter(ref CloseAdapter data);

    [DllImport("gdi32.dll")]
    private static extern int D3DKMTQueryAdapterInfo(ref QueryAdapterInfo data);
}
