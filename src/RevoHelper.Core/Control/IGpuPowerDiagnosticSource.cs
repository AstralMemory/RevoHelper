namespace RevoHelper.Core.Control;

/// <summary>A read-only report on what limits the discrete GPU's power, for the user to send to the developer.</summary>
/// <param name="Text">The full report.</param>
/// <param name="Summary">One short line for the page: the driver's limit in effect and the GPU's maximum.</param>
public sealed record GpuPowerDiagnosticReport(string Text, string Summary);

public interface IGpuPowerDiagnosticSource
{
    /// <summary>Reads only. Nothing is written to the EC, the OEM service or the driver.</summary>
    Task<GpuPowerDiagnosticReport> CollectAsync(CancellationToken cancellationToken);
}
