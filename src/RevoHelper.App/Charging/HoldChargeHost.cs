using System.Diagnostics;
using System.IO;
using System.Text.Json;
using RevoHelper.Hardware.Windows.BatteryCharge;

namespace RevoHelper.App.Charging;

public sealed record HoldChargeStatus(string Phase, string Message, int LimitPercent, DateTimeOffset At,
    HoldChargeObservation? Last = null);

/// <summary>
/// Worker process for the charge limit. It runs apart from the UI so a crash of the window cannot
/// leave a half-written state, and it shares the old limiter's mutexes so a leftover session from
/// an earlier version and this one can never touch the battery together.
/// </summary>
public static class HoldChargeHost
{
    public const string Mode = "--hold-charge-worker";
    public const string StopName = @"Local\RevoHelper.HoldCharge.Stop";
    // Set when Windows is shutting down: a limit that is holding is left applied for three days.
    public const string ShutdownName = @"Local\RevoHelper.HoldCharge.Shutdown";
    // The answer to the shutdown signal when the charge is below the limit, written before the
    // restore that follows: nothing stays applied, so Windows need not wait for it.
    public const string ReleasingPhase = "Releasing";
    // No time is promised when the restore is not confirmed: a hold left for a shutdown, or one
    // partly written then, keeps the battery from charging for up to three days.
    public const string StoppedUnconfirmed = "限充已停止，恢复未确认；固件计时到期后会自行恢复正常充电（通常约 30 分钟，最长 3 天）。";
    public static string StatusPath => Path.Combine(ChargeSessionFiles.Root, "hold-status.json");
    public static bool IsHelper(string[] args) => args.Length > 0 && args[0] == Mode;

    /// <summary>The worker process's charge limit, on a thread of its own (see <see cref="HoldChargeWorkerThread"/>).</summary>
    public static HoldChargeWorkerThread StartThread(string[] args, Action<int> finished)
    {
        var worker = new HoldChargeWorkerThread(() => Run(args), finished, () =>
        {
            using var stop = EventWaitHandle.OpenExisting(StopName);
            using var shutdown = new EventWaitHandle(false, EventResetMode.ManualReset, ShutdownName);
            return SignalShutdownUnlessStopped(stop, shutdown);
        });
        worker.Start();
        return worker;
    }

    /// <summary>
    /// The worker's own answer to Windows: the limit is told to hold for the shutdown, unless a
    /// stop came first. A stop that was asked for goes ahead and puts the original values back.
    /// The other order still holds: a stop after this signal (the program exiting for the
    /// shutdown) does not undo it, since the limit keeps the hold whenever this signal is set.
    /// </summary>
    /// <returns>False when a stop came first and nothing was set.</returns>
    public static bool SignalShutdownUnlessStopped(WaitHandle stop, EventWaitHandle shutdown)
    {
        if (stop.WaitOne(0)) return false;
        shutdown.Set();
        return true;
    }

    public static int Run(string[] args)
    {
        var limit = 80;
        try
        {
            if (args.Length != 4 || !int.TryParse(args[1], out limit) || limit is not (60 or 80)
                || !int.TryParse(args[2], out var owner) || owner <= 0 || !long.TryParse(args[3], out var ticks) || ticks <= 0)
                return 2;
            Directory.CreateDirectory(ChargeSessionFiles.Root);
            using var a = Acquire(@"Global\RevoHelperTemporaryBatteryPlatformTrial");
            using var b = Acquire(@"Global\RevoHelperNativeBatteryThresholdTrial");
            using var c = Acquire(@"Global\RevoHelperTemporaryBatteryPlatformWriter");
            using var stop = EventWaitHandle.OpenExisting(StopName);
            using var shutdown = new EventWaitHandle(false, EventResetMode.ManualReset, ShutdownName);
            Save(new("Starting", "正在启动限充并检查固件。", limit, DateTimeOffset.Now));
            var log = Path.Combine(ChargeSessionFiles.Root, $"hold-log-{DateTime.Now:yyyyMMdd-HHmmss}.jsonl");
            // One small file per run; keep the newest few so the folder does not grow for ever.
            try
            {
                foreach (var old in new DirectoryInfo(ChargeSessionFiles.Root).GetFiles("hold-log-*.jsonl")
                             .OrderByDescending(file => file.LastWriteTimeUtc).Skip(20))
                    old.Delete();
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
            RemoveStaleTemporaryFiles(ChargeSessionFiles.Root);
            HoldChargeObservation? previous = null;
            var sinceLogged = 0;
            var engine = new HoldChargeEngine(() => new ReviewedChargeTransport());
            var selectedLimit = limit;
            var restored = engine.Run(limit,
                () => stop.WaitOne(0) || shutdown.WaitOne(0) || !ChargeSessionFiles.IsProcess(owner, ticks),
                () => WaitHandle.WaitAny([stop, shutdown], 2000),
                observation =>
                {
                    // Keep the log small: write changes, and a heartbeat every half minute.
                    var changed = previous is null || observation with { At = previous.At } != previous;
                    // Writing the log and the status is best effort: an exception thrown here would be
                    // taken for an EC failure, and five in a row would stop the charge limit.
                    if (changed || ++sinceLogged >= 15)
                    {
                        TryAppend(log, JsonSerializer.Serialize(observation));
                        sinceLogged = 0;
                    }
                    previous = observation;
                    if (observation.Note == HoldChargeEngine.ShutdownBelowLimitNote)
                    {
                        // If this cannot be written, the window waits until this process ends.
                        try { Save(new(ReleasingPhase, "关机：低于上限，正在恢复原值。", selectedLimit, DateTimeOffset.Now, observation)); }
                        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
                        return;
                    }
                    if (observation.Soc < 0) return;
                    var message = !observation.Holding ? "限充运行中：低于上限，正在充电。"
                        : !observation.Charging && observation.ChargeRateMw == 0 ? "限充运行中：已达上限，当前未充电。"
                        : "限充运行中：已请求停充，等待固件响应。";
                    try { Save(new("Active", message, selectedLimit, DateTimeOffset.Now, observation)); }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        // The next round, a few seconds later, writes it again.
                        TryAppend(log, JsonSerializer.Serialize(observation with
                            { At = DateTimeOffset.Now, Note = "status-write: " + exception.Message }));
                    }
                }, keepForShutdown: () => shutdown.WaitOne(0));
            if (engine.HeldForShutdown)
            {
                Save(new("Held", "关机期间保持限充，最多 3 天。", limit, DateTimeOffset.Now, previous));
                return 0;
            }
            var reason = engine.StopReason is { } text ? text : "";
            Save(new(restored ? "Stopped" : "Unconfirmed",
                (restored ? "限充已停止，已恢复原值。" : StoppedUnconfirmed) + reason,
                limit, DateTimeOffset.Now, previous));
            return restored ? 0 : 1;
        }
        catch (Exception exception)
        {
            try { Save(new("Error", "限充未启用：" + exception.Message, limit, DateTimeOffset.Now)); } catch (Exception) { }
            return 2;
        }
    }

    public static HoldChargeStatus? ReadStatus()
    {
        try { return JsonSerializer.Deserialize<HoldChargeStatus>(File.ReadAllText(StatusPath)); }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    private static void Save(HoldChargeStatus status) => ReplaceFile(StatusPath, JsonSerializer.Serialize(status));

    /// <summary>
    /// Replaces the file in one step. Windows refuses to replace a file that anyone has open, even
    /// read-only for a moment (the window reads the status every second, and virus scanners look
    /// at new files), so the replacement is retried briefly. The temporary file never stays behind.
    /// </summary>
    public static void ReplaceFile(string path, string contents)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, contents);
            for (var attempt = 1; ; attempt++)
            {
                try { File.Move(temporary, path, true); return; }
                catch (Exception exception) when (attempt < 5 && exception is IOException or UnauthorizedAccessException)
                { Thread.Sleep(20); }
            }
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>Temporary status files that version 1.0.6 left behind after a refused replacement.</summary>
    public static void RemoveStaleTemporaryFiles(string folder)
    {
        try
        {
            foreach (var stale in new DirectoryInfo(folder).GetFiles("hold-status.json.*.tmp"))
                stale.Delete();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private static void TryAppend(string path, string line)
    {
        try { File.AppendAllText(path, line + "\n"); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private static IDisposable Acquire(string name)
    {
        var mutex = new Mutex(false, name);
        bool held;
        try { held = mutex.WaitOne(0); } catch (AbandonedMutexException) { held = true; }
        if (!held) { mutex.Dispose(); throw new InvalidOperationException("另一个限充会话或恢复仍在运行。"); }
        return new Release(mutex);
    }

    private sealed class Release(Mutex mutex) : IDisposable
    {
        public void Dispose() { mutex.ReleaseMutex(); mutex.Dispose(); }
    }
}

