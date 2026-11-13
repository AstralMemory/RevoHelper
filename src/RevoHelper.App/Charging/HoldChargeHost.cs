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
    public static string StatusPath => Path.Combine(ChargeSessionFiles.Root, "hold-status.json");
    public static bool IsHelper(string[] args) => args.Length > 0 && args[0] == Mode;

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
                    if (changed || ++sinceLogged >= 15)
                    {
                        File.AppendAllText(log, JsonSerializer.Serialize(observation) + "\n");
                        sinceLogged = 0;
                    }
                    previous = observation;
                    if (observation.Soc < 0) return;
                    var message = !observation.Holding ? "限充运行中：低于上限，正在充电。"
                        : !observation.Charging && observation.ChargeRateMw == 0 ? "限充运行中：已达上限，当前未充电。"
                        : "限充运行中：已请求停充，等待固件响应。";
                    Save(new("Active", message, selectedLimit, DateTimeOffset.Now, observation));
                }, keepForShutdown: () => shutdown.WaitOne(0));
            if (engine.HeldForShutdown)
            {
                Save(new("Held", "关机期间保持限充，最多 3 天。", limit, DateTimeOffset.Now, previous));
                return 0;
            }
            var reason = engine.StopReason is { } text ? text : "";
            Save(new(restored ? "Stopped" : "Unconfirmed",
                restored ? "限充已停止，已恢复原值。" + reason : "限充已停止，恢复未确认；固件会在约 30 分钟内自行恢复正常充电。" + reason,
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

    private static void Save(HoldChargeStatus status)
    {
        var temporary = StatusPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(status));
        File.Move(temporary, StatusPath, true);
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

