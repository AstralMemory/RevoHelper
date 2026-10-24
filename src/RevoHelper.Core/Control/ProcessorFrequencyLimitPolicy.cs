namespace RevoHelper.Core.Control;

public sealed record ProcessorFrequencyLimitPolicy(
    uint AcMaximumMhz,
    uint DcMaximumMhz,
    uint AcClass1MaximumMhz,
    uint DcClass1MaximumMhz,
    uint AcClass2MaximumMhz,
    uint DcClass2MaximumMhz)
{
    public static ProcessorFrequencyLimitPolicy Uniform(uint maximumMhz) =>
        new(maximumMhz, maximumMhz, maximumMhz, maximumMhz, maximumMhz, maximumMhz);
}

public interface IProcessorFrequencyLimitController
{
    ValueTask<ProcessorFrequencyLimitPolicy> ReadAsync(CancellationToken cancellationToken);

    ValueTask<ControlResult> ApplyAsync(
        ProcessorFrequencyLimitPolicy policy,
        CancellationToken cancellationToken);
}

public sealed record ProcessorFrequencyLimitLease(
    int SchemaVersion,
    Guid Scheme,
    ProcessorFrequencyLimitPolicy Original,
    ProcessorFrequencyLimitPolicy Expected);

public interface IProcessorFrequencyLimitLeaseStore
{
    ValueTask<ProcessorFrequencyLimitLease?> LoadAsync(CancellationToken cancellationToken);

    ValueTask SaveAsync(
        ProcessorFrequencyLimitLease lease,
        CancellationToken cancellationToken);

    ValueTask ClearAsync(CancellationToken cancellationToken);
}
