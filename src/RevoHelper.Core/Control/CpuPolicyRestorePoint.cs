namespace RevoHelper.Core.Control;

/// <summary>
/// Session-only restore point. The backend keeps its full native state rather
/// than squeezing restore data into the two-value UI policy.
/// </summary>
public sealed class CpuPolicyRestorePoint(
    CpuPolicy policy,
    Func<CancellationToken, ValueTask<ControlResult>> restore)
{
    public CpuPolicy Policy { get; } = policy;
    public ValueTask<ControlResult> RestoreAsync(CancellationToken cancellationToken) => restore(cancellationToken);

    public static async ValueTask<CpuPolicyRestorePoint?> CaptureAsync(
        ICpuPolicyController controller, CancellationToken cancellationToken)
    {
        if (controller is ICpuPolicyRestorePointProvider provider)
            return await provider.CaptureRestorePointAsync(cancellationToken).ConfigureAwait(false);
        var current = await controller.ReadCpuPolicyAsync(cancellationToken).ConfigureAwait(false);
        return current is null ? null : new(current, token => controller.ApplyCpuPolicyAsync(current, token));
    }
}

public interface ICpuPolicyRestorePointProvider
{
    ValueTask<CpuPolicyRestorePoint?> CaptureRestorePointAsync(CancellationToken cancellationToken);
}
