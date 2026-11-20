using RevoHelper.Core.Hardware;

namespace RevoHelper.Core.Dashboard;

public static class HomeDashboardFactory
{
    private const string UnknownReading = "N/A";

    private static readonly IReadOnlyList<PerformanceProfile> Profiles =
    [
        new("office", "办公", "低噪音与低功耗"),
        new("balanced", "平衡", "日常性能与温度平衡"),
        new("turbo", "狂暴", "静音 / 高能两种策略"),
        new("custom", "自定义", "保存你的专属调度")
    ];

    /// <summary>Names only, as offline previews have them; the vendor is read from the name.</summary>
    public static HomeDashboard Create(
        MachineIdentity identity,
        IEnumerable<string> displayAdapterNames) =>
        CreateFromAdapters(identity, displayAdapterNames.Select(name => new DisplayAdapter(name, HardwareVendors.FromName(name))));

    public static HomeDashboard CreateFromAdapters(
        MachineIdentity identity,
        IEnumerable<DisplayAdapter> displayAdapters)
    {
        var devices = new List<DeviceStatus>
        {
            CreateDevice(
                DeviceKind.Cpu,
                "CPU",
                CleanCpuModelName(identity.ProcessorName),
                ProcessorDetails(identity),
                HardwareVendor.Unknown)
        };

        var adapters = displayAdapters
            .Where(adapter => !string.IsNullOrWhiteSpace(adapter.Name))
            .Select(adapter => adapter with { Name = CleanModelName(adapter.Name) })
            .DistinctBy(adapter => adapter.Name, StringComparer.OrdinalIgnoreCase)
            .Select(adapter => (Adapter: adapter, Kind: ClassifyAdapter(adapter)))
            .OrderBy(item => AdapterSortOrder(item.Kind))
            .ThenBy(item => item.Adapter.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var (adapter, kind) in adapters)
        {
            devices.Add(CreateDevice(
                kind,
                kind switch
                {
                    DeviceKind.IntegratedGpu => "核显",
                    DeviceKind.DiscreteGpu => "独显",
                    _ => "GPU"
                },
                adapter.Name,
                string.Empty,
                adapter.Vendor));
        }

        return new(identity, Profiles, devices);
    }

    private static DeviceStatus CreateDevice(
        DeviceKind kind,
        string label,
        string model,
        string details,
        HardwareVendor vendor) =>
        new(
            kind,
            label,
            model,
            details,
            UnknownReading,
            UnknownReading,
            UnknownReading,
            UnknownReading,
            vendor);

    private static string ProcessorDetails(MachineIdentity identity) =>
        identity.ProcessorCoreCount > 0 && identity.ProcessorThreadCount > 0
            ? $"{identity.ProcessorCoreCount} 核 · {identity.ProcessorThreadCount} 线程"
            : "自动识别";

    /// <summary>
    /// Hardware facts first, names last. Where the PCI location settles it (every Intel integrated
    /// GPU is at bus 0, device 2) no name is read, so a new product name never needs a new rule.
    /// </summary>
    public static DeviceKind ClassifyAdapter(DisplayAdapter adapter)
    {
        if (adapter.Integrated is { } integrated) return integrated ? DeviceKind.IntegratedGpu : DeviceKind.DiscreteGpu;
        var vendor = adapter.Vendor is HardwareVendor.Unknown ? HardwareVendors.FromName(adapter.Name) : adapter.Vendor;
        return vendor switch
        {
            // NVIDIA makes no integrated GPU for these processors.
            HardwareVendor.Nvidia => DeviceKind.DiscreteGpu,
            // AMD's discrete mobile GPUs are all "Radeon RX …" (or "Radeon Pro …"); its integrated
            // ones are "Radeon Graphics", "Radeon 780M", "Radeon 8060S" and so on.
            HardwareVendor.Amd => IsAmdDiscreteName(adapter.Name) ? DeviceKind.DiscreteGpu : DeviceKind.IntegratedGpu,
            // Only reached when Windows gave no PCI location for the Intel adapter.
            HardwareVendor.Intel => GpuNames.IsDiscreteIntelArc(adapter.Name) ? DeviceKind.DiscreteGpu : DeviceKind.IntegratedGpu,
            _ => DeviceKind.UnknownGpu
        };
    }

    private static bool IsAmdDiscreteName(string name) =>
        name.Contains("Radeon RX", StringComparison.OrdinalIgnoreCase) ||
        name.Contains(" RX ", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Radeon Pro", StringComparison.OrdinalIgnoreCase);

    private static int AdapterSortOrder(DeviceKind kind) =>
        kind switch
        {
            DeviceKind.DiscreteGpu => 0,
            DeviceKind.IntegratedGpu => 1,
            _ => 2
        };

    private static string CleanCpuModelName(string value)
    {
        var model = CleanModelName(value);
        return model.StartsWith("Intel ", StringComparison.OrdinalIgnoreCase)
            ? model["Intel ".Length..]
            : model;
    }

    /// <summary>The name a GPU row shows; telemetry names go through the same cleanup before matching.</summary>
    public static string NormalizeAdapterName(string value) => CleanModelName(value);

    private static string CleanModelName(string value)
    {
        var cleaned = value
            .Replace("Intel(R)", "Intel", StringComparison.OrdinalIgnoreCase)
            .Replace("Core(TM)", "Core", StringComparison.OrdinalIgnoreCase)
            .Replace("NVIDIA ", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("(R)", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("(TM)", string.Empty, StringComparison.OrdinalIgnoreCase);

        return string.Join(' ', cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
