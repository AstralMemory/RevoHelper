namespace RevoHelper.Core.Control;

/// <summary>Only the temporary MAXPROCSTATE compatibility guard is owned by this lease.</summary>
public sealed record ProcessorBoostCompatibilityLease(
    int Version, Guid Scheme, uint OriginalAcMaximum, uint OriginalDcMaximum,
    uint ExpectedAcMaximum, uint ExpectedDcMaximum);

public interface IProcessorBoostCompatibilityLeaseStore
{
    ValueTask<ProcessorBoostCompatibilityLease?> LoadAsync(CancellationToken cancellationToken);
    ValueTask SaveAsync(ProcessorBoostCompatibilityLease lease, CancellationToken cancellationToken);
    ValueTask ClearAsync(CancellationToken cancellationToken);
}
