using System.Diagnostics;
using System.IO;
using RevoHelper.Hardware.Windows.BatteryCharge;

namespace RevoHelper.App.Charging;

public static class ExperimentalChargeHelperHost
{
    private const string GuardianMode = "--experimental-charge-guardian";
    private const string WorkerMode = "--experimental-charge-worker";
    private const string RecoverMode = "--experimental-charge-recover";
    public static bool IsHelper(string[] args) => args.Length > 0 && args[0].StartsWith("--experimental-charge-", StringComparison.Ordinal);

    public static int Run(string[] args)
    {
        ChargeSessionFiles? files = null;
        try
        {
            if (args.Length != 5 || args[0] is not (GuardianMode or WorkerMode or RecoverMode)
                || !int.TryParse(args[2], out var limit) || limit is not (60 or 80)
                || !int.TryParse(args[3], out var owner) || owner <= 0 || !long.TryParse(args[4], out var ticks) || ticks <= 0)
                return 2;
            files = new(args[1]); files.Initialize();
            if (args[0] == WorkerMode) return RunWorker(files);
            if (args[0] == RecoverMode) return RunRecovery(files);
            return RunGuardian(files, limit, owner, ticks);
        }
        catch (Exception ex)
        {
            if (files is not null)
                try { files.SaveStatus(new("Error", "实验性限充未完成：" + ex.Message, 80, RecoveryPending: File.Exists(files.LeasePath))); } catch (Exception) { }
            return 2;
        }
    }

    private static int RunGuardian(ChargeSessionFiles files, int limit, int owner, long ownerTicks)
    {
        using var session = Acquire(@"Global\RevoHelperTemporaryBatteryPlatformTrial");
        using var legacy = Acquire(@"Global\RevoHelperNativeBatteryThresholdTrial");
        using var stop = new EventWaitHandle(false, EventResetMode.ManualReset, files.StopName);
        using var start = new EventWaitHandle(false, EventResetMode.ManualReset, files.StartName);
        files.SaveStatus(new("Starting", "正在检查控制接口与原值；尚未确认生效。", limit));
        if (!ChargeSessionFiles.IsProcess(owner, ownerTicks) || stop.WaitOne(0))
        { files.SaveStatus(new("Restored", "实验未启用，原值未改动。", limit)); return 1; }
        ChargeEcSnapshot original;
        using (var transport = new ReviewedChargeTransport()) original = transport.ReadSnapshot();
        ChargeSessionEngine.ValidateInitial(original);
        using var self = Process.GetCurrentProcess();
        var lease = new ChargeLease(files.SessionId, limit, owner, ownerTicks, self.Id,
            self.StartTime.ToUniversalTime().Ticks, 0, 0, original);
        if (File.Exists(files.LeasePath)) throw new IOException("Session already has a recovery lease.");
        files.SaveLease(lease); files.Journal("GuardianPrepared");
        Process? child = null;
        string? cause = null;
        try
        {
            child = ChargeSessionFiles.Launch(WorkerMode, files.SessionId, limit, owner, ownerTicks);
            lease = lease with { WorkerId = child.Id, WorkerStartTicks = child.StartTime.ToUniversalTime().Ticks };
            files.SaveLease(lease); start.Set();
            var lastProgress = DateTime.UtcNow;
            while (!child.WaitForExit(1000))
            {
                var online = System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online;
                try
                {
                    var updated = File.GetLastWriteTimeUtc(files.StatusPath);
                    if (updated > lastProgress) lastProgress = updated;
                }
                catch (IOException) { stop.Set(); }
                catch (UnauthorizedAccessException) { stop.Set(); }
                var stale = DateTime.UtcNow - lastProgress > TimeSpan.FromSeconds(30);
                if (stale) cause = "工作进程超过30秒没有状态进展，监护请求停止并恢复。";
                if (!ChargeSessionFiles.IsProcess(owner, ownerTicks) || stop.WaitOne(0) || !online || stale)
                {
                    stop.Set();
                    if (!child.WaitForExit(20000))
                    {
                        cause = "工作进程未及时退出，监护正在恢复；不能保证WMI provider已取消迟到调用。";
                        child.Kill();
                        if (!child.WaitForExit(5000)) throw new InvalidOperationException("无法确认工作进程已结束；不并行发起恢复。");
                    }
                    break;
                }
            }
        }
        finally
        {
            if (child is not null && child.HasExited)
            {
                using var writer = Acquire(@"Global\RevoHelperTemporaryBatteryPlatformWriter");
                FinishRecovery(files, lease, cause);
            }
            child?.Dispose();
        }
        return 0;
    }

    private static int RunWorker(ChargeSessionFiles files)
    {
        using var start = EventWaitHandle.OpenExisting(files.StartName);
        using var stop = EventWaitHandle.OpenExisting(files.StopName);
        if (!start.WaitOne(10000)) throw new InvalidOperationException("Independent monitor was not ready.");
        var lease = files.ReadLease();
        using var self = Process.GetCurrentProcess();
        if (lease.WorkerId != self.Id || lease.WorkerStartTicks != self.StartTime.ToUniversalTime().Ticks)
            throw new InvalidOperationException("Worker identity differs from the prepared lease.");
        using var writer = Acquire(@"Global\RevoHelperTemporaryBatteryPlatformWriter");
        var engine = new ChargeSessionEngine(() => new ReviewedChargeTransport());
        try
        {
            var result = engine.Run(lease.Original, lease.LimitPercent, () => stop.WaitOne(0), CheckOwners,
                () => stop.WaitOne(2000), files.Journal, snapshot =>
                {
                    var reached = (snapshot.Byte(0x07B9) & 0x80) != 0;
                    var message = reached && !snapshot.Charging && snapshot.ChargeRateMw == 0
                        ? "实验性限充运行中：已达上限，当前未充电。"
                        : snapshot.Charging ? "实验性限充运行中：低于上限，正在充电。" : "实验性限充运行中：等待充电状态确认。";
                    files.SaveStatus(new("Active", message, lease.LimitPercent, Enabled: true, Snapshot: snapshot));
                }, recoveryWait: () => Thread.Sleep(1000)); // A signalled stop must not skip firmware cache settling.
            files.SaveStatus(new("WorkerFinished", "工作进程已尝试恢复，监护正在复核。", lease.LimitPercent,
                RecoveryPending: !result.CriticalRestored || !result.CacheMatchesOriginal, Recovery: result));
        }
        catch (Exception ex)
        {
            // Engine finally already attempted independent owned recovery.
            try { files.Journal("WorkerError:" + ex.Message); } catch (Exception) { }
            files.SaveStatus(new("WorkerFinished", "实验已停止，监护正在复核恢复：" + ex.Message, lease.LimitPercent, RecoveryPending: true));
        }
        return 0;

        void CheckOwners()
        {
            if (!ChargeSessionFiles.IsProcess(lease.OwnerId, lease.OwnerStartTicks)
                || !ChargeSessionFiles.IsProcess(lease.GuardianId, lease.GuardianStartTicks))
                throw new InvalidOperationException("界面或独立监护已退出，停止实验并恢复。");
        }
    }

    private static int RunRecovery(ChargeSessionFiles files)
    {
        var lease = files.ReadLease();
        if (ChargeSessionFiles.IsProcess(lease.GuardianId, lease.GuardianStartTicks)
            || ChargeSessionFiles.IsProcess(lease.WorkerId, lease.WorkerStartTicks))
        {
            using var stop = EventWaitHandle.OpenExisting(files.StopName); stop.Set();
            return 1; // Existing monitor owns the hardware. Never race it.
        }
        using var session = Acquire(@"Global\RevoHelperTemporaryBatteryPlatformTrial");
        using var legacy = Acquire(@"Global\RevoHelperNativeBatteryThresholdTrial");
        using var writer = Acquire(@"Global\RevoHelperTemporaryBatteryPlatformWriter");
        FinishRecovery(files, lease, "手动恢复请求。");
        return 0;
    }

    private static void FinishRecovery(ChargeSessionFiles files, ChargeLease lease, string? cause)
    {
        var engine = new ChargeSessionEngine(() => new ReviewedChargeTransport());
        // A failed journal read must not suppress recovery after a dead worker.
        // The prepared lease fixes the original state; recovery still only touches
        // exact experiment-owned C3=4/B9=limit values and never unknown values.
        var intents = ChargeRecoveryIntents.ReadOrConservativeFallback(files.HasIntent);
        ChargeRecoveryResult? result = null;
        for (var pass = 0; pass < 3; pass++)
        {
            result = engine.Recover(lease.Original, lease.LimitPercent,
                intents.Threshold, intents.Platform, true,
                files.Journal, () => Thread.Sleep(1000));
            if (pass < 2) Thread.Sleep(1000); // Bounded late-provider stability check.
        }
        var restored = result is { CriticalRestored: true, CacheMatchesOriginal: true };
        if (intents.Error is not null)
            result = result! with { Errors = [.. result!.Errors, "恢复意图日志读取失败，已按已保存会话进行受限恢复：" + intents.Error] };
        files.SaveStatus(new(restored ? "Restored" : "RecoveryPending",
            restored ? "实验性限充已停止；平台、阈值和缓存观察已恢复，不能排除全部持久化副作用。"
                : "恢复尚未确认。请勿再次启用，保留诊断日志并使用恢复按钮。",
            lease.LimitPercent, RecoveryPending: !restored, Recovery: result));
        try { files.Journal(restored ? "GuardianRestored" : "GuardianRecoveryPending"); if (cause is not null) files.Journal("Cause:" + cause); } catch (Exception) { }
    }

    private static MutexLease Acquire(string name) => new(name);
    private sealed class MutexLease : IDisposable
    {
        private readonly Mutex mutex;
        public MutexLease(string name)
        {
            mutex = new(false, name);
            bool held;
            try { held = mutex.WaitOne(0); } catch (AbandonedMutexException) { held = true; }
            if (!held) { mutex.Dispose(); throw new InvalidOperationException("另一个限充实验或恢复仍在运行。"); }
        }
        public void Dispose() { mutex.ReleaseMutex(); mutex.Dispose(); }
    }
}
