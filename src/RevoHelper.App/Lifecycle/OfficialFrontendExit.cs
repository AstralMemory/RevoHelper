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

/// <summary>The pre-conflict check and the later sign-in checks share one time and attempt budget.</summary>
public sealed class OfficialFrontendStartupWatch
{
    internal OfficialFrontendStartupWatch(DateTimeOffset deadline) => Deadline = deadline;
    internal DateTimeOffset Deadline { get; }
    internal Dictionary<string, int> Attempts { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal OfficialFrontendExitResult FirstRound { get; set; } = OfficialFrontendExitResult.None;
}

/// <summary>
/// Closes the official window and tray once: for the exit button, and at every start while long-term
/// exit is on. When RevoHelper starts with Windows it also keeps checking for a few minutes, since the
/// official service starts its tray after sign-in, later than RevoHelper. Opening the official control
/// center after that is left alone. A process that keeps coming back is given up on after a few closes
/// instead of being fought.
/// </summary>
public sealed class OfficialFrontendExit(
    IOfficialFrontendProcesses processes,
    Func<TimeSpan, CancellationToken, Task>? delay = null,
    Action<string>? log = null,
    TimeProvider? timeProvider = null)
{
    public static readonly TimeSpan WatchDuration = TimeSpan.FromMinutes(3);
    // At most 19 checks including the one before startup conflict detection.
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);
    public const int MaximumClosesPerName = 3;

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

    public OfficialFrontendStartupWatch StartStartupWatch(CancellationToken cancellationToken = default)
    {
        var startup = new OfficialFrontendStartupWatch(clock.GetUtcNow() + WatchDuration);
        startup.FirstRound = CloseRound(startup, cancellationToken);
        return startup;
    }

    /// <summary>
    /// Reports the first check and then every check that closed, or failed to close, something.
    /// The checks run on the thread pool; <paramref name="changed"/> runs on the caller's context.
    /// </summary>
    public async Task<OfficialFrontendExitResult> WatchAsync(
        Action<OfficialFrontendExitResult> changed, CancellationToken cancellationToken,
        OfficialFrontendStartupWatch? startup = null)
    {
        startup ??= await Task.Run(() => StartStartupWatch(cancellationToken), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var total = startup.FirstRound;
        changed(total);
        var lastRound = (int)(WatchDuration / PollInterval);
        for (var round = 1; round <= lastRound; round++)
        {
            var remaining = startup.Deadline - clock.GetUtcNow();
            if (remaining <= TimeSpan.Zero) break;
            await delay(remaining < PollInterval ? remaining : PollInterval, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (clock.GetUtcNow() > startup.Deadline) break;

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
            bool MayClose() => !token.IsCancellationRequested && (startup is null || clock.GetUtcNow() <= startup.Deadline);
            token.ThrowIfCancellationRequested();
            if (!MayClose()) return OfficialFrontendExitResult.None;
            var snapshot = Inspect();
            int closed = 0, failed = 0;
            bool gaveUp = false;
            foreach (var process in snapshot.Targets)
            {
                token.ThrowIfCancellationRequested();
                if (!MayClose()) break;
                var attempts = startup?.Attempts.GetValueOrDefault(process.Name) ?? 0;
                if (attempts >= MaximumClosesPerName)
                {
                    gaveUp = true;
                    continue;
                }

                OfficialFrontendCloseOutcome outcome;
                try { outcome = processes.Close(process, MayClose); }
                catch (Exception) { outcome = OfficialFrontendCloseOutcome.Failed; }
                if (outcome == OfficialFrontendCloseOutcome.Skipped) break;
                if (outcome == OfficialFrontendCloseOutcome.Gone) continue;
                if (startup is not null)
                {
                    startup.Attempts[process.Name] = attempts + 1;
                    if (attempts + 1 == MaximumClosesPerName)
                        log?.Invoke($"startup limit reached for {process.Name} after {MaximumClosesPerName} attempts");
                }
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
