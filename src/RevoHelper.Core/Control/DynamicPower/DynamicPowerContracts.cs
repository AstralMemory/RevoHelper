using RevoHelper.Core.Telemetry;

namespace RevoHelper.Core.Control.DynamicPower;

public interface IDynamicPowerController
{
    DynamicPowerStatus Status { get; }
    Task SetEnabledAsync(bool enabled, CancellationToken token);
    Task ObserveAsync(HardwareTelemetrySnapshot sample, CancellationToken token);
    Task RecoverAsync(CancellationToken token);
    Task PauseAsync(string reason, CancellationToken token);
    Task PrepareForExternalChangeAsync(string reason, CancellationToken token);
}

public sealed record DynamicPowerStatus(bool Enabled, string Message, int IdleSeconds, bool IsLimited,
    bool HasRecoveryPending, int IdlePercent = 0);

public sealed record DynamicPowerContext(Guid Scheme, bool IsAc);

public sealed record DynamicFrequencyValues(uint Class0Mhz, uint Class1Mhz, uint Class2Mhz)
{
    public uint this[int processorClass] => processorClass switch
    { 0 => Class0Mhz, 1 => Class1Mhz, 2 => Class2Mhz, _ => throw new ArgumentOutOfRangeException(nameof(processorClass)) };
}

public sealed record DynamicPowerLease(int SchemaVersion, DynamicPowerContext Context,
    DynamicFrequencyValues Original, DynamicFrequencyValues Expected);

public interface IDynamicPowerLeaseStore
{
    Task<DynamicPowerLease?> LoadAsync(CancellationToken token);
    Task SaveAsync(DynamicPowerLease lease, CancellationToken token);
    Task ClearAsync(CancellationToken token);
}

public interface IDynamicFrequencyBackend
{
    Task<DynamicPowerContext> GetContextAsync(CancellationToken token);
    Task<DynamicFrequencyValues> ReadAsync(DynamicPowerContext context, CancellationToken token);
    Task WriteAsync(DynamicPowerContext context, int processorClass, uint mhz, CancellationToken token);
    Task RefreshAsync(DynamicPowerContext context, CancellationToken token);
}
