namespace RevoHelper.Core.Control;

/// <summary>
/// Orders the app's own frequency reads and writes around the two recoveries that must not be
/// raced. Both wait until the start-up recovery attempt (Boost, dynamic, manual) has
/// finished, so a profile write is never interleaved with it and a
/// read-compare-write never compares against values the recovery is about to restore. After
/// <see cref="BeginShutdown"/> no write starts, so nothing can land after the exit recovery.
/// The recovery itself goes to the inner controller directly.
/// </summary>
public sealed class FrequencyWriteGate : IProcessorFrequencyLimitController
{
    private readonly IProcessorFrequencyLimitController inner;
    private readonly Task startupRecovery;
    private readonly TimeSpan startupWait;
    private readonly object sync = new();
    private bool shuttingDown;
    private int writesInFlight;
    private TaskCompletionSource idle = CompletedSource();

    public FrequencyWriteGate(
        IProcessorFrequencyLimitController inner,
        Task? startupRecovery = null,
        TimeSpan? startupWait = null)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this.startupRecovery = startupRecovery ?? Task.CompletedTask;
        this.startupWait = startupWait ?? TimeSpan.FromSeconds(10);
    }

    public async ValueTask<ProcessorFrequencyLimitPolicy> ReadAsync(CancellationToken cancellationToken)
    {
        // While the start-up recovery may still restore a value, that value is no baseline: a
        // schedule would roll back to it and a recheck would compare against it. Callers read
        // before they write, so failing here stops them before any hardware write.
        if (!await StartupRecoveryFinishedAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new TimeoutException(StartupRecoveryStillRunningReadMessage);
        }
        return await inner.ReadAsync(cancellationToken).ConfigureAwait(false);
    }

    public const string StartupRecoveryStillRunningReadMessage = "上次的频率恢复仍在进行，暂不读取频率设置。";
    public const string StartupRecoveryStillRunningWriteMessage = "上次的频率恢复仍在进行，暂不写入新的频率限制。";

    /// <summary>True when a failure reason comes from a read or write this gate deferred.</summary>
    public static bool IsStartupRecoveryDeferral(string reason) =>
        reason.Contains(StartupRecoveryStillRunningReadMessage, StringComparison.Ordinal) ||
        reason.Contains(StartupRecoveryStillRunningWriteMessage, StringComparison.Ordinal);

    public ValueTask<ControlResult> ApplyAsync(
        ProcessorFrequencyLimitPolicy policy,
        CancellationToken cancellationToken) =>
        RunWriteAsync(() => inner.ApplyAsync(policy, cancellationToken), cancellationToken);

    public ValueTask<ControlResult> PrepareForExplicitDisableAsync(CancellationToken cancellationToken) =>
        RunWriteAsync(() => inner.PrepareForExplicitDisableAsync(cancellationToken), cancellationToken);

    /// <summary>No write starts after this. A write already admitted still finishes.</summary>
    public void BeginShutdown()
    {
        lock (sync) shuttingDown = true;
    }

    /// <summary>Completes once no admitted write is running.</summary>
    public Task WaitForIdleAsync(CancellationToken cancellationToken)
    {
        Task pending;
        lock (sync) pending = writesInFlight == 0 ? Task.CompletedTask : idle.Task;
        return pending.WaitAsync(cancellationToken);
    }

    /// <summary>False only when the start-up recovery is still running after the wait.</summary>
    private async ValueTask<bool> StartupRecoveryFinishedAsync(CancellationToken cancellationToken)
    {
        if (startupRecovery.IsCompleted) return true;
        try
        {
            await startupRecovery.WaitAsync(startupWait, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The attempt has finished; its own result decides what it left behind.
            return true;
        }
    }

    private async ValueTask<ControlResult> RunWriteAsync(
        Func<ValueTask<ControlResult>> write,
        CancellationToken cancellationToken)
    {
        if (!await StartupRecoveryFinishedAsync(cancellationToken).ConfigureAwait(false))
        {
            return ControlResult.Rejected(StartupRecoveryStillRunningWriteMessage);
        }

        lock (sync)
        {
            if (shuttingDown)
            {
                return ControlResult.Rejected("RevoHelper 正在退出，不再写入新的频率限制。");
            }

            if (writesInFlight++ == 0)
            {
                idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        try
        {
            return await write().ConfigureAwait(false);
        }
        finally
        {
            lock (sync)
            {
                if (--writesInFlight == 0)
                {
                    idle.TrySetResult();
                }
            }
        }
    }

    private static TaskCompletionSource CompletedSource()
    {
        var source = new TaskCompletionSource();
        source.SetResult();
        return source;
    }
}
