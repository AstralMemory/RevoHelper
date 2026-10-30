using RevoHelper.Core.Control.ExperimentalCharge;

namespace RevoHelper.App.Charging;

/// <summary>
/// Keeps only this process's successful, manual charge-control intent. Power transitions
/// pause the raw session; they never relax its firmware, ownership, or recovery guards.
/// </summary>
public sealed class SessionChargeController : IExperimentalChargeController, IDisposable
{
    private readonly IExperimentalChargeController inner;
    private readonly Func<bool> isAcOnline;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;
    private readonly SemaphoreSlim operations = new(1, 1);
    private readonly object stateGate = new();
    private ExperimentalChargeStatus status;
    private int? desiredLimit;
    private bool powerAvailable = true;
    private bool powerPaused;
    private bool disposed;
    private long generation;
    private int mutationDepth;
    private CancellationTokenSource? pendingEnable;
    private string? message;

    public SessionChargeController(IExperimentalChargeController inner, Func<bool> isAcOnline,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this.isAcOnline = isAcOnline ?? throw new ArgumentNullException(nameof(isAcOnline));
        this.delay = delay ?? Task.Delay;
        // Do not adopt a raw session or query power/firmware during construction.
        status = inner.Status with { Enabled = false, PowerPaused = false };
        inner.StatusChanged += InnerStatusChanged;
    }

    public ExperimentalChargeStatus Status { get { lock (stateGate) return status; } }
    public event EventHandler? StatusChanged;

    public async Task EnableAsync(int limitPercent, CancellationToken cancellationToken = default)
    {
        if (limitPercent is not (60 or 80 or 100)) throw new ArgumentOutOfRangeException(nameof(limitPercent));
        long request;
        lock (stateGate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            if (inner.Status.RecoveryPending) throw new InvalidOperationException("请先确认原值恢复，不接管待恢复的限充会话。");
            if (powerPaused && desiredLimit is not null)
            {
                if (inner.Status.Busy) throw new InvalidOperationException("限充仍在恢复，请等待完成后再选择档位。");
                // This changes a previously authorized session's target, not hardware. A queued
                // resume reads the latest target only after its stable-power delay.
                desiredLimit = limitPercent;
                message = PausedMessage();
                RefreshLocked();
                request = -1;
            }
            else
            {
                if (!powerAvailable || ProbeAc() != true)
                    throw new InvalidOperationException("请接通电源后手动启用限充；未启用的会话不会自动开始。");
                request = ++generation;
                CancelPendingEnableLocked();
            }
        }
        Publish();
        if (request == -1) return;

        await operations.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (stateGate) { if (!IsCurrentLocked(request) || !powerAvailable) return; }
            await EnableInnerAsync(limitPercent, request, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            lock (stateGate)
            {
                if (generation == request)
                    ClearIntentLocked("限充启用未确认，保持关闭；不会自动重试，请检查恢复状态。");
                RefreshLocked();
            }
            Publish();
            throw;
        }
        finally { operations.Release(); }
    }

    public Task SetPowerAvailableAsync(bool available, CancellationToken cancellationToken = default)
    {
        long request;
        bool stopNeeded;
        lock (stateGate)
        {
            if (disposed) return Task.CompletedTask;
            if (!available)
            {
                powerAvailable = false;
                request = ++generation;
                CancelPendingEnableLocked();
                powerPaused = desiredLimit is not null;
                if (powerPaused) message = PausedMessage();
                var raw = inner.Status;
                stopNeeded = raw.Enabled || raw.Busy;
                if (stopNeeded) SignalInnerStopLocked("电源断开或系统暂停，停止当前限充并恢复原值。");
            }
            else
            {
                if (!powerAvailable) generation++;
                powerAvailable = true;
                request = generation;
                stopNeeded = false;
                if (powerPaused) message = PausedMessage();
            }
            RefreshLocked();
        }
        Publish();
        return available ? ResumeAfterPowerAsync(request, cancellationToken)
            : stopNeeded ? CompletePowerStopAsync(cancellationToken) : Task.CompletedTask;
    }

    private async Task CompletePowerStopAsync(CancellationToken token)
    {
        var held = false;
        try
        {
            await operations.WaitAsync(token).ConfigureAwait(false);
            held = true;
            await InvokeInnerAsync(() => inner.StopAsync("电源暂停，确认原值已恢复。", token)).ConfigureAwait(false);
            EnsureRestored();
            lock (stateGate) { if (powerPaused) message = PausedMessage(); RefreshLocked(); }
        }
        catch (Exception exception)
        {
            lock (stateGate)
            {
                ClearIntentLocked($"电源暂停后的恢复未确认；不会自动重启限充。{exception.Message}");
                RefreshLocked();
            }
        }
        finally { if (held) operations.Release(); Publish(); }
    }

    private async Task ResumeAfterPowerAsync(long request, CancellationToken token)
    {
        var held = false;
        CancellationTokenSource? cancellation = null;
        try
        {
            await operations.WaitAsync(token).ConfigureAwait(false);
            held = true;
            lock (stateGate)
            {
                if (!CanResumeLocked(request)) return;
                if (!ConfirmResumePowerLocked()) return;
            }
            if (inner.Status.Enabled || inner.Status.Busy)
                await InvokeInnerAsync(() => inner.StopAsync("重新接电前，确认旧限充会话已恢复。", token)).ConfigureAwait(false);
            EnsureRestored();
            lock (stateGate)
            {
                if (!CanResumeLocked(request)) return;
                cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
                pendingEnable = cancellation;
            }
            await delay(TimeSpan.FromSeconds(2), cancellation.Token).ConfigureAwait(false);
            int limit;
            lock (stateGate)
            {
                if (!CanResumeLocked(request) || !ConfirmResumePowerLocked()) return;
                limit = desiredLimit!.Value;
            }
            EnsureRestored();
            await EnableInnerAsync(limit, request, cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (IsSuperseded(request)) { }
        catch (Exception exception)
        {
            lock (stateGate)
            {
                if (generation == request)
                    ClearIntentLocked($"限充自动恢复未确认，保持关闭且不自动重试。{exception.Message}");
                RefreshLocked();
            }
        }
        finally
        {
            lock (stateGate)
            {
                if (ReferenceEquals(pendingEnable, cancellation)) pendingEnable = null;
                RefreshLocked();
            }
            cancellation?.Dispose();
            if (held) operations.Release();
            Publish();
        }
    }

    private async Task EnableInnerAsync(int limit, long request, CancellationToken token)
    {
        Task start;
        CancellationTokenSource cancellation;
        lock (stateGate)
        {
            if (!IsCurrentLocked(request) || !powerAvailable) return;
            if (!ConfirmResumePowerLocked())
                throw new OperationCanceledException("启用前电源状态已变化；未启用新的限充会话。");
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            pendingEnable = cancellation;
            mutationDepth++;
            try
            {
                // Start the raw async call before releasing the state lock. An off request then
                // signals an already-started raw generation, never one created after its stop.
                start = inner.EnableAsync(limit, cancellation.Token);
            }
            catch { mutationDepth--; pendingEnable = null; cancellation.Dispose(); throw; }
        }
        try
        {
            await start.ConfigureAwait(false);
            bool superseded;
            lock (stateGate)
            {
                superseded = !IsCurrentLocked(request) || !powerAvailable;
                if (!superseded)
                {
                    var raw = inner.Status;
                    if (!raw.Enabled || raw.Busy || raw.RecoveryPending || raw.LimitPercent != limit)
                        throw new InvalidOperationException("底层未确认目标限充会话已启用。");
                    desiredLimit = limit;
                    powerPaused = false;
                    message = null;
                    RefreshLocked();
                }
            }
            if (superseded)
            {
                // Even an uncooperative/late Enable completion cannot leave an obsolete raw
                // session active. The operation gate still excludes every newer raw enable.
                lock (stateGate) SignalInnerStopLocked("过期的限充启用请求已取消，恢复原值。");
                await inner.StopAsync("恢复已被停止或电源变化替代的限充请求。", CancellationToken.None).ConfigureAwait(false);
                EnsureRestored();
            }
        }
        finally
        {
            lock (stateGate)
            {
                mutationDepth--;
                if (ReferenceEquals(pendingEnable, cancellation)) pendingEnable = null;
                RefreshLocked();
            }
            cancellation.Dispose();
            Publish();
        }
    }

    public Task StopAsync(string reason, CancellationToken cancellationToken = default)
    {
        var request = ClearAndSignalStop(reason);
        return RunManualAsync(() => inner.StopAsync(reason, cancellationToken), request, cancellationToken);
    }

    public Task RecoverAsync(CancellationToken cancellationToken = default)
    {
        var request = ClearAndSignalStop("手动恢复，取消本会话的自动恢复意图。");
        return RunManualAsync(() => inner.RecoverAsync(cancellationToken), request, cancellationToken);
    }

    private async Task RunManualAsync(Func<Task> action, long request, CancellationToken token)
    {
        await operations.WaitAsync(token).ConfigureAwait(false);
        try
        {
            lock (stateGate) { if (!IsCurrentLocked(request)) return; }
            await InvokeInnerAsync(action).ConfigureAwait(false);
            EnsureRestored();
            lock (stateGate) { if (generation == request) message = null; RefreshLocked(); }
        }
        finally { operations.Release(); Publish(); }
    }

    public void RequestStop(string reason) => ClearAndSignalStop(reason);

    private long ClearAndSignalStop(string reason)
    {
        long request;
        lock (stateGate)
        {
            if (disposed) return -1;
            ClearIntentLocked("已请求关闭限充，正在确认原值恢复。");
            SignalInnerStopLocked(reason);
            request = generation;
            RefreshLocked();
        }
        Publish();
        return request;
    }

    private async Task InvokeInnerAsync(Func<Task> action)
    {
        Task operation;
        lock (stateGate)
        {
            mutationDepth++;
            try { operation = action(); }
            catch { mutationDepth--; throw; }
        }
        try { await operation.ConfigureAwait(false); }
        finally { lock (stateGate) { mutationDepth--; RefreshLocked(); } Publish(); }
    }

    private void InnerStatusChanged(object? sender, EventArgs args)
    {
        lock (stateGate)
        {
            if (disposed) return;
            // Always re-read the current raw record. A queued notification is not authority
            // to adopt an old session, turn a logical switch on, or recreate cleared intent.
            var raw = inner.Status;
            if (raw.RecoveryPending && desiredLimit is not null)
                ClearIntentLocked(raw.Message);
            else if (mutationDepth == 0 && desiredLimit is not null && !powerPaused && !raw.Enabled)
            {
                if (ProbeAc() == false)
                {
                    powerAvailable = false;
                    powerPaused = true;
                    generation++;
                    CancelPendingEnableLocked();
                    message = PausedMessage();
                }
                else ClearIntentLocked($"限充已安全停止，不自动重启。{raw.Message}");
            }
            RefreshLocked();
        }
        Publish();
    }

    private void SignalInnerStopLocked(string reason)
    {
        mutationDepth++;
        try { inner.RequestStop(reason); }
        finally { mutationDepth--; }
    }

    private bool? ProbeAc()
    {
        try { return isAcOnline(); }
        catch { return null; }
    }

    private bool ConfirmResumePowerLocked()
    {
        var ac = ProbeAc();
        if (ac == true) return true;
        if (ac == false)
        {
            powerAvailable = false;
            powerPaused = desiredLimit is not null;
            generation++;
            CancelPendingEnableLocked();
            message = powerPaused ? PausedMessage() : "电源已断开，限充未启用。";
        }
        else ClearIntentLocked("无法核对电源状态，保持关闭；不会自动重启限充。");
        RefreshLocked();
        return false;
    }

    private void EnsureRestored()
    {
        var raw = inner.Status;
        if (raw.Enabled || raw.Busy || raw.RecoveryPending)
            throw new InvalidOperationException("旧限充会话恢复尚未确认，不并行启用新会话。");
    }

    private bool IsCurrentLocked(long request) => !disposed && generation == request;
    private bool CanResumeLocked(long request) => IsCurrentLocked(request) && powerAvailable &&
        powerPaused && desiredLimit is not null && !inner.Status.RecoveryPending;
    private bool IsSuperseded(long request) { lock (stateGate) return !IsCurrentLocked(request); }
    private string PausedMessage() => inner.Status is { Enabled: true } or { Busy: true }
        ? "限充开关保持开启；正在安全暂停并恢复原值，完成后等待电源，不代表正在限制充电。"
        : powerAvailable
            ? "限充开关保持开启；已安全暂停，等待电源稳定后恢复，并非正在限制充电。"
            : "限充开关保持开启；已安全暂停，等待重新接通电源，并非正在限制充电。";

    private void ClearIntentLocked(string reason)
    {
        desiredLimit = null;
        powerPaused = false;
        generation++;
        CancelPendingEnableLocked();
        message = reason;
    }

    private void CancelPendingEnableLocked()
    {
        try { pendingEnable?.Cancel(); } catch (ObjectDisposedException) { }
    }

    private void RefreshLocked()
    {
        var raw = inner.Status;
        status = raw with
        {
            Enabled = !disposed && desiredLimit is not null,
            PowerPaused = !disposed && desiredLimit is not null && powerPaused,
            LimitPercent = desiredLimit ?? raw.LimitPercent,
            Message = message ?? raw.Message
        };
    }

    private void Publish()
    {
        try { StatusChanged?.Invoke(this, EventArgs.Empty); }
        catch (Exception) { /* UI observers must not interrupt restoration. */ }
    }

    public void Dispose()
    {
        lock (stateGate)
        {
            if (disposed) return;
            ClearIntentLocked("程序退出，限充已关闭并请求恢复原值。");
            disposed = true;
            SignalInnerStopLocked("程序退出，取消会话恢复意图。");
            inner.StatusChanged -= InnerStatusChanged;
            RefreshLocked();
        }
        Publish();
        // App composition owns disposal of the raw client after its existing shutdown path.
        // Do not dispose the semaphore while a bounded raw recovery may still be awaiting it.
    }
}
