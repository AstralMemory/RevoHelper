using RevoHelper.Hardware.Windows.Discovery;

namespace RevoHelper.App.Lifecycle;

public sealed record WindowsServiceIdentity(string Name, string State, string StartMode);

public sealed record ConflictServiceStatus(
    string ServiceName,
    string DisplayName,
    bool IsRunning,
    bool IsInstalled,
    string StatusText);

public sealed class ConflictServiceStatusDetector
{
    private static readonly (string ServiceName, string DisplayName)[] KnownServices =
    [
        ("XtuService", "Intel XTU Service"),
        ("dptftcs", "Intel Dynamic Tuning"),
        ("ipfsvc", "Intel Innovation Platform Framework")
    ];

    private readonly Func<IReadOnlyList<WindowsServiceIdentity>> serviceSnapshot;

    public ConflictServiceStatusDetector()
        : this(SnapshotServices)
    {
    }

    public ConflictServiceStatusDetector(Func<IReadOnlyList<WindowsServiceIdentity>> serviceSnapshot)
    {
        this.serviceSnapshot = serviceSnapshot;
    }

    public IReadOnlyList<ConflictServiceStatus> Detect()
    {
        IReadOnlyDictionary<string, WindowsServiceIdentity> services;
        try
        {
            services = serviceSnapshot()
                .GroupBy(service => service.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return KnownServices
                .Select(service => new ConflictServiceStatus(
                    service.ServiceName,
                    service.DisplayName,
                    false,
                    false,
                    "状态不可用"))
                .ToArray();
        }

        return KnownServices
            .Select(service => services.TryGetValue(service.ServiceName, out var identity)
                ? new ConflictServiceStatus(
                    service.ServiceName,
                    service.DisplayName,
                    string.Equals(identity.State, "Running", StringComparison.OrdinalIgnoreCase),
                    true,
                    string.Equals(identity.State, "Running", StringComparison.OrdinalIgnoreCase)
                        ? "正在运行"
                        : "已停止")
                : new ConflictServiceStatus(
                    service.ServiceName,
                    service.DisplayName,
                    false,
                    false,
                    "未安装"))
            .ToArray();
    }

    private static IReadOnlyList<WindowsServiceIdentity> SnapshotServices()
    {
        var query = new SystemManagementWmiQuery();
        return query.ReadAll("Win32_Service")
            .Where(row => row.TryGetValue("Name", out var name) &&
                          KnownServices.Any(service => string.Equals(
                              service.ServiceName,
                              name,
                              StringComparison.OrdinalIgnoreCase)))
            .Select(row => new WindowsServiceIdentity(
                row.GetValueOrDefault("Name", string.Empty),
                row.GetValueOrDefault("State", string.Empty),
                row.GetValueOrDefault("StartMode", string.Empty)))
            .ToArray();
    }
}
