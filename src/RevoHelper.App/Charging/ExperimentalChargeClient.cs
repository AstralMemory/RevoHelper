using System.Diagnostics;
using System.IO;
using RevoHelper.Core.Control.ExperimentalCharge;

namespace RevoHelper.App.Charging;

/// <summary>Owns only processes, cancellation and display state; never EC I/O.</summary>
public sealed class ExperimentalChargeClient : IExperimentalChargeController, IDisposable
{
    private readonly SemaphoreSlim operations = new(1, 1);
    private readonly object stateGate = new();
    private readonly Func<ChargeSessionFiles?> findPendingRecovery;
    private ExperimentalChargeStatus status = new();
    private volatile ChargeSessionFiles? files;
    private Process? guardian;
    private EventWaitHandle? stopEvent;
    private CancellationTokenSource? monitorCancellation;
    private int stopGeneration;
    private int enableGeneration;
    private volatile bool stopRequested;
    private volatile bool disposed;

    // Injection is for memory-only policy tests. Construction never inspects
    // sessions, creates events, starts helpers or calls hardware.
    public ExperimentalChargeClient(Func<ChargeSessionFiles?>? findPendingRecovery = null) =>
        this.findPendingRecovery = findPendingRecovery ?? FindPending;
    public event EventHandler? StatusChanged;
    public ExperimentalChargeStatus Status { get { lock (stateGate) return status; } }

    public void InspectPendingRecovery()
    {
        // Read-only, explicit startup inspection. Never enables or recovers hardware.
        var pending = findPendingRecovery();
        if (pending is not null)
        {
            files = pending;
            SetStatus(new(RecoveryPending: true, Message: "发现未确认恢复的实验会话。请先使用恢复按钮，不自动启用。", SessionId: pending.SessionId));
        }
    }
    public async Task EnableAsync(int limitPercent, CancellationToken cancellationToken = default)
    {
        if (limitPercent is not (60 or 80 or 100)) throw new ArgumentOutOfRangeException(nameof(limitPercent));
        var request = Interlocked.Increment(ref enableGeneration);
        var generation = Volatile.Read(ref stopGeneration);
        await operations.WaitAsync(cancellationToken).ConfigureAwait(false);
        var ownedSessionTouched = false;
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (request != Volatile.Read(ref enableGeneration) || generation != Volatile.Read(ref stopGeneration)) return;
            if (Status.RecoveryPending) throw new InvalidOperationException("先恢复上一会话；不会接管未知原值。");
            if (Status.Enabled && Status.LimitPercent == limitPercent && !Status.Busy) return;
            if (Status.Enabled || Status.Busy)
            {
                ownedSessionTouched = true;
                SignalStop("正在切换充电档位，先恢复已有实验限制。");
                await StopCurrentSessionAsync(cancellationToken).ConfigureAwait(false);
                ownedSessionTouched = false;
            }
            InspectPendingRecovery();
            if (Status.RecoveryPending) throw new InvalidOperationException("先恢复上一会话；不会接管未知原值。");
            if (request != Volatile.Read(ref enableGeneration) || generation != Volatile.Read(ref stopGeneration)) return;
            cancellationToken.ThrowIfCancellationRequested();
            ClearCompletedSession();
            stopRequested = false;
            if (limitPercent == 100)
            {
                // 100 is managed pass-through, NOT an EC threshold. No helper,
                // event, platform override, or new firmware write is created.
                SetStatus(new(Enabled: true, LimitPercent: 100,
                    Message: "已开启：100% 不限制，按原生策略充电；未保持平台覆盖。"));
                if (request != Volatile.Read(ref enableGeneration) || generation != Volatile.Read(ref stopGeneration))
                    SignalStop("100% 透传启用请求已被停止请求替代。");
                return;
            }
            ownedSessionTouched = true;
            files = new(Guid.NewGuid().ToString("N"));
            stopEvent = new(false, EventResetMode.ManualReset, files.StopName);
            using var self = Process.GetCurrentProcess();
            SetStatus(new(Busy: true, LimitPercent: limitPercent, Message: "正在启动独立监护并检查本机固件。", SessionId: files.SessionId));
            guardian = ChargeSessionFiles.Launch("--experimental-charge-guardian", files.SessionId,
                limitPercent, self.Id, self.StartTime.ToUniversalTime().Ticks);
            if (generation != Volatile.Read(ref stopGeneration)) stopEvent.Set();
            monitorCancellation = new();
            _ = MonitorAsync(files, guardian, stopEvent, monitorCancellation.Token);
            var deadline = Stopwatch.StartNew();
            while (deadline.Elapsed < TimeSpan.FromSeconds(30))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (request != Volatile.Read(ref enableGeneration) || generation != Volatile.Read(ref stopGeneration))
                {
                    SignalStop("启用请求已被新的选择或停止请求替代。");
                    await StopCurrentSessionAsync(cancellationToken).ConfigureAwait(false);
                    return;
                }
                var wire = files.ReadStatus();
                if (wire is { Phase: "Active" }) { ApplyWire(wire, files.SessionId); return; }
                if (wire is { Phase: "Restored" }) { ApplyWire(wire, files.SessionId); return; }
                if (wire is { Phase: "Error" or "RecoveryPending" }) throw new InvalidOperationException(wire.Message);
                if (guardian.HasExited)
                    throw new InvalidOperationException(wire?.Message ?? "独立监护未启动，未确认启用。");
                await Task.Delay(200, cancellationToken).ConfigureAwait(false);
            }
            throw new TimeoutException("监护启动超时，已请求停止；请检查恢复状态。");
        }
        catch
        {
            if (ownedSessionTouched)
            {
                SignalStop("启用未完成，停止并恢复。");
                SetStatus(new(RecoveryPending: files is not null || Status.RecoveryPending, LimitPercent: limitPercent,
                    Message: "启用未完成，已请求恢复；请检查状态或使用恢复按钮。", SessionId: files?.SessionId));
            }
            throw;
        }
        finally { operations.Release(); }
    }

    public void RequestStop(string reason)
    {
        Interlocked.Increment(ref stopGeneration);
        Interlocked.Increment(ref enableGeneration);
        SignalStop(reason);
    }

    private void SignalStop(string reason)
    {
        stopRequested = true;
        if (Status is { Enabled: true, LimitPercent: 100, RecoveryPending: false } && files is null)
        {
            SetStatus(new(LimitPercent: 100, Message: "限充已关闭；100% 透传模式没有平台覆盖需要恢复。"));
            return;
        }
        try
        {
            if (stopEvent is not null) stopEvent.Set();
            // A record discovered during startup is not an owned live session.
            // Only explicit RecoverAsync signals that old session. Do not open
            // arbitrary pending-session events merely because this UI closes.
        }
        catch (ObjectDisposedException) { }
        if (Status.Enabled || Status.Busy)
            SetStatus(Status with { Enabled = false, Busy = true, Message = "正在停止限充并恢复原值。" });
    }
    public async Task StopAsync(string reason, CancellationToken cancellationToken = default)
    {
        RequestStop(reason);
        await operations.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopCurrentSessionAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            SetStatus(Status with { Enabled = false, Busy = false, RecoveryPending = files is not null || Status.RecoveryPending,
                Message = "恢复未确认；请先使用恢复按钮，不继续启用或换档。" });
            throw;
        }
        finally { operations.Release(); }
    }
    public async Task RecoverAsync(CancellationToken cancellationToken = default)
    {
        RequestStop("手动恢复请求。");
        await operations.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            files = findPendingRecovery() ?? files;
            if (files is null || !File.Exists(files.LeasePath))
            { SetStatus(new(Message: "没有需要恢复的实验会话；保持关闭。")); return; }
            var lease = files.ReadLease();
            SetStatus(new(Busy: true, RecoveryPending: true, LimitPercent: lease.LimitPercent, Message: "正在核对旧会话并恢复原值。", SessionId: files.SessionId));
            try { using var signal = EventWaitHandle.OpenExisting(files.StopName); signal.Set(); }
            catch (WaitHandleCannotBeOpenedException) { }
            var wait = Stopwatch.StartNew();
            while ((ChargeSessionFiles.IsProcess(lease.GuardianId, lease.GuardianStartTicks)
                || ChargeSessionFiles.IsProcess(lease.WorkerId, lease.WorkerStartTicks)) && wait.Elapsed < TimeSpan.FromSeconds(30))
                await Task.Delay(200, cancellationToken).ConfigureAwait(false);
            if (ChargeSessionFiles.IsProcess(lease.GuardianId, lease.GuardianStartTicks)
                || ChargeSessionFiles.IsProcess(lease.WorkerId, lease.WorkerStartTicks))
                throw new InvalidOperationException("原监护或工作进程仍在恢复，不并行写入。");
            using var self = Process.GetCurrentProcess();
            using var helper = ChargeSessionFiles.Launch("--experimental-charge-recover", files.SessionId,
                lease.LimitPercent, self.Id, self.StartTime.ToUniversalTime().Ticks);
            await helper.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(45), cancellationToken).ConfigureAwait(false);
            await WaitForRestorationAsync(files, cancellationToken).ConfigureAwait(false);
        }
        finally { operations.Release(); }
    }

    private async Task StopCurrentSessionAsync(CancellationToken token)
    {
        var current = files;
        if (current is not null)
            await WaitForRestorationAsync(current, token).ConfigureAwait(false);
        if (Status.RecoveryPending)
            throw new InvalidOperationException("原值恢复尚未确认；请先使用恢复按钮。");
        var limit = Status.LimitPercent;
        ClearCompletedSession();
        SetStatus(new(LimitPercent: limit, Message: "限充已关闭，已有实验限制已恢复；按原生策略充电。"));
    }

    private void ClearCompletedSession()
    {
        // Clear the identity first so an already queued old monitor callback
        // cannot resurrect Enabled/RecoveryPending after a confirmed restore.
        lock (stateGate) files = null;
        monitorCancellation?.Cancel(); monitorCancellation?.Dispose(); monitorCancellation = null;
        guardian?.Dispose(); guardian = null;
        stopEvent?.Dispose(); stopEvent = null;
    }
    private async Task WaitForRestorationAsync(ChargeSessionFiles current, CancellationToken token)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(45))
        {
            token.ThrowIfCancellationRequested();
            var wire = current.ReadStatus();
            if (wire is not null) ApplyWire(wire, current.SessionId);
            if (wire is { Phase: "Restored" }) return;
            if (wire is { Phase: "RecoveryPending" or "Error" }) throw new InvalidOperationException(wire.Message);
            if (!File.Exists(current.LeasePath) && guardian?.HasExited != false)
            { SetStatus(new(Message: "实验未启用，原值未改动。")); return; }
            await Task.Delay(200, token).ConfigureAwait(false);
        }
        SetStatus(Status with { Enabled = false, Busy = false, RecoveryPending = true, Message = "恢复尚未确认；独立监护可能仍在处理，请勿再次启用。" });
        throw new TimeoutException(Status.Message);
    }
    private async Task MonitorAsync(ChargeSessionFiles current, Process process, EventWaitHandle? ownedStopSignal, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                if (!ReferenceEquals(files, current) || disposed) return;
                var wire = current.ReadStatus();
                if (wire is not null) ApplyWire(wire, current.SessionId);
                if (wire is { Phase: "Restored" or "RecoveryPending" }) return;
                if (process.HasExited)
                {
                    if (File.Exists(current.LeasePath))
                        SetStatus(Status with { Enabled = false, Busy = false, RecoveryPending = true, Message = "独立监护已退出，但恢复尚未确认。请使用恢复按钮。" }, current.SessionId);
                    return;
                }
                await Task.Delay(1000, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            lock (stateGate)
            {
                if (!ReferenceEquals(files, current) || disposed || token.IsCancellationRequested) return;
                stopRequested = true;
                Interlocked.Increment(ref stopGeneration);
                Interlocked.Increment(ref enableGeneration);
            }
            // This callback may outlive its session. Signal only the event it
            // originally owned, never the newest session's global stopEvent.
            try { ownedStopSignal?.Set(); } catch (ObjectDisposedException) { }
            SetStatus(Status with { Enabled = false, Busy = false, RecoveryPending = true, Message = "状态监测异常，已请求停止；请核对恢复。" }, current.SessionId);
        }
    }
    private void ApplyWire(ChargeWireStatus wire, string session)
    {
        if (disposed || files?.SessionId != session) return;
        var suppressActive = stopRequested && wire.Enabled;
        SetStatus(new(wire.Enabled && !suppressActive,
            suppressActive || wire.Phase is "Starting" or "WorkerFinished", wire.RecoveryPending,
            wire.LimitPercent, suppressActive ? "正在停止限充并恢复原值。" : wire.Message, session), session);
    }
    private static ChargeSessionFiles? FindPending()
    {
        if (!Directory.Exists(ChargeSessionFiles.Root)) return null;
        foreach (var directory in new DirectoryInfo(ChargeSessionFiles.Root).EnumerateDirectories().OrderByDescending(d => d.LastWriteTimeUtc))
        {
            if (!Guid.TryParseExact(directory.Name, "N", out _)) continue;
            var candidate = new ChargeSessionFiles(directory.Name);
            if (File.Exists(candidate.LeasePath) && candidate.ReadStatus()?.Phase != "Restored") return candidate;
        }
        return null;
    }
    private void SetStatus(ExperimentalChargeStatus value, string? expectedSession = null)
    {
        lock (stateGate)
        {
            if (disposed || (expectedSession is not null && files?.SessionId != expectedSession)) return;
            // A stop may arrive between ApplyWire's initial check and this lock.
            // Late Active observations must not resurrect the switch afterward.
            if (expectedSession is not null && stopRequested && value.Enabled)
                value = value with { Enabled = false, Busy = true, Message = "正在停止限充并恢复原值。" };
            status = value;
        }
        try { StatusChanged?.Invoke(this, EventArgs.Empty); } catch (Exception) { /* UI observers must not stop monitoring. */ }
    }
    public void Dispose()
    {
        if (disposed) return;
        RequestStop("程序退出。"); disposed = true;
        monitorCancellation?.Cancel(); monitorCancellation?.Dispose(); guardian?.Dispose(); stopEvent?.Dispose();
    }
}
