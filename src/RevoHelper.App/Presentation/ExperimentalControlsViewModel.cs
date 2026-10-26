using System.ComponentModel;
using RevoHelper.App.Settings;
using RevoHelper.Core.Control.DynamicPower;
using RevoHelper.Core.Telemetry;

namespace RevoHelper.App.Presentation;

public sealed class ExperimentalControlsViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IDynamicPowerController? dynamicPower;
    private readonly IBootWaterTaskManager? bootWater;
    private readonly Action<Action> dispatch;
    private readonly TimeProvider clock;
    private ITimer? watchdog;
    private long lastSampleTicks;
    private int observing;
    private int pausingForTelemetry;
    private int waterBusy;
    private int pausing;
    private volatile bool stopping;
    private bool dynamicBusy;
    private int notificationQueued;
    private DisplayState displayed;
    private string? operationError;
    private BootWaterTaskStatus waterStatus = new(BootWaterTaskState.NotInstalled, "点击检查，读取登录前任务状态。");

    public ExperimentalControlsViewModel(IDynamicPowerController? dynamicPower = null,
        IBootWaterTaskManager? bootWater = null, Action<Action>? dispatch = null, TimeProvider? timeProvider = null)
    {
        this.dynamicPower = dynamicPower; this.bootWater = bootWater;
        this.dispatch = dispatch ?? (action => action()); clock = timeProvider ?? TimeProvider.System;
        displayed = CaptureDisplayState();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public bool IsDynamicEnabled => dynamicPower?.Status.Enabled == true;
    public bool CanToggleDynamic => dynamicPower is not null && !dynamicBusy && !stopping && Volatile.Read(ref pausing) == 0;
    public bool HasRecoveryPending => dynamicPower?.Status.HasRecoveryPending == true;
    public string DynamicMessage => operationError ?? dynamicPower?.Status.Message ?? "动态节能尚未连接。";
    public string DynamicProgress => dynamicPower?.Status.IsLimited == true ? "当前临时上限：3 GHz（不提高已有更低限制）"
        : $"观察窗口：{dynamicPower?.Status.IdleSeconds ?? 0} / 600 秒 · 空闲 {dynamicPower?.Status.IdlePercent ?? 0}%";
    public bool CanManageBootWater => bootWater is not null && Volatile.Read(ref waterBusy) == 0;
    public string BootWaterMessage => waterStatus.Message;
    public string BootWaterLastResult => waterStatus.LastConnectionResult ?? "尚无开机连接记录。";

    public async Task SetDynamicEnabledAsync(bool enabled)
    {
        if (!CanToggleDynamic || dynamicPower is null) return;
        dynamicBusy = true; operationError = null; Notify();
        try
        {
            Interlocked.Exchange(ref lastSampleTicks, clock.GetTimestamp());
            await Task.Run(() => stopping && enabled ? Task.CompletedTask
                : dynamicPower.SetEnabledAsync(enabled, CancellationToken.None)).ConfigureAwait(false);
            if (IsDynamicEnabled && !stopping)
                watchdog ??= clock.CreateTimer(_ => CheckTelemetryAge(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }
        catch (Exception ex) { operationError = $"动态节能未完成：{ex.Message}"; }
        finally { dynamicBusy = false; Notify(); }
    }

    public async Task RecoverAsync()
    {
        if (dynamicPower is null || dynamicBusy) return;
        dynamicBusy = true; Notify();
        operationError = null;
        try { await Task.Run(() => dynamicPower.RecoverAsync(CancellationToken.None)).ConfigureAwait(false); }
        catch (Exception ex) { operationError = $"恢复未完成：{ex.Message}"; }
        finally { dynamicBusy = false; Notify(); }
    }

    public async Task<bool> PauseForManualControlAsync()
    {
        if (dynamicPower is null) return true;
        Interlocked.Increment(ref pausing); operationError = null; Notify();
        try
        {
            await Task.Run(() => dynamicPower.PrepareForExternalChangeAsync(
                "性能设置已改变，动态节能保持开启并重新观察。", CancellationToken.None)).ConfigureAwait(false);
        }
        catch (Exception ex) { operationError = $"频率恢复未完成：{ex.Message}"; }
        finally { Interlocked.Decrement(ref pausing); Notify(); }
        return !HasRecoveryPending && operationError is null;
    }

    public async Task PauseAsync(string reason, CancellationToken token)
    {
        if (dynamicPower is null) return;
        Interlocked.Increment(ref pausing); operationError = null; Notify();
        try { await Task.Run(() => dynamicPower.PauseAsync(reason, token), token).ConfigureAwait(false); }
        catch (Exception ex) { operationError = $"频率恢复未完成：{ex.Message}"; }
        finally { Interlocked.Decrement(ref pausing); Notify(); }
    }

    public async Task ObserveAsync(HardwareTelemetrySnapshot sample)
    {
        Interlocked.Exchange(ref lastSampleTicks, clock.GetTimestamp());
        if (stopping || dynamicPower is null || !IsDynamicEnabled || Volatile.Read(ref pausingForTelemetry) != 0) return;
        if (Interlocked.Increment(ref observing) > 4)
        {
            Interlocked.Decrement(ref observing);
            await PauseForTelemetryAsync("遥测处理积压，动态节能已暂停。").ConfigureAwait(false);
            return;
        }
        // Called by the background telemetry pump, never the UI dispatcher. Publish every
        // incoming sample to the controller immediately so a busy sample cancels a pending cap.
        try { await dynamicPower.ObserveAsync(sample, CancellationToken.None).ConfigureAwait(false); }
        catch (Exception ex)
        {
            await PauseAsync($"遥测处理异常：{ex.Message}", CancellationToken.None).ConfigureAwait(false);
        }
        finally { Interlocked.Decrement(ref observing); Notify(); }
    }

    public Task RefreshBootWaterAsync() => WaterAsync(manager => manager.ReadAsync(CancellationToken.None));
    public Task PauseAutomaticallyAsync(string reason) => PauseForTelemetryAsync(reason);
    public Task InstallBootWaterAsync() => WaterAsync(manager => manager.InstallAsync(CancellationToken.None));
    public Task RemoveBootWaterAsync() => WaterAsync(manager => manager.RemoveAsync(CancellationToken.None));

    private async Task WaterAsync(Func<IBootWaterTaskManager, Task<BootWaterTaskStatus>> action)
    {
        if (bootWater is null || Interlocked.CompareExchange(ref waterBusy, 1, 0) != 0) return;
        Notify();
        try { waterStatus = await action(bootWater).ConfigureAwait(false); }
        catch (Exception ex) { waterStatus = new(BootWaterTaskState.Error, $"水冷任务操作失败：{ex.Message}"); }
        finally { Interlocked.Exchange(ref waterBusy, 0); Notify(); }
    }

    private void CheckTelemetryAge()
    {
        if (!stopping && IsDynamicEnabled && clock.GetElapsedTime(Interlocked.Read(ref lastSampleTicks)) > TimeSpan.FromSeconds(4))
            _ = PauseForTelemetryAsync("遥测已中断，动态节能已暂停。");
    }

    private async Task PauseForTelemetryAsync(string reason)
    {
        if (!IsDynamicEnabled || Interlocked.CompareExchange(ref pausingForTelemetry, 1, 0) != 0) return;
        try { if (IsDynamicEnabled) await PauseAsync(reason, CancellationToken.None).ConfigureAwait(false); }
        finally { Interlocked.Exchange(ref pausingForTelemetry, 0); }
    }

    public async Task<bool> ShutdownAsync(CancellationToken token)
    {
        stopping = true;
        watchdog?.Dispose();
        await PauseAsync("程序退出，恢复原始频率限制。", token).ConfigureAwait(false);
        return operationError is null && !HasRecoveryPending && dynamicPower?.Status.IsLimited != true;
    }

    private void Notify()
    {
        // Only coalesce display notifications. Every control sample still reaches the backend.
        if (Interlocked.Exchange(ref notificationQueued, 1) != 0) return;
        dispatch(() =>
        {
            Interlocked.Exchange(ref notificationQueued, 0);
            var previous = displayed;
            displayed = CaptureDisplayState();
            Changed(previous.DynamicEnabled != displayed.DynamicEnabled, nameof(IsDynamicEnabled));
            Changed(previous.CanToggle != displayed.CanToggle, nameof(CanToggleDynamic));
            Changed(previous.RecoveryPending != displayed.RecoveryPending, nameof(HasRecoveryPending));
            Changed(previous.Message != displayed.Message, nameof(DynamicMessage));
            Changed(previous.Progress != displayed.Progress, nameof(DynamicProgress));
            Changed(previous.CanManageWater != displayed.CanManageWater, nameof(CanManageBootWater));
            Changed(previous.WaterMessage != displayed.WaterMessage, nameof(BootWaterMessage));
            Changed(previous.WaterResult != displayed.WaterResult, nameof(BootWaterLastResult));
        });
    }

    private void Changed(bool changed, string property)
    {
        if (changed) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }

    private DisplayState CaptureDisplayState() => new(IsDynamicEnabled, CanToggleDynamic,
        HasRecoveryPending, DynamicMessage, DynamicProgress, CanManageBootWater, BootWaterMessage, BootWaterLastResult);

    private readonly record struct DisplayState(bool DynamicEnabled, bool CanToggle, bool RecoveryPending,
        string Message, string Progress, bool CanManageWater, string WaterMessage, string WaterResult);
    public void Dispose() { stopping = true; watchdog?.Dispose(); }
}
