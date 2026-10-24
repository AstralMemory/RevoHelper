using System.Diagnostics;

namespace RevoHelper.App.Lifecycle;

public sealed record ControlConflict(string DisplayName);

public sealed class ControlConflictDetector
{
    public const string OfficialConsoleDisplayName = "MECHREVO Control Center（机械革命官方控制台）";

    private static readonly IReadOnlyDictionary<string, string> KnownConflicts =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ControlCenterU"] = OfficialConsoleDisplayName,
            ["GamingCenterU"] = OfficialConsoleDisplayName,
            ["CCUWinUI"] = OfficialConsoleDisplayName,
            ["ThrottleStop"] = "ThrottleStop",
            ["L-Mechrevo"] = "L-Mechrevo",
            ["UXTU"] = "Universal x86 Tuning Utility",
            ["Universal x86 Tuning Utility"] = "Universal x86 Tuning Utility",
            ["UniversalX86TuningUtility"] = "Universal x86 Tuning Utility",
            ["XtuUiLauncher"] = "Intel XTU",
            ["XtuCLI"] = "Intel XTU",
            ["Intel XTU"] = "Intel XTU",
            ["Ryzen Controller"] = "Ryzen Controller",
            ["RyzenController"] = "Ryzen Controller",
            ["GHelper"] = "G-Helper",
            ["G-Helper"] = "G-Helper"
        };

    private readonly Func<IReadOnlyList<ProcessIdentity>> processSnapshot;

    public ControlConflictDetector()
        : this(SnapshotProcesses)
    {
    }

    public ControlConflictDetector(Func<IReadOnlyList<ProcessIdentity>> processSnapshot)
    {
        this.processSnapshot = processSnapshot;
    }

    public IReadOnlyList<ControlConflict> Detect()
    {
        try
        {
            return processSnapshot()
                .Select(process => KnownConflicts.TryGetValue(process.Name, out var displayName)
                    ? displayName
                    : null)
                .Where(displayName => displayName is not null)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(displayName => string.Equals(displayName, OfficialConsoleDisplayName, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .Select(displayName => new ControlConflict(displayName!))
                .ToArray();
        }
        catch
        {
            // A process may disappear or deny access while Windows is taking the snapshot.
            // Conflict detection is advisory and must never prevent RevoHelper from starting.
            return Array.Empty<ControlConflict>();
        }
    }

    public bool IsOfficialConsoleRunning() =>
        Detect().Any(conflict => string.Equals(
            conflict.DisplayName, OfficialConsoleDisplayName, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<ProcessIdentity> SnapshotProcesses() =>
        Process.GetProcesses()
            .Select(process => new ProcessIdentity(process.Id, process.ProcessName))
            .ToArray();
}
