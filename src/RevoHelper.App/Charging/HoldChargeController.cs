using System.Diagnostics;
using System.IO;
using RevoHelper.Core.Control.ExperimentalCharge;

namespace RevoHelper.App.Charging;

/// <summary>
/// The charge limit as the rest of the app sees it, implemented with the firmware hold feature
/// (see <see cref="HoldChargeEngine"/>) instead of the platform-state override. It owns only the
/// worker process and the displayed status. The old controller is kept for one job: restoring a
/// session that an earlier version left behind.
/// </summary>
public sealed class HoldChargeController : IExperimentalChargeController, IDisposable
{
    private readonly IExperimentalChargeController legacy;
    private readonly Func<int, IHoldWorker> startWorker;
    private readonly Func<HoldChargeStatus?> readStatus;
    private readonly TimeSpan poll;
    private readonly SemaphoreSlim operations = new(1, 1);
    private readonly object gate = new();
    private ExperimentalChargeStatus status = new();
    private IHoldWorker? worker;
    private CancellationTokenSource? watching;
    private volatile bool stopRequested;
    private volatile bool disposed;

    public HoldChargeController(IExperimentalChargeController legacy, Func<int, IHoldWorker>? startWorker = null,
        Func<HoldChargeStatus?>? readStatus = null, TimeSpan? poll = null)
    {
        this.legacy = legacy ?? throw new ArgumentNullException(nameof(legacy));
        this.startWorker = startWorker ?? (limit => new HoldWorkerProcess(limit));
        this.readStatus = readStatus ?? HoldChargeHost.ReadStatus;
        this.poll = poll ?? TimeSpan.FromMilliseconds(500);
        legacy.StatusChanged += (_, _) => Publish();
    }

    public event EventHandler? StatusChanged;

    public ExperimentalChargeStatus Status
    {
        get
        {
            // An unrestored session from the old method blocks everything until it is restored.
            var old = legacy.Status;
            if (old.RecoveryPending || old.Busy || old.Enabled)
                return old with { Enabled = false, Busy = old.Busy || old.Enabled };
            lock (gate) return status;
        }
    }

    public async Task EnableAsync(int limitPercent, CancellationToken cancellationToken = default)
    {
        if (limitPercent is not (60 or 80 or 100)) throw new ArgumentOutOfRangeException(nameof(limitPercent));
        await operations.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (legacy.Status.RecoveryPending) throw new InvalidOperationException("先恢复上一会话；不会接管未知原值。");
            var current = Status;
            if (current is { Enabled: true, Busy: false } && current.LimitPercent == limitPercent) return;
            await StopWorkerAsync(cancellationToken).ConfigureAwait(false);
            stopRequested = false;
            if (limitPercent == 100)
            {
                Set(new(Enabled: true, LimitPercent: 100, Message: "已开启：100% 不限制，按原生策略充电。"));
                return;
            }
            Set(new(Busy: true, LimitPercent: limitPercent, Message: "正在启动限充并检查固件。"));
            var started = startWorker(limitPercent);
            lock (gate) worker = started;
            var deadline = Stopwatch.StartNew();
            while (deadline.Elapsed < TimeSpan.FromSeconds(30))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (stopRequested) { await StopWorkerAsync(cancellationToken).ConfigureAwait(false); return; }
                var wire = readStatus();
                if (wire is { Phase: "Active" } && wire.LimitPercent == limitPercent)
                {
                    Set(new(Enabled: true, LimitPercent: limitPercent, Message: wire.Message));
                    watching = new();
                    _ = WatchAsync(started, limitPercent, watching.Token);
                    return;
                }
                if (started.HasExited)
                    throw new InvalidOperationException(wire is { Phase: "Error" or "Stopped" or "Unconfirmed" } ? wire.Message : "限充进程未能启动。");
                await Task.Delay(poll, cancellationToken).ConfigureAwait(false);
            }
            throw new TimeoutException("限充启动超时。");
        }
        catch (Exception exception)
        {
            await StopWorkerAsync(CancellationToken.None).ConfigureAwait(false);
            Set(new(LimitPercent: limitPercent, Message: exception.Message));
            throw;
        }
        finally { operations.Release(); }
    }

    public void RequestStop(string reason)
    {
        stopRequested = true;
        IHoldWorker? current;
        lock (gate) current = worker;
        try { current?.SignalStop(); } catch (ObjectDisposedException) { }
        legacy.RequestStop(reason);
        var now = Status;
        if (now.Enabled && now.LimitPercent == 100)
            Set(new(LimitPercent: 100, Message: "限充已关闭；按原生策略充电。"));
        else if (now.Enabled)
            Set(now with { Enabled = false, Busy = true, Message = "正在停止限充并恢复原值。" });
    }

    /// <summary>
    /// Windows is shutting down. A limit that is holding is left applied (for at most three days)
    /// instead of running out half an hour later. Returns false when no limit is running.
    /// </summary>
    public bool HoldForShutdown()
    {
        IHoldWorker? current;
        lock (gate) current = worker;
        if (disposed || current is null || current.HasExited) return false;
        try { current.SignalShutdownHold(); return true; }
        catch (ObjectDisposedException) { return false; }
    }

    public async Task StopAsync(string reason, CancellationToken cancellationToken = default)
    {
        RequestStop(reason);
        await operations.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var limit = Status.LimitPercent;
            var confirmed = await StopWorkerAsync(cancellationToken).ConfigureAwait(false);
            Set(new(LimitPercent: limit, Message: confirmed
                ? "限充已关闭；按原生策略充电。"
                : "限充已关闭；恢复未确认，固件会在约 30 分钟内自行恢复正常充电。"));
        }
        finally { operations.Release(); }
    }

    public async Task RecoverAsync(CancellationToken cancellationToken = default)
    {
        await legacy.RecoverAsync(cancellationToken).ConfigureAwait(false);
        Publish();
    }

    /// <returns>True when the worker reported that it put everything back (or nothing was running).</returns>
    private async Task<bool> StopWorkerAsync(CancellationToken token)
    {
        IHoldWorker? current;
        lock (gate) { current = worker; worker = null; }
        watching?.Cancel(); watching?.Dispose(); watching = null;
        if (current is null) return true;
        try
        {
            current.SignalStop();
            var exited = await current.WaitForExitAsync(TimeSpan.FromSeconds(30), token).ConfigureAwait(false);
            return exited && readStatus()?.Phase is "Stopped" or "Error";
        }
        finally { current.Dispose(); }
    }

    private async Task WatchAsync(IHoldWorker started, int limit, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
                var wire = readStatus();
                lock (gate) if (!ReferenceEquals(worker, started)) return;
                if (started.HasExited)
                {
                    // It stopped on its own: power lost, battery state invalid, or a firmware surprise.
                    lock (gate) { if (ReferenceEquals(worker, started)) worker = null; }
                    started.Dispose();
                    Set(new(LimitPercent: limit, Message: wire?.Message ?? "限充进程已退出。"));
                    return;
                }
                if (wire is { Phase: "Active" } && !stopRequested && wire.Message != Status.Message)
                    Set(new(Enabled: true, LimitPercent: limit, Message: wire.Message));
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    private void Set(ExperimentalChargeStatus value)
    {
        lock (gate) { if (disposed) return; status = value; }
        Publish();
    }

    private void Publish()
    {
        try { StatusChanged?.Invoke(this, EventArgs.Empty); } catch (Exception) { /* Observers must not stop the limiter. */ }
    }

    public void Dispose()
    {
        if (disposed) return;
        // The worker also stops when this process is gone; either way the countdown is cleared or runs out.
        RequestStop("程序退出。");
        disposed = true;
        watching?.Cancel();
        IHoldWorker? current;
        lock (gate) { current = worker; worker = null; }
        current?.Dispose();
    }
}

public interface IHoldWorker : IDisposable
{
    bool HasExited { get; }
    void SignalStop();
    void SignalShutdownHold();
    Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken token);
}

internal sealed class HoldWorkerProcess : IHoldWorker
{
    private readonly Process process;
    private readonly EventWaitHandle stop;
    private readonly EventWaitHandle shutdown;

    public HoldWorkerProcess(int limit)
    {
        stop = new(false, EventResetMode.ManualReset, HoldChargeHost.StopName);
        stop.Reset();
        shutdown = new(false, EventResetMode.ManualReset, HoldChargeHost.ShutdownName);
        shutdown.Reset();
        try { File.Delete(HoldChargeHost.StatusPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        using var self = Process.GetCurrentProcess();
        var info = new ProcessStartInfo(Environment.ProcessPath ?? throw new InvalidOperationException("Executable path unavailable."))
        { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        foreach (var argument in new[] { HoldChargeHost.Mode, limit.ToString(culture), self.Id.ToString(culture),
                     self.StartTime.ToUniversalTime().Ticks.ToString(culture) })
            info.ArgumentList.Add(argument);
        try { process = Process.Start(info) ?? throw new InvalidOperationException("无法启动限充进程。"); }
        catch { stop.Dispose(); shutdown.Dispose(); throw; }
    }

    public bool HasExited { get { try { return process.HasExited; } catch (InvalidOperationException) { return true; } } }
    public void SignalStop() { try { stop.Set(); } catch (ObjectDisposedException) { } }
    public void SignalShutdownHold() => shutdown.Set();
    public async Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken token)
    {
        try { await process.WaitForExitAsync(token).WaitAsync(timeout, token).ConfigureAwait(false); return true; }
        catch (TimeoutException) { return false; }
    }
    public void Dispose() { process.Dispose(); stop.Dispose(); shutdown.Dispose(); }
}
