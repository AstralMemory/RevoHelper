using RevoHelper.Core.Dashboard;

namespace RevoHelper.App.Diagnostics;

/// <summary>Brief fits in one Bilibili message (500 characters); Full is for a file or a GitHub issue.</summary>
public enum DiagnosticReportDetail
{
    Brief,
    Full
}

/// <summary>Model and firmware names only: no serial numbers, user or computer names.</summary>
public sealed record DiagnosticMachine(string Manufacturer, string Model, string Family, string Baseboard,
    string Bios, string EmbeddedController, string Processor, string Windows);

/// <summary>One GPU as nvidia-smi reports it; "N/A" where the driver has no value.</summary>
public sealed record DiagnosticGpu(string Name, string Driver, string EnforcedW, string DefaultW, string MaximumW);

/// <summary>Whether runtime.log could be read, so "no failures" is never claimed for a log nobody read.</summary>
public enum DiagnosticLogState
{
    Read,
    NoFile,
    PartlyRead,
    Unreadable
}

/// <summary>What the system side read. Every part is read-only; a part that failed says why instead.</summary>
/// <param name="GpuFailure">A fixed reason, never a program's own output.</param>
/// <param name="RuntimeLog">runtime.log lines from the last 24 hours, oldest first, not yet cleaned.</param>
/// <param name="ChargeLog">The newest charge-limit log, cut down to numbers and fixed event names (full report only).</param>
/// <param name="GpuPowerReport">The GPU power report with the EC table (full report only).</param>
public sealed record DiagnosticSystemFacts(
    DiagnosticMachine? Machine,
    string Mode,
    bool? PowerOnline,
    IReadOnlyList<DiagnosticGpu> Gpus,
    string? GpuFailure,
    IReadOnlyList<string> RuntimeLog,
    DiagnosticLogState RuntimeLogState,
    IReadOnlyList<string> ChargeLog,
    string? GpuPowerReport);

/// <summary>A detected feature or CPU control route and why it is or is not available.</summary>
public sealed record DiagnosticFeature(string Name, string State, string Reason);

/// <summary>What the window already knows when the report is made.</summary>
public sealed record DiagnosticAppState(
    string Version,
    string SelectedProfile,
    int? BatteryPercent,
    string ChargeLimit,
    string ChargeLimitDetail,
    bool? OfficialConsoleRunning,
    string OemConnection,
    bool CapabilitiesDetected,
    IReadOnlyList<DiagnosticFeature> Features,
    IReadOnlyList<DiagnosticFeature> CpuBackends,
    IReadOnlyList<DeviceStatus> Devices,
    IReadOnlyList<string> Services,
    int CpuCores,
    int CpuThreads)
{
    public string OfficialConsoleStateText => OfficialConsoleRunning switch
    {
        true => "运行中", false => "未运行", null => "状态未知"
    };
}

public interface IDiagnosticReportSource
{
    /// <summary>Reads only. The full report also reads the EC table, which takes about 10 seconds.</summary>
    Task<DiagnosticSystemFacts> ReadAsync(DiagnosticReportDetail detail, CancellationToken cancellationToken);
}
