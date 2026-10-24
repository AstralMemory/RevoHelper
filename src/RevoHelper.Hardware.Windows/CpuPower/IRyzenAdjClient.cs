namespace RevoHelper.Hardware.Windows.CpuPower;

// Native reads/writes use milliwatt precision. Only UI-facing CpuPolicy rounds
// to whole watts; restore points retain all three original native values.
public sealed record RyzenAdjPowerLimits(decimal StapmWatts, decimal SlowWatts, decimal FastWatts);

public interface IRyzenAdjClient : IDisposable
{
    bool IsAvailable { get; }

    string AvailabilityReason { get; }

    ValueTask<RyzenAdjPowerLimits?> ReadPowerLimitsAsync(CancellationToken cancellationToken);

    ValueTask<bool> ApplyPowerLimitsAsync(
        RyzenAdjPowerLimits limits,
        CancellationToken cancellationToken);
}
