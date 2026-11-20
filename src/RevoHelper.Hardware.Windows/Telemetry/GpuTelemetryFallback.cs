using RevoHelper.Core.Dashboard;
using RevoHelper.Core.Telemetry;
using RevoHelper.Hardware.Windows.Discovery;

namespace RevoHelper.Hardware.Windows.Telemetry;

/// <summary>
/// The backup for GPU readings. What the sensor library left empty (N/A) is filled from Windows'
/// own data, and an adapter the library does not list at all still gets its readings; a value the
/// library has is never replaced, and on a machine where it reads everything nothing here runs.
/// Engine usage is the graphics kernel's own bookkeeping and costs the device nothing. The driver
/// is asked for temperature and clock only while Windows reports the device powered, and a
/// discrete GPU that sits idle is left alone for a while so it can power down, the rule the NVIDIA
/// readings follow. A powered-down GPU is shown asleep.
/// </summary>
public sealed class GpuTelemetryFallback(IWindowsGpuSource source, Action<string, string>? diagnostic = null)
{
    /// <summary><see cref="Pair"/>: the library has no device for this adapter, so a row is added.</summary>
    public const int Added = -1;

    /// <summary><see cref="Pair"/>: a library device may be this adapter under another name; nothing is added.</summary>
    public const int Unsure = -2;

    /// <summary>How long a driver figure stands in for a moment when the driver reports none.</summary>
    public static readonly TimeSpan Hold = TimeSpan.FromSeconds(5);

    private static readonly IReadOnlyDictionary<string, double> NoUsage = new Dictionary<string, double>();
    private readonly Dictionary<string, AdapterState> states = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<DeviceTelemetry> Fill(IReadOnlyList<DeviceTelemetry> devices, DateTimeOffset now)
    {
        var adapters = Try(source.Adapters, (IReadOnlyList<WindowsGpuAdapter>)[]);
        if (adapters.Count == 0) return devices;

        var pairs = Pair(devices, adapters);
        var work = new List<Work>();
        for (var index = 0; index < adapters.Count; index++)
        {
            if (pairs[index] == Unsure) continue;
            var device = pairs[index] >= 0 ? devices[pairs[index]] : null;
            var needs = Needs.Of(device, adapters[index].Kind);
            if (needs.Any) work.Add(new(adapters[index], pairs[index], device, needs));
        }
        if (work.Count == 0) return devices;

        foreach (var item in work) item.Powered = Try(() => source.ReadPowered(item.Adapter), null);
        var wanted = work.Where(item => item.Powered != false && item.Needs.Usage).Select(item => item.Adapter).ToArray();
        var usage = wanted.Length > 0 ? Try(() => source.ReadUsage(wanted), NoUsage) : NoUsage;

        var result = devices.ToList();
        foreach (var item in work)
        {
            var filled = item.Powered == false ? Asleep(item, now) : Awake(item, usage, now);
            if (item.DeviceIndex >= 0) result[item.DeviceIndex] = filled;
            else result.Add(filled);
            ReportOnce(item, filled);
        }
        return result;
    }

    /// <summary>
    /// For each adapter, the index of the library's device for it, <see cref="Added"/> when the
    /// library has none, or <see cref="Unsure"/> when an unmatched library device of the same vendor
    /// might be it (a second row could then show one GPU twice). Hardware identity first, then the
    /// cleaned-up name, then the only device of its vendor on both sides.
    /// </summary>
    public static int[] Pair(IReadOnlyList<DeviceTelemetry> devices, IReadOnlyList<WindowsGpuAdapter> adapters)
    {
        var result = new int[adapters.Count];
        Array.Fill(result, Added);
        var taken = new bool[devices.Count];

        for (var adapter = 0; adapter < adapters.Count; adapter++)
        {
            for (var device = 0; device < devices.Count; device++)
            {
                if (!Free(device) || devices[device].InstanceId is not { } id ||
                    !string.Equals(PlugAndPlayDevices.NormalizeInstanceId(id), adapters[adapter].InstanceId,
                        StringComparison.OrdinalIgnoreCase)) continue;
                Take(adapter, device);
                break;
            }
        }

        for (var adapter = 0; adapter < adapters.Count; adapter++)
        {
            if (result[adapter] != Added) continue;
            var name = HomeDashboardFactory.NormalizeAdapterName(adapters[adapter].Name);
            for (var device = 0; device < devices.Count; device++)
            {
                if (!Free(device) || !string.Equals(HomeDashboardFactory.NormalizeAdapterName(devices[device].Model), name,
                        StringComparison.OrdinalIgnoreCase)) continue;
                Take(adapter, device);
                break;
            }
        }

        for (var adapter = 0; adapter < adapters.Count; adapter++)
        {
            var vendor = adapters[adapter].Vendor;
            if (result[adapter] != Added || vendor is HardwareVendor.Unknown) continue;
            var candidates = Enumerable.Range(0, devices.Count).Where(device => Free(device) && devices[device].Vendor == vendor).ToArray();
            var open = Enumerable.Range(0, adapters.Count).Count(other => result[other] == Added && adapters[other].Vendor == vendor);
            if (candidates.Length == 1 && open == 1) Take(adapter, candidates[0]);
        }

        for (var adapter = 0; adapter < adapters.Count; adapter++)
        {
            var vendor = adapters[adapter].Vendor;
            if (result[adapter] == Added && Enumerable.Range(0, devices.Count).Any(device =>
                    Free(device) && (devices[device].Vendor == vendor || devices[device].Vendor is HardwareVendor.Unknown)))
                result[adapter] = Unsure;
        }

        return result;

        bool Free(int device) => !taken[device] && devices[device].Kind is not DeviceKind.Cpu;

        void Take(int adapter, int device)
        {
            result[adapter] = device;
            taken[device] = true;
        }
    }

    private DeviceTelemetry Asleep(Work item, DateTimeOffset now)
    {
        // Powered down: nothing is asked, so nothing wakes it. Next time it wakes, its rest starts over.
        State(item.Adapter).Gate.ShouldPoll(false, now);
        var device = item.Device ?? New(item.Adapter);
        return device with { UsagePercent = device.UsagePercent ?? 0, PowerWatts = device.PowerWatts ?? 0, Asleep = true };
    }

    private DeviceTelemetry Awake(Work item, IReadOnlyDictionary<string, double> usage, DateTimeOffset now)
    {
        var device = item.Device ?? New(item.Adapter);
        var busy = device.UsagePercent ?? (usage.TryGetValue(item.Adapter.InstanceId, out var counted) ? (double?)counted : null);
        var driver = item.Needs.Driver ? AskDriver(item, busy, now) : WindowsGpuDriverReading.Empty;
        var memory = item.Needs.Memory && item.Powered == true
            ? Try(() => source.ReadDedicatedMemoryMegabytes(item.Adapter), null)
            : null;
        return device with
        {
            UsagePercent = busy,
            TemperatureC = device.TemperatureC ?? (item.Needs.Temperature ? driver.TemperatureC : null),
            FrequencyMhz = device.FrequencyMhz ?? (item.Needs.Frequency ? driver.FrequencyMhz : null),
            DedicatedMemoryMegabytes = device.DedicatedMemoryMegabytes ?? memory
        };
    }

    private WindowsGpuDriverReading AskDriver(Work item, double? usage, DateTimeOffset now)
    {
        var state = State(item.Adapter);
        // An integrated GPU stays powered with the display; asking it keeps nothing awake.
        if (item.Adapter.Kind is DeviceKind.IntegratedGpu)
            return Remember(state, Try(() => source.ReadDriver(item.Adapter), WindowsGpuDriverReading.Empty), now);

        // A discrete GPU only while Windows says it is powered, and only while its usage shows when
        // it idles, so that it can be left alone to power down.
        if (item.Powered != true || usage is null) return WindowsGpuDriverReading.Empty;
        if (!state.Gate.ShouldPoll(true, now)) return state.Last;
        var reading = Remember(state, Try(() => source.ReadDriver(item.Adapter), WindowsGpuDriverReading.Empty), now);
        state.Gate.Observe(usage, now);
        return reading;
    }

    /// <summary>
    /// A driver can report no clock at the instant its engine is power-gated between frames (seen on
    /// Intel's integrated GPU at 30 % load); the figure from a moment ago is truer than N/A flickering in.
    /// </summary>
    private static WindowsGpuDriverReading Remember(AdapterState state, WindowsGpuDriverReading reading, DateTimeOffset now)
    {
        if (reading.TemperatureC is not null) state.TemperatureAt = now;
        if (reading.FrequencyMhz is not null) state.FrequencyAt = now;
        state.Last = new(
            reading.TemperatureC ?? (now - state.TemperatureAt <= Hold ? state.Last.TemperatureC : null),
            reading.FrequencyMhz ?? (now - state.FrequencyAt <= Hold ? state.Last.FrequencyMhz : null));
        return state.Last;
    }

    private void ReportOnce(Work item, DeviceTelemetry filled)
    {
        var state = State(item.Adapter);
        if (state.Reported) return;
        var before = item.Device;
        var fields = new List<string>(5);
        if (filled.Asleep && before?.Asleep != true) fields.Add("asleep");
        if (before?.UsagePercent is null && filled.UsagePercent is not null) fields.Add("usage");
        if (before?.TemperatureC is null && filled.TemperatureC is not null) fields.Add("temperature");
        if (before?.FrequencyMhz is null && filled.FrequencyMhz is not null) fields.Add("clock");
        if (before?.DedicatedMemoryMegabytes is null && filled.DedicatedMemoryMegabytes is not null) fields.Add("memory");
        if (fields.Count == 0) return;
        state.Reported = true;
        try
        {
            diagnostic?.Invoke("telemetry-gpu",
                $"{item.Adapter.Name}: {(before is null ? "not listed by the sensor library; " : "")}from Windows: {string.Join(", ", fields)}");
        }
        catch (Exception) { /* Logging never changes a reading. */ }
    }

    private AdapterState State(WindowsGpuAdapter adapter)
    {
        if (!states.TryGetValue(adapter.InstanceId, out var state)) states[adapter.InstanceId] = state = new();
        return state;
    }

    private static DeviceTelemetry New(WindowsGpuAdapter adapter) =>
        new(adapter.Kind, adapter.Name, null, null, null, null, Vendor: adapter.Vendor, InstanceId: adapter.InstanceId);

    private static T Try<T>(Func<T> read, T fallback)
    {
        try { return read(); }
        catch (Exception) { return fallback; }
    }

    private readonly record struct Needs(bool Usage, bool Temperature, bool Frequency, bool Memory)
    {
        public bool Any => Usage || Temperature || Frequency || Memory;

        public bool Driver => Temperature || Frequency;

        /// <param name="kind">From the adapter's PCI facts, not the library's guess.</param>
        public static Needs Of(DeviceTelemetry? device, DeviceKind kind)
        {
            // An integrated GPU's temperature is the processor's, which its row already shows,
            // and it has no memory of its own to report.
            var integrated = kind is DeviceKind.IntegratedGpu;
            if (device is null) return new(true, !integrated, true, !integrated);
            return new(
                device.UsagePercent is null,
                !integrated && !device.Asleep && device.TemperatureC is null,
                !device.Asleep && device.FrequencyMhz is null,
                !integrated && device.DedicatedMemoryMegabytes is null);
        }
    }

    private sealed class Work(WindowsGpuAdapter adapter, int deviceIndex, DeviceTelemetry? device, Needs needs)
    {
        public WindowsGpuAdapter Adapter { get; } = adapter;
        public int DeviceIndex { get; } = deviceIndex;
        public DeviceTelemetry? Device { get; } = device;
        public Needs Needs { get; } = needs;
        public bool? Powered { get; set; }
    }

    private sealed class AdapterState
    {
        public DiscreteGpuPollGate Gate { get; } = new();
        public WindowsGpuDriverReading Last { get; set; } = WindowsGpuDriverReading.Empty;
        public DateTimeOffset TemperatureAt { get; set; } = DateTimeOffset.MinValue;
        public DateTimeOffset FrequencyAt { get; set; } = DateTimeOffset.MinValue;
        public bool Reported { get; set; }
    }
}
