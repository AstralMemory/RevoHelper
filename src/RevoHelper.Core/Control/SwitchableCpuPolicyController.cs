namespace RevoHelper.Core.Control;

/// <summary>
/// Lets startup remain fast while a hardware-specific direct backend is probed off the UI thread.
/// Until a verified controller is installed, writes are explicitly rejected.
/// </summary>
public sealed class SwitchableCpuPolicyController : ICpuPolicyController, ICpuPolicyRestorePointProvider, IDisposable
{
    private ICpuPolicyController? controller;
    private bool disposed;

    public bool IsAvailable => Volatile.Read(ref controller) is not null;

    public void SetController(ICpuPolicyController verifiedController)
    {
        ArgumentNullException.ThrowIfNull(verifiedController);
        ObjectDisposedException.ThrowIf(disposed, this);

        var previous = Interlocked.Exchange(ref controller, verifiedController);
        if (!ReferenceEquals(previous, verifiedController) && previous is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    public ValueTask<CpuPolicy?> ReadCpuPolicyAsync(CancellationToken cancellationToken)
    {
        var current = Volatile.Read(ref controller);
        return current is null
            ? ValueTask.FromResult<CpuPolicy?>(null)
            : current.ReadCpuPolicyAsync(cancellationToken);
    }

    public ValueTask<ControlResult> ApplyCpuPolicyAsync(
        CpuPolicy policy,
        CancellationToken cancellationToken)
    {
        var current = Volatile.Read(ref controller);
        return current is null
            ? ValueTask.FromResult(ControlResult.Rejected(
                "CPU 功耗直连后端尚未通过运行时探测，未写入预设档位。"))
            : current.ApplyCpuPolicyAsync(policy, cancellationToken);
    }

    public ValueTask<CpuPolicyRestorePoint?> CaptureRestorePointAsync(CancellationToken cancellationToken)
    {
        var current = Volatile.Read(ref controller);
        return current is null ? ValueTask.FromResult<CpuPolicyRestorePoint?>(null)
            : CpuPolicyRestorePoint.CaptureAsync(current, cancellationToken);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        if (Interlocked.Exchange(ref controller, null) is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
