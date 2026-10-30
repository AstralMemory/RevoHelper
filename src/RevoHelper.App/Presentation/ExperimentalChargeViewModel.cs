using System.ComponentModel;
using RevoHelper.Core.Control.ExperimentalCharge;

namespace RevoHelper.App.Presentation;

/// <summary>A session-only UI. Construction and draft selection never enable charging control.</summary>
public sealed class ExperimentalChargeViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IExperimentalChargeController? controller;
    private readonly Action<Action> dispatch;
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private int operations;
    private int notificationQueued;
    private int selectedLimitPercent = 80;
    private int stopGeneration;
    private int commandRevision;
    private volatile bool desiredEnabled;
    private volatile bool disposed;
    private volatile bool recoveryFailed;
    private string? operationError;

    public ExperimentalChargeViewModel(IExperimentalChargeController? controller = null,
        Action<Action>? dispatch = null)
    {
        this.controller = controller;
        this.dispatch = dispatch ?? (action => action());
        desiredEnabled = controller?.Status.Enabled == true;
        if (controller is not null) controller.StatusChanged += Controller_StatusChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public bool IsEnabled => controller?.Status.Enabled == true;
    public bool IsBusy => Volatile.Read(ref operations) != 0 || controller?.Status.Busy == true;
    public bool RecoveryPending => recoveryFailed || controller?.Status.RecoveryPending == true;
    public bool CanToggle => controller is not null && !disposed && !IsBusy && !RecoveryPending;
    public bool CanEnable => CanToggle && !IsEnabled;
    public bool CanStop => controller is not null && !disposed && !IsBusy && (IsEnabled || RecoveryPending);
    public bool CanRecover => controller is not null && !disposed && !IsBusy && RecoveryPending;
    public bool CanChooseLimit => !disposed && !IsBusy && !RecoveryPending;
    // Even logical 100 keeps native-care editing separate: turn the feature off first.
    public bool CanUseNativeCareModes => !IsEnabled && !IsBusy && !RecoveryPending;
    public bool IsSixtySelected => SelectedLimitPercent == 60;
    public bool IsEightySelected => SelectedLimitPercent == 80;
    public bool IsHundredSelected => SelectedLimitPercent == 100;
    public string StatusText => operationError ?? controller?.Status.Message ?? "限充未连接；默认关闭。";

    public int SelectedLimitPercent
    {
        get => Volatile.Read(ref selectedLimitPercent);
        set
        {
            if (value is not (60 or 80 or 100) || disposed || RecoveryPending || SelectedLimitPercent == value) return;
            // Preserve already queued valid slider events while an operation runs.
            // CanChooseLimit disables new UI interaction; this is only a local draft.
            Volatile.Write(ref selectedLimitPercent, value);
            Notify();
        }
    }

    public int SelectedLimitIndex
    {
        get => SelectedLimitPercent switch { 60 => 0, 100 => 2, _ => 1 };
        set
        {
            if (value is < 0 or > 2) return;
            SelectedLimitPercent = value switch { 0 => 60, 2 => 100, _ => 80 };
        }
    }

    public Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default) => enabled
        ? EnableAsync(cancellationToken)
        : StopAsync(cancellationToken: cancellationToken);

    public Task EnableAsync(CancellationToken cancellationToken = default)
    {
        if (!CanEnable || controller is null) return Task.CompletedTask;
        desiredEnabled = true;
        return QueueSelectedLimitAsync(cancellationToken);
    }

    public Task CommitSelectedLimitAsync(CancellationToken cancellationToken = default)
    {
        // Only an already-running enable may queue a slider change while the raw
        // status is off. A stale desired flag after a health stop is not consent
        // to restart the feature just because its draft slider was moved.
        if (controller is null || disposed || RecoveryPending ||
            (!IsEnabled && (!desiredEnabled || Volatile.Read(ref operations) == 0))) return Task.CompletedTask;
        desiredEnabled = true;
        return QueueSelectedLimitAsync(cancellationToken);
    }

    private Task QueueSelectedLimitAsync(CancellationToken cancellationToken)
    {
        var revision = Interlocked.Increment(ref commandRevision);
        var generation = Volatile.Read(ref stopGeneration);
        return RunAsync(async () =>
        {
            if (!IsCurrent()) return false;
            var limit = SelectedLimitPercent;
            if (controller!.Status is { Enabled: true, Busy: false, RecoveryPending: false } current && current.LimitPercent == limit)
                return true;
            if (!controller.Status.PowerPaused && (controller.Status.Enabled || controller.Status.Busy))
            {
                await controller.StopAsync("切换限充档位，先恢复已有实验限制。", cancellationToken).ConfigureAwait(false);
                if (controller.Status is { RecoveryPending: true } or { Enabled: true } or { Busy: true })
                    throw new InvalidOperationException("换档前恢复未确认；保持关闭并先恢复原值。");
            }
            if (!IsCurrent()) return false;
            if (controller.Status.RecoveryPending) throw new InvalidOperationException("存在待恢复记录，不启用新的充电档位。");
            // A newer queued commit reads the latest draft after this command
            // releases the gate. Stale commands never re-enable an old selection.
            await controller.EnableAsync(limit, cancellationToken).ConfigureAwait(false);
            if (!IsCurrent())
            {
                controller.RequestStop("本次限充请求已被更新选择或停止请求替代。");
                await controller.StopAsync("恢复已被替代的限充会话。", CancellationToken.None).ConfigureAwait(false);
                if (controller.Status is { RecoveryPending: true } or { Enabled: true } or { Busy: true })
                    throw new InvalidOperationException("旧限充会话尚未恢复；不启用排队的新档位。");
            }
            return true;
        }, "限充操作失败", cancellationToken);

        bool IsCurrent() => !disposed && desiredEnabled && !RecoveryPending
            && revision == Volatile.Read(ref commandRevision) && generation == Volatile.Read(ref stopGeneration);
    }

    public Task StopAsync(string reason = "用户关闭限充，尝试恢复原值。",
        CancellationToken cancellationToken = default)
    {
        if (controller is null) return Task.CompletedTask;
        var needsWait = IsEnabled || IsBusy || RecoveryPending;
        RequestStop(reason);
        if (!needsWait) return Task.CompletedTask;
        return RunAsync(async () =>
        {
            await controller.StopAsync(reason, cancellationToken).ConfigureAwait(false);
            if (controller.Status is { RecoveryPending: true } or { Enabled: true } or { Busy: true })
                throw new InvalidOperationException("关闭后的原值恢复尚未确认。");
            return true;
        }, "停止限充失败", cancellationToken);
    }

    public Task RecoverAsync(CancellationToken cancellationToken = default)
    {
        if (!CanRecover || controller is null) return Task.CompletedTask;
        desiredEnabled = false;
        Interlocked.Increment(ref stopGeneration);
        Interlocked.Increment(ref commandRevision);
        return RunAsync(async () =>
        {
            await controller.RecoverAsync(cancellationToken).ConfigureAwait(false);
            if (controller.Status is { RecoveryPending: true } or { Enabled: true } or { Busy: true })
                throw new InvalidOperationException("恢复操作结束，但原值仍未确认。");
            return true;
        }, "恢复限充失败", cancellationToken);
    }

    public void RequestStop(string reason)
    {
        desiredEnabled = false;
        Interlocked.Increment(ref stopGeneration);
        Interlocked.Increment(ref commandRevision);
        if (controller is null) return;
        try { controller.RequestStop(reason); }
        catch (Exception exception)
        {
            recoveryFailed = true;
            operationError = $"停止限充失败：{exception.Message}";
        }
        Notify();
    }

    private async Task RunAsync(Func<Task<bool>> action, string failure, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref operations);
        Notify();
        var entered = false;
        try
        {
            await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            entered = true;
            var completed = await Task.Run(action, cancellationToken).ConfigureAwait(false);
            if (completed)
            {
                operationError = null;
                recoveryFailed = controller?.Status.RecoveryPending == true;
            }
        }
        catch (Exception exception)
        {
            // A skipped/stale command cannot clear this latch. Only a successful
            // confirmed stop/recovery can unlock native-care or enable controls.
            desiredEnabled = false;
            recoveryFailed = true;
            operationError = $"{failure}：{exception.Message}";
        }
        finally
        {
            if (entered) operationGate.Release();
            Interlocked.Decrement(ref operations);
            Notify();
        }
    }

    private void Controller_StatusChanged(object? sender, EventArgs e)
    {
        // Restore/status messages describe the active session, not the user's
        // selected next value. Never roll the slider draft back to an old limit.
        Notify();
    }

    private void Notify()
    {
        if (disposed || Interlocked.Exchange(ref notificationQueued, 1) != 0) return;
        dispatch(() =>
        {
            Interlocked.Exchange(ref notificationQueued, 0);
            if (disposed) return;
            foreach (var property in new[] { nameof(IsEnabled), nameof(IsBusy), nameof(RecoveryPending),
                nameof(CanToggle), nameof(CanEnable), nameof(CanStop), nameof(CanRecover), nameof(CanChooseLimit),
                nameof(CanUseNativeCareModes), nameof(SelectedLimitPercent), nameof(SelectedLimitIndex),
                nameof(IsSixtySelected), nameof(IsEightySelected), nameof(IsHundredSelected), nameof(StatusText) })
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        });
    }

    public void Dispose()
    {
        if (disposed) return;
        // Suppress presentation notifications before RequestStop; in-flight work
        // still owns operationGate until its independent restoration completes.
        disposed = true;
        RequestStop("限充界面释放，尝试恢复原值。");
        if (controller is not null) controller.StatusChanged -= Controller_StatusChanged;
    }
}
