namespace RevoHelper.App.Lifecycle;

/// <summary>A running process of the official control center's own window or tray.</summary>
public sealed record OfficialFrontendProcess(int Id, string Name);

public sealed record OfficialFrontendSnapshot(IReadOnlyList<OfficialFrontendProcess> Targets,
    bool HasUnknown = false, bool LegacyRunning = false)
{
    public static OfficialFrontendSnapshot Unknown { get; } = new([], HasUnknown: true);
}

public enum OfficialFrontendCloseOutcome { Closed, Gone, Skipped, Failed }

/// <summary>
/// The official control center's window (CCUWinUI) and tray (SystrayComponent): the only processes
/// RevoHelper closes for the user. Both are images inside the CCU.WinUI package. The background
/// service RevoHelper works through (GCUBridge, GCUService), the OSD hotkey helper and the driver
/// are installed elsewhere and never match; no service setting or file is changed.
/// </summary>
public interface IOfficialFrontendProcesses
{
    OfficialFrontendSnapshot Find();

    OfficialFrontendCloseOutcome Close(OfficialFrontendProcess process, Func<bool> mayClose);
}

/// <summary>What one exit run did so far.</summary>
public sealed record OfficialFrontendExitResult(int Closed, int Failed, bool GaveUp, bool HasUnknown = false)
{
    public static OfficialFrontendExitResult None { get; } = new(0, 0, false);
}

/// <summary>
/// One run of checks. The pre-conflict check and the later sign-in checks share its time and
/// attempt budget. A wake, or a new process of the official service, gives the tray three more
/// minutes and a few closes of its own (<see cref="OfficialFrontendExit.ExtendTray"/>); the
/// window's time is never moved, since after sign-in a window is open because someone opened it.
/// </summary>
public sealed class OfficialFrontendStartupWatch
{
    private readonly object sync = new();
    private readonly Dictionary<string, int> attempts = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset trayDeadline;
    private int roundsLeft = Rounds;
    private bool ended;

    internal OfficialFrontendStartupWatch(DateTimeOffset deadline, bool trayOnly = false)
    {
        // A watch for a wake or a new service process closes only the tray: the service opens that
        // by itself, while a window is open because someone opened it.
        WindowDeadline = trayOnly ? DateTimeOffset.MinValue : deadline;
        trayDeadline = deadline;
    }

    private static int Rounds => (int)(OfficialFrontendExit.WatchDuration / OfficialFrontendExit.PollInterval);

    internal DateTimeOffset WindowDeadline { get; }

    /// <summary>Null until the first check has run.</summary>
    internal OfficialFrontendExitResult? FirstRound { get; set; }

    private DateTimeOffset Deadline => trayDeadline > WindowDeadline ? trayDeadline : WindowDeadline;

    internal bool InTime(DateTimeOffset now) { lock (sync) return now <= Deadline; }

    internal bool MayClose(string name, DateTimeOffset now)
    {
        lock (sync)
            return now <= (string.Equals(name, OfficialFrontendExit.TrayName, StringComparison.OrdinalIgnoreCase)
                ? trayDeadline : WindowDeadline);
    }

    internal int AttemptsOf(string name) { lock (sync) return attempts.GetValueOrDefault(name); }

    internal int CountAttempt(string name) { lock (sync) return attempts[name] = attempts.GetValueOrDefault(name) + 1; }

    /// <summary>
    /// The wait before the next check, or null when the watch is over. Over is final: a later wake
    /// or new service process starts a new watch instead.
    /// </summary>
    internal TimeSpan? NextRound(DateTimeOffset now)
    {
        lock (sync)
        {
            var remaining = Deadline - now;
            if (remaining <= TimeSpan.Zero || roundsLeft <= 0)
            {
                ended = true;
                return null;
            }
            roundsLeft--;
            return remaining < OfficialFrontendExit.PollInterval ? remaining : OfficialFrontendExit.PollInterval;
        }
    }

    /// <summary>
    /// Three more minutes for the tray from <paramref name="now"/>, with a fresh close budget. Its
    /// time may have run out already (the computer slept through it) as long as the watch has not
    /// noticed. False when the watch is over.
    /// </summary>
    internal bool ExtendTray(DateTimeOffset now)
    {
        lock (sync)
        {
            if (ended) return false;
            var until = now + OfficialFrontendExit.WatchDuration;
            if (until > trayDeadline) trayDeadline = until;
            roundsLeft = Math.Max(roundsLeft, Rounds);
            attempts.Remove(OfficialFrontendExit.TrayName);
            return true;
        }
    }
}

/// <summary>
/// Closes the official window and tray once: for the exit button, and at every start while long-term
/// exit is on. When RevoHelper starts with Windows it also keeps checking for a few minutes, since the
/// official service starts its tray after sign-in, later than RevoHelper. The service opens its tray
/// again after the computer wakes or the service starts a new process; then only the tray is looked
/// for, the same way. Opening the official control center is left alone. A process that keeps coming
/// back is given up on after a few closes instead of being fought.
/// </summary>
public sealed class OfficialFrontendExit(
    IOfficialFrontendProcesses processes,
    Func<TimeSpan, CancellationToken, Task>? delay = null,
    Action<string>? log = null,
    TimeProvider? timeProvider = null)
{
    public static readonly TimeSpan WatchDuration = TimeSpan.FromMinutes(3);
    // At most 19 checks including the one before startup conflict detection, unless extended.
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);
    public const int MaximumClosesPerName = 3;
    public const string TrayName = "SystrayComponent";

    private readonly Func<TimeSpan, CancellationToken, Task> delay = delay ?? Task.Delay;
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim closeGate = new(1, 1);

    public OfficialFrontendSnapshot Inspect()
    {
        try { return processes.Find(); }
        catch (Exception exception)
        {
            log?.Invoke("listing failed: " + exception.Message);
            return OfficialFrontendSnapshot.Unknown;
        }
    }

    public OfficialFrontendExitResult CloseNow(CancellationToken cancellationToken = default) =>
        CloseRound(null, cancellationToken);

    /// <summary>A watch with its first check done here, before startup conflict detection.</summary>
    public OfficialFrontendStartupWatch StartStartupWatch(CancellationToken cancellationToken = default)
    {
        var startup = CreateWatch(trayOnly: false);
        startup.FirstRound = CloseRound(startup, cancellationToken);
        return startup;
    }

    /// <summary>A watch whose first check runs in <see cref="WatchAsync"/>.</summary>
    public OfficialFrontendStartupWatch CreateWatch(bool trayOnly) => new(clock.GetUtcNow() + WatchDuration, trayOnly);

    /// <summary>
    /// For a wake or a new process of the official service: the tray gets three more minutes and a
    /// few closes of its own. False when <paramref name="watch"/> is already over; then start another.
    /// </summary>
    public bool ExtendTray(OfficialFrontendStartupWatch watch) => watch.ExtendTray(clock.GetUtcNow());

    /// <summary>
    /// Reports the first check and then every check that closed, or failed to close, something.
    /// The checks run on the thread pool; <paramref name="changed"/> runs on the caller's context.
    /// </summary>
    public async Task<OfficialFrontendExitResult> WatchAsync(
        Action<OfficialFrontendExitResult> changed, CancellationToken cancellationToken,
        OfficialFrontendStartupWatch? startup = null)
    {
        startup ??= CreateWatch(trayOnly: false);
        startup.FirstRound ??= await Task.Run(() => CloseRound(startup, cancellationToken), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var total = startup.FirstRound;
        changed(total);
        while (startup.NextRound(clock.GetUtcNow()) is { } wait)
        {
            await delay(wait, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            // Woken beyond its time: no check, unless a wake gave the tray more time meanwhile.
            if (!startup.InTime(clock.GetUtcNow())) continue;

            var result = await Task.Run(() => CloseRound(startup, cancellationToken), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var next = new OfficialFrontendExitResult(total.Closed + result.Closed, total.Failed + result.Failed,
                total.GaveUp || result.GaveUp, result.HasUnknown);
            if (next != total) changed(next);
            total = next;
        }
        return total;
    }

    private OfficialFrontendExitResult CloseRound(OfficialFrontendStartupWatch? startup, CancellationToken token)
    {
        // A manual click can overlap a startup round. Only closing takes this gate; UI status reads
        // can still run while Windows is waiting for a process to exit.
        closeGate.Wait(token);
        try
        {
            bool MayClose(string name) => !token.IsCancellationRequested &&
                (startup is null || startup.MayClose(name, clock.GetUtcNow()));
            token.ThrowIfCancellationRequested();
            if (startup is not null && !startup.InTime(clock.GetUtcNow())) return OfficialFrontendExitResult.None;
            var snapshot = Inspect();
            int closed = 0, failed = 0;
            bool gaveUp = false;
            foreach (var process in snapshot.Targets)
            {
                token.ThrowIfCancellationRequested();
                // Past its time, or a window in a watch for the tray alone.
                if (!MayClose(process.Name)) continue;
                var attempts = startup?.AttemptsOf(process.Name) ?? 0;
                if (attempts >= MaximumClosesPerName)
                {
                    gaveUp = true;
                    continue;
                }

                OfficialFrontendCloseOutcome outcome;
                try { outcome = processes.Close(process, () => MayClose(process.Name)); }
                catch (Exception) { outcome = OfficialFrontendCloseOutcome.Failed; }
                if (outcome is OfficialFrontendCloseOutcome.Skipped or OfficialFrontendCloseOutcome.Gone) continue;
                if (startup is not null && startup.CountAttempt(process.Name) == MaximumClosesPerName)
                    log?.Invoke($"startup limit reached for {process.Name} after {MaximumClosesPerName} attempts");
                if (outcome == OfficialFrontendCloseOutcome.Closed) closed++;
                else failed++;
                log?.Invoke($"{(outcome == OfficialFrontendCloseOutcome.Closed ? "closed" : "could not close")} {process.Name}#{process.Id}");
            }
            token.ThrowIfCancellationRequested();
            return new(closed, failed, gaveUp, snapshot.HasUnknown);
        }
        finally { closeGate.Release(); }
    }
}
