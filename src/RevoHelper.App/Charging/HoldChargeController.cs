using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using RevoHelper.App.Lifecycle;
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
    private readonly TimeSpan heldResume;
    private readonly Func<bool> windowsShuttingDown;
    private readonly SemaphoreSlim operations = new(1, 1);
    private readonly object gate = new();
    private ExperimentalChargeStatus status = new();
    private IHoldWorker? worker;
    private CancellationTokenSource? watching;
    // The limit a worker left applied for a Windows shutdown that this process has outlived.
    private int? held;
    // Counts what is asked of the limit; a carry-on planned before the latest request is void.
    private long requests;
    private volatile bool stopRequested;
    private volatile bool disposed;

    /// <param name="heldResume">How long after a worker left the limit applied for a shutdown, with this process still running, a new worker carries it on.</param>
    /// <param name="windowsShuttingDown">True while Windows is shutting down; nothing is carried on then.</param>
    public HoldChargeController(IExperimentalChargeController legacy, Func<int, IHoldWorker>? startWorker = null,
        Func<HoldChargeStatus?>? readStatus = null, TimeSpan? poll = null, TimeSpan? heldResume = null,
        Func<bool>? windowsShuttingDown = null)
    {
        this.legacy = legacy ?? throw new ArgumentNullException(nameof(legacy));
        // The program file is found again for every start: it may have been moved since this
        // process started. If it cannot be found, nothing below starts and the switch can be retried.
        this.startWorker = startWorker ?? (limit => new HoldWorkerProcess(limit, RunningExecutable.Locate()));
        this.readStatus = readStatus ?? HoldChargeHost.ReadStatus;
        this.poll = poll ?? TimeSpan.FromMilliseconds(500);
        this.heldResume = heldResume ?? TimeSpan.FromSeconds(90);
        this.windowsShuttingDown = windowsShuttingDown ?? (() => GetSystemMetrics(SmShuttingDown) != 0);
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
            lock (gate)
            {
                if (current is { Enabled: true, Busy: false } && current.LimitPercent == limitPercent && held is null) return;
                requests++;
            }
            await StopWorkerAsync(cancellationToken).ConfigureAwait(false);
            stopRequested = false;
            if (limitPercent == 100)
            {
                // No limit: one left applied for a shutdown is put back first.
                if (!await ReleaseHeldAsync(cancellationToken).ConfigureAwait(false))
                    throw new InvalidOperationException(OffUnconfirmed);
                Set(new(Enabled: true, LimitPercent: 100, Message: "已开启：100% 不限制，按原生策略充电。"));
                return;
            }
            // The new worker takes over a countdown left for a shutdown, as it does after a restart.
            lock (gate) held = null;
            await StartWorkerAsync(limitPercent, carryingOn: false, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await StopWorkerAsync(CancellationToken.None).ConfigureAwait(false);
            Set(new(LimitPercent: limitPercent, Message: exception.Message));
            throw;
        }
        finally { operations.Release(); }
    }

    // Starts a worker and waits until it reports the limit applied. When it carries on a limit
    // that stayed on, the switch is not shown off meanwhile.
    private async Task StartWorkerAsync(int limit, bool carryingOn, CancellationToken cancellationToken)
    {
        Set(new(Enabled: carryingOn, Busy: true, LimitPercent: limit, Message: "正在启动限充并检查固件。"));
        var started = startWorker(limit);
        lock (gate) worker = started;
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(30))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (stopRequested) { await StopWorkerAsync(cancellationToken).ConfigureAwait(false); return; }
            var wire = readStatus();
            if (wire is { Phase: "Active" } && wire.LimitPercent == limit)
            {
                Set(new(Enabled: true, LimitPercent: limit, Message: wire.Message));
                watching = new();
                _ = WatchAsync(started, limit, watching.Token);
                return;
            }
            if (started.HasExited)
                throw new InvalidOperationException(wire is { Phase: "Error" or "Stopped" or "Unconfirmed" } ? wire.Message : "限充进程未能启动。");
            await Task.Delay(poll, cancellationToken).ConfigureAwait(false);
        }
        throw new TimeoutException("限充启动超时。");
    }

    public void RequestStop(string reason)
    {
        stopRequested = true;
        IHoldWorker? current;
        lock (gate) { current = worker; requests++; }
        try { current?.SignalStop(); } catch (ObjectDisposedException) { }
        legacy.RequestStop(reason);
        var now = Status;
        if (now.Enabled && now.LimitPercent == 100)
            Set(new(LimitPercent: 100, Message: "限充已关闭；按原生策略充电。"));
        else if (now.Enabled)
            Set(now with { Enabled = false, Busy = true, Message = "正在停止限充并恢复原值。" });
    }

    /// <summary>Switched off, but the worker did not confirm the restore. See <see cref="HoldChargeHost.StoppedUnconfirmed"/>.</summary>
    public const string OffUnconfirmed = "限充已关闭；恢复未确认，固件计时到期后会自行恢复正常充电（通常约 30 分钟，最长 3 天）。";

    /// <summary>
    /// Windows is shutting down. A limit that is holding is left applied (for at most three days)
    /// instead of running out half an hour later. Windows may end the worker soon after this
    /// process answers, so this waits, at most <paramref name="wait"/>, for the worker's answer to
    /// this signal: the long countdown written, or the charge below the limit. A status from
    /// before the signal is no answer: the worker may have just started holding and not said so
    /// yet. Null when no limit is running, or when a stop was asked for: that goes ahead.
    /// </summary>
    public ShutdownHoldResult? HoldForShutdown(TimeSpan wait)
    {
        IHoldWorker? current;
        bool left;
        lock (gate) { current = worker; left = held is not null; }
        if (disposed || stopRequested) return null;
        // Windows asked the worker first, and it has already left the limit applied.
        if (current is null) return left ? ShutdownHoldResult.HeldEarlier : null;
        if (current.HasExited) return readStatus() is { Phase: "Held" } ? ShutdownHoldResult.HeldEarlier : null;
        try { current.SignalShutdownHold(); }
        catch (ObjectDisposedException) { return null; }
        var clock = Stopwatch.StartNew();
        while (true)
        {
            // Checked before the status: a worker that has ended has already written its last one.
            var exited = current.HasExited;
            var wire = readStatus();
            // The worker writes either of these only after it has seen the signal.
            if (wire is { Phase: "Held" }) return ShutdownHoldResult.Held;
            if (wire is { Phase: HoldChargeHost.ReleasingPhase }) return ShutdownHoldResult.BelowLimit;
            if (exited) return ShutdownHoldResult.Ended;
            if (clock.Elapsed >= wait) return ShutdownHoldResult.TimedOut;
            Thread.Sleep(50);
        }
    }

    public async Task StopAsync(string reason, CancellationToken cancellationToken = default)
    {
        RequestStop(reason);
        await operations.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var limit = Status.LimitPercent;
            var confirmed = await StopWorkerAsync(cancellationToken).ConfigureAwait(false);
            // A limit left applied for a shutdown that did not happen is put back as well.
            confirmed = await ReleaseHeldAsync(cancellationToken).ConfigureAwait(false) && confirmed;
            Set(new(LimitPercent: limit, Message: confirmed
                ? "限充已关闭；按原生策略充电。"
                : OffUnconfirmed));
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

    /// <summary>
    /// Puts back a limit that a worker left applied for a shutdown this process outlived. A worker
    /// that is told to stop before it starts takes the countdown over and puts the original values back.
    /// </summary>
    /// <returns>True when nothing was left applied, or the worker confirmed the restore.</returns>
    private async Task<bool> ReleaseHeldAsync(CancellationToken token)
    {
        int limit;
        lock (gate)
        {
            if (held is not { } left) return true;
            limit = left;
            held = null;
        }
        var restored = false;
        try
        {
            using var releasing = startWorker(limit);
            releasing.SignalStop();
            restored = await releasing.WaitForExitAsync(TimeSpan.FromSeconds(30), token).ConfigureAwait(false)
                && readStatus()?.Phase == "Stopped";
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { }
        finally
        {
            // On battery, for one, the worker does not start. The next stop tries again, and the
            // next worker that starts takes the countdown over.
            if (!restored) lock (gate) held ??= limit;
        }
        return restored;
    }

    private async Task WatchAsync(IHoldWorker started, int limit, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                // Twice the start-up poll: once a second.
                await Task.Delay(poll * 2, token).ConfigureAwait(false);
                var wire = readStatus();
                lock (gate) if (!ReferenceEquals(worker, started)) return;
                if (started.HasExited)
                {
                    lock (gate) { if (ReferenceEquals(worker, started)) worker = null; }
                    started.Dispose();
                    if (wire is { Phase: "Held" } && !stopRequested)
                    {
                        // Windows asked the worker about a shutdown on its own, and it left the limit
                        // applied for it. Windows asks every program in turn; another one may have
                        // called the shutdown off before this one was asked. Nothing was stopped:
                        // the limit stays on, and carries on if this process is still running later.
                        long request;
                        lock (gate) { held = limit; request = requests; }
                        Set(new(Enabled: true, LimitPercent: limit, Message: wire.Message));
                        _ = ResumeHeldAsync(limit, request);
                        return;
                    }
                    // It stopped on its own: power lost, battery state invalid, or a firmware surprise.
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

    // Windows ends this process soon after a shutdown that goes ahead. Still running a while
    // later, with Windows no longer shutting down, the shutdown was called off: a new worker
    // carries the limit on and takes the long countdown back. Anything asked of the limit in the
    // meantime (a stop, another limit) replaces this. An exit leaves the countdown to run out,
    // or to the next start; an error exit of a worker is never carried on.
    private async Task ResumeHeldAsync(int limit, long request)
    {
        do await Task.Delay(heldResume).ConfigureAwait(false);
        while (IsHeld(limit, request) && windowsShuttingDown());
        await operations.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!IsHeld(limit, request)) return;
            lock (gate) held = null;
            await StartWorkerAsync(limit, carryingOn: true, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await StopWorkerAsync(CancellationToken.None).ConfigureAwait(false);
            // Whether the new worker took the countdown over before it failed is not known.
            Set(new(LimitPercent: limit, Message: OffUnconfirmed + exception.Message));
        }
        finally { operations.Release(); }
    }

    private bool IsHeld(int limit, long request)
    {
        lock (gate) return !disposed && requests == request && held == limit && worker is null;
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

    private const int SmShuttingDown = 0x2000;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}

/// <summary>How a running charge limit answered a Windows shutdown.</summary>
public enum ShutdownHoldResult
{
    /// <summary>The long countdown is written: the limit stays applied while the laptop is off.</summary>
    Held,
    /// <summary>Windows asked the worker first, and it had already left the limit applied.</summary>
    HeldEarlier,
    /// <summary>The worker answered that the charge is below the limit: nothing stays applied, and it is putting the original values back.</summary>
    BelowLimit,
    /// <summary>The worker ended without either answer; its log says why.</summary>
    Ended,
    /// <summary>No answer in time. Windows may end the worker before it writes.</summary>
    TimedOut,
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

    /// <param name="executable">This program's file as it is now (<see cref="RunningExecutable.Locate()"/>).</param>
    public HoldWorkerProcess(int limit, string executable)
    {
        // Everything about the start is settled before any event or file is touched.
        ProcessStartInfo info;
        using (var self = Process.GetCurrentProcess())
        {
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            info = RunningExecutable.StartInfo(executable, [HoldChargeHost.Mode, limit.ToString(culture),
                self.Id.ToString(culture), self.StartTime.ToUniversalTime().Ticks.ToString(culture)]);
        }
        stop = new(false, EventResetMode.ManualReset, HoldChargeHost.StopName);
        stop.Reset();
        shutdown = new(false, EventResetMode.ManualReset, HoldChargeHost.ShutdownName);
        shutdown.Reset();
        try { File.Delete(HoldChargeHost.StatusPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
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
