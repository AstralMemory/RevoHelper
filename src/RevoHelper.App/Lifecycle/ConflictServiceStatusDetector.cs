using System.Management;

namespace RevoHelper.App.Lifecycle;

public sealed record WindowsServiceIdentity(string Name, string State, string StartMode);

public sealed record ConflictServiceStatus(
    string ServiceName,
    string DisplayName,
    bool IsRunning,
    bool IsInstalled,
    string StatusText)
{
    // Preserve the old constructor for in-memory controllers and callers. Real
    // observations always supply the native state, including pending/unknown.
    public string NativeState { get; init; } = IsInstalled
        ? IsRunning ? "Running" : "Stopped"
        : "Missing";

    public bool IsTransitioning => NativeState.EndsWith(" Pending", StringComparison.OrdinalIgnoreCase);
}

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
                    "状态不可用") { NativeState = "Unknown" })
                .ToArray();
        }

        return KnownServices
            .Select(service => services.TryGetValue(service.ServiceName, out var identity)
                ? new ConflictServiceStatus(
                    service.ServiceName,
                    service.DisplayName,
                    string.Equals(identity.State.Trim(), "Running", StringComparison.OrdinalIgnoreCase),
                    true,
                    StateText(identity.State))
                {
                    NativeState = string.IsNullOrWhiteSpace(identity.State) ? "Unknown" : identity.State.Trim()
                }
                : new ConflictServiceStatus(
                    service.ServiceName,
                    service.DisplayName,
                    false,
                    false,
                    "未安装") { NativeState = "Missing" })
            .ToArray();
    }

    private static string StateText(string state) => state.Trim().ToUpperInvariant() switch
    {
        "RUNNING" => "正在运行",
        "STOPPED" => "已停止",
        "START PENDING" => "正在启动",
        "STOP PENDING" => "正在停止",
        "CONTINUE PENDING" => "正在继续",
        "PAUSE PENDING" => "正在暂停",
        "PAUSED" => "已暂停",
        _ => "状态不可用"
    };

    private static IReadOnlyList<WindowsServiceIdentity> SnapshotServices()
    {
        // Only manual operations poll this snapshot. Limit the query and its
        // wait instead of repeatedly enumerating every Windows service.
        using var searcher = new ManagementObjectSearcher(
            "SELECT Name, State, StartMode FROM Win32_Service WHERE " +
            "Name='XtuService' OR Name='dptftcs' OR Name='ipfsvc'");
        searcher.Options.Timeout = TimeSpan.FromSeconds(2);
        using var results = searcher.Get();
        var snapshot = new List<WindowsServiceIdentity>();
        foreach (ManagementObject service in results)
        {
            using (service)
                snapshot.Add(new(
                    service["Name"]?.ToString()?.Trim() ?? string.Empty,
                    service["State"]?.ToString()?.Trim() ?? string.Empty,
                    service["StartMode"]?.ToString()?.Trim() ?? string.Empty));
        }
        return snapshot;
    }
}
