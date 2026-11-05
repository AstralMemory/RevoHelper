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

    // Only a direct user action may replace an unreadable recovery baseline.
    // Ordinary profile synchronization and automatic recovery never call this.
    ValueTask<ControlResult> PrepareForExplicitDisableAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(new ControlResult(true, "频率限制可以关闭。"));
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

    // Preserve unreadable evidence before replacing it with an explicit reset intent.
    ValueTask ArchiveCorruptAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException("当前恢复记录存储不支持保存损坏记录副本。");
}
