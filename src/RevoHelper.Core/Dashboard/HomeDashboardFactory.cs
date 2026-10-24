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

    public static HomeDashboard Create(
        MachineIdentity identity,
        IEnumerable<string> displayAdapterNames)
    {
        var devices = new List<DeviceStatus>
        {
            CreateDevice(
                DeviceKind.Cpu,
                "CPU",
                CleanCpuModelName(identity.ProcessorName),
                ProcessorDetails(identity))
        };

        var adapters = displayAdapterNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(CleanModelName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(AdapterSortOrder)
            .ThenBy(name => name, StringComparer.OrdinalIgnoreCase);

        foreach (var adapter in adapters)
        {
            var kind = ClassifyAdapter(adapter);
            devices.Add(CreateDevice(
                kind,
                kind switch
                {
                    DeviceKind.IntegratedGpu => "核显",
                    DeviceKind.DiscreteGpu => "独显",
                    _ => "GPU"
                },
                adapter,
                string.Empty));
        }

        return new(identity, Profiles, devices);
    }

    private static DeviceStatus CreateDevice(
        DeviceKind kind,
        string label,
        string model,
        string details) =>
        new(
            kind,
            label,
            model,
            details,
            UnknownReading,
            UnknownReading,
            UnknownReading,
            UnknownReading);

    private static string ProcessorDetails(MachineIdentity identity) =>
        identity.ProcessorCoreCount > 0 && identity.ProcessorThreadCount > 0
            ? $"{identity.ProcessorCoreCount} 核 · {identity.ProcessorThreadCount} 线程"
            : "自动识别";

    private static DeviceKind ClassifyAdapter(string name)
    {
        if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("GeForce", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Radeon RX", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Intel Arc", StringComparison.OrdinalIgnoreCase))
        {
            return DeviceKind.DiscreteGpu;
        }

        if (name.Contains("Intel", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Radeon Graphics", StringComparison.OrdinalIgnoreCase))
        {
            return DeviceKind.IntegratedGpu;
        }

        return DeviceKind.UnknownGpu;
    }

    private static int AdapterSortOrder(string name) =>
        ClassifyAdapter(name) switch
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
