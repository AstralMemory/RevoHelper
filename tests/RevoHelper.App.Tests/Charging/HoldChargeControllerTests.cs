using RevoHelper.App.Charging;
using RevoHelper.Core.Control.ExperimentalCharge;

namespace RevoHelper.App.Tests.Charging;

public sealed class HoldChargeControllerTests
{
    [Fact]
    public async Task Enable_StartsOneWorkerAndReportsWhatItReports()
    {
        var rig = new Rig();
        await rig.Controller.EnableAsync(80);

        Assert.Equal([80], rig.Started);
        Assert.True(rig.Controller.Status.Enabled);
        Assert.Equal(80, rig.Controller.Status.LimitPercent);
        Assert.Equal("限充运行中：已达上限，当前未充电。", rig.Controller.Status.Message);

        await rig.Controller.EnableAsync(80);          // same request again: nothing restarts
        Assert.Equal([80], rig.Started);
    }

    [Fact]
    public async Task Stop_WaitsForTheWorkerAndReportsOff()
    {
        var rig = new Rig();
        await rig.Controller.EnableAsync(60);
        await rig.Controller.StopAsync("用户关闭");

        Assert.True(rig.Workers.Single().Stopped);
        Assert.False(rig.Controller.Status.Enabled);
        Assert.False(rig.Controller.Status.Busy);
        Assert.False(rig.Controller.Status.RecoveryPending);
        Assert.Equal("限充已关闭；按原生策略充电。", rig.Controller.Status.Message);
    }

    [Fact]
    public async Task Shutdown_AsksARunningWorkerToKeepTheLimit_AndDoesNothingWhenOff()
    {
        var rig = new Rig();
        Assert.Null(rig.Controller.HoldForShutdown(TimeSpan.FromSeconds(4)));

        await rig.Controller.EnableAsync(80);
        Assert.Equal(ShutdownHoldResult.Held, rig.Controller.HoldForShutdown(TimeSpan.FromSeconds(4)));
        Assert.True(rig.Workers.Single().HeldForShutdown);
        Assert.False(rig.Workers.Single().Stopped);
    }

    // Windows may end every program soon after they have answered the shutdown query, so the
    // answer waits until the worker has written the long countdown, but never past the time given.
    [Fact]
    public async Task Shutdown_WaitsForTheWorkerToConfirm_ButNotPastTheTimeGiven()
    {
        var slow = new Rig { HoldDelay = TimeSpan.FromMilliseconds(300) };
        await slow.Controller.EnableAsync(80);
        Assert.Equal(ShutdownHoldResult.Held, slow.Controller.HoldForShutdown(TimeSpan.FromSeconds(10)));

        var silent = new Rig { HoldDelay = Timeout.InfiniteTimeSpan };
        await silent.Controller.EnableAsync(80);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Assert.Equal(ShutdownHoldResult.TimedOut, silent.Controller.HoldForShutdown(TimeSpan.FromMilliseconds(200)));
        Assert.InRange(clock.ElapsedMilliseconds, 200, 5000);

        var ended = new Rig { ShutdownPhase = "Stopped" };
        await ended.Controller.EnableAsync(80);
        Assert.Equal(ShutdownHoldResult.Ended, ended.Controller.HoldForShutdown(TimeSpan.FromSeconds(10)));
    }

    // A status from before the signal is no answer. Here the last one says "below the limit", but
    // the worker has just started holding and not said so yet: the wait goes on until it answers.
    [Fact]
    public async Task Shutdown_AStatusFromBeforeTheSignalIsNoAnswer()
    {
        var rig = new Rig { HoldDelay = TimeSpan.FromMilliseconds(300) };
        await rig.Controller.EnableAsync(80);
        rig.Wire = rig.Wire! with
        {
            Last = new(DateTimeOffset.Now, 79, false, 80, 0, 0, 0, true, true, 30000, "charge")
        };
        Assert.Equal(ShutdownHoldResult.Held, rig.Controller.HoldForShutdown(TimeSpan.FromSeconds(10)));
    }

    // Below the limit the worker answers as soon as it sees the signal; the restore it does next
    // is not waited for, since nothing stays applied.
    [Fact]
    public async Task Shutdown_BelowTheLimit_EndsWithTheWorkersAnswer()
    {
        var rig = new Rig { ShutdownPhase = HoldChargeHost.ReleasingPhase, HoldDelay = TimeSpan.FromMilliseconds(100) };
        await rig.Controller.EnableAsync(80);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Assert.Equal(ShutdownHoldResult.BelowLimit, rig.Controller.HoldForShutdown(TimeSpan.FromSeconds(10)));
        Assert.InRange(clock.ElapsedMilliseconds, 0, 2000);
        Assert.False(rig.Workers.Single().HasExited);
    }

    // When the worker cannot confirm the restore, no time is promised that a hold left for a
    // shutdown could outlast.
    [Fact]
    public async Task Stop_WithTheRestoreUnconfirmed_PromisesNoShortTime()
    {
        var rig = new Rig { StopPhase = "Unconfirmed" };
        await rig.Controller.EnableAsync(80);
        await rig.Controller.StopAsync("用户关闭");

        Assert.False(rig.Controller.Status.Enabled);
        Assert.Equal(HoldChargeController.OffUnconfirmed, rig.Controller.Status.Message);
        Assert.Contains("最长 3 天", HoldChargeController.OffUnconfirmed);
        Assert.Contains("最长 3 天", HoldChargeHost.StoppedUnconfirmed);
        foreach (var message in new[] { HoldChargeController.OffUnconfirmed, HoldChargeHost.StoppedUnconfirmed + "已断开电源或电池状态无效，停止并恢复。" })
        {
            var english = RevoHelper.App.Presentation.UiTextCatalog.Translate(message, "en-US");
            Assert.Contains("at most 3 days", english);
            Assert.DoesNotContain(english, c => c is >= '一' and <= '鿿');
        }
    }

    [Fact]
    public async Task ChangingTheLimit_StopsTheOldWorkerBeforeStartingTheNew()
    {
        var rig = new Rig();
        await rig.Controller.EnableAsync(80);
        await rig.Controller.EnableAsync(60);

        Assert.Equal([80, 60], rig.Started);
        Assert.True(rig.Workers[0].Stopped);
        Assert.False(rig.Workers[1].Stopped);
        Assert.Equal(60, rig.Controller.Status.LimitPercent);
    }

    [Fact]
    public async Task OneHundred_IsPassThroughWithNoWorker()
    {
        var rig = new Rig();
        await rig.Controller.EnableAsync(80);
        await rig.Controller.EnableAsync(100);

        Assert.Equal([80], rig.Started);
        Assert.True(rig.Workers[0].Stopped);
        Assert.True(rig.Controller.Status is { Enabled: true, LimitPercent: 100 });
    }

    [Fact]
    public async Task AWorkerThatRefuses_LeavesTheSwitchOffWithItsReason()
    {
        var rig = new Rig { Refuse = "限充未启用：固件当前不执行保持功能（073F=0x80 0727=0x00 097C=0x00）。" };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Controller.EnableAsync(80));

        Assert.Contains("固件当前不执行保持功能", error.Message);
        Assert.False(rig.Controller.Status.Enabled);
        Assert.False(rig.Controller.Status.RecoveryPending);     // nothing was left behind to restore
        Assert.Contains("固件当前不执行保持功能", rig.Controller.Status.Message);
    }

    [Fact]
    public async Task ASessionLeftByAnEarlierVersion_MustBeRestoredFirst_ThroughTheOldController()
    {
        var rig = new Rig();
        rig.Legacy.Set(new(RecoveryPending: true, Message: "发现未确认恢复的实验会话。"));

        Assert.True(rig.Controller.Status.RecoveryPending);
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Controller.EnableAsync(80));
        Assert.Empty(rig.Started);

        await rig.Controller.RecoverAsync();
        Assert.Equal(1, rig.Legacy.Recoveries);
        await rig.Controller.EnableAsync(80);
        Assert.Equal([80], rig.Started);
    }

    // Windows asks every program about a shutdown, the worker too. Here it was asked first, left the
    // limit applied for the shutdown and ended; then another program called the shutdown off, so
    // this process was never asked. The limit is not taken for stopped: it stays on, and a little
    // later a new worker carries it on.
    [Fact]
    public async Task AShutdownOnlyTheWorkerWasAskedAbout_KeepsTheLimitOn_AndCarriesItOn()
    {
        var rig = new Rig(heldResume: TimeSpan.FromMilliseconds(300));
        var session = new SessionChargeController(rig.Controller, () => true);
        await session.EnableAsync(80);

        rig.Workers[0].EndOnItsOwn("Held", Held);
        await Until(() => rig.Controller.Status.Message == Held);
        Assert.True(rig.Controller.Status.Enabled);
        Assert.True(session.Status.Enabled);
        // Asked now, this process reports the hold the worker already made.
        Assert.Equal(ShutdownHoldResult.HeldEarlier, rig.Controller.HoldForShutdown(TimeSpan.FromSeconds(4)));

        await Until(() => rig.Started.Count == 2 && rig.Controller.Status is { Busy: false });
        Assert.Equal([80, 80], rig.Started);
        Assert.True(rig.Controller.Status.Enabled);
        Assert.True(session.Status.Enabled);
        Assert.Equal("限充运行中：已达上限，当前未充电。", rig.Controller.Status.Message);
    }

    [Fact]
    public async Task WhileWindowsIsStillShuttingDown_NothingIsCarriedOn()
    {
        var rig = new Rig(heldResume: TimeSpan.FromMilliseconds(100)) { ShuttingDown = true };
        await rig.Controller.EnableAsync(80);
        rig.Workers[0].EndOnItsOwn("Held", Held);

        await Task.Delay(600);
        Assert.Equal([80], rig.Started);
        Assert.True(rig.Controller.Status.Enabled);

        rig.ShuttingDown = false;
        await Until(() => rig.Started.Count == 2);
    }

    // Switched off in the meantime: a worker that is told to stop before it starts takes the long
    // countdown over and puts everything back. Nothing is carried on afterwards.
    [Fact]
    public async Task SwitchedOffWhileHeld_TheCountdownIsPutBack_AndNothingCarriesOn()
    {
        var rig = new Rig(heldResume: TimeSpan.FromMilliseconds(300));
        await rig.Controller.EnableAsync(80);
        rig.Workers[0].EndOnItsOwn("Held", Held);
        await Until(() => rig.Controller.Status.Message == Held);

        await rig.Controller.StopAsync("用户关闭");

        Assert.Equal([80, 80], rig.Started);
        Assert.True(rig.Workers[1].Stopped);
        Assert.False(rig.Controller.Status.Enabled);
        Assert.Equal("限充已关闭；按原生策略充电。", rig.Controller.Status.Message);
        await Task.Delay(700);
        Assert.Equal(2, rig.Started.Count);
    }

    [Fact]
    public async Task SwitchedOffWhileHeld_ARestoreThatCannotBeConfirmed_PromisesNoShortTime()
    {
        var rig = new Rig(heldResume: TimeSpan.FromMilliseconds(300));
        await rig.Controller.EnableAsync(80);
        rig.Workers[0].EndOnItsOwn("Held", Held);
        await Until(() => rig.Controller.Status.Message == Held);
        rig.Refuse = "限充未启用：请连接电源并确认电池状态有效后，再手动启用。";

        await rig.Controller.StopAsync("用户关闭");

        Assert.Equal(HoldChargeController.OffUnconfirmed, rig.Controller.Status.Message);
        // The next stop tries again.
        rig.Refuse = null;
        await rig.Controller.StopAsync("用户关闭");
        Assert.Equal(3, rig.Started.Count);
        Assert.Equal("限充已关闭；按原生策略充电。", rig.Controller.Status.Message);
    }

    // Another limit, or none, in the meantime: what was planned for the old one is void.
    [Theory]
    [InlineData(60)]
    [InlineData(100)]
    public async Task AnotherLimitWhileHeld_ReplacesTheCarryOn(int limit)
    {
        var rig = new Rig(heldResume: TimeSpan.FromMilliseconds(300));
        await rig.Controller.EnableAsync(80);
        rig.Workers[0].EndOnItsOwn("Held", Held);
        await Until(() => rig.Controller.Status.Message == Held);

        await rig.Controller.EnableAsync(limit);
        await Task.Delay(700);

        // 60 takes the countdown over; 100 puts it back with a worker told to stop at once.
        Assert.Equal([80, limit == 100 ? 80 : 60], rig.Started);
        Assert.Equal(limit == 100, rig.Workers[1].Stopped);
        Assert.True(rig.Controller.Status is { Enabled: true, Busy: false } status && status.LimitPercent == limit);
    }

    // Only a worker that left the limit applied for a shutdown is carried on, never one that
    // stopped on its own.
    [Fact]
    public async Task AWorkerThatStopsOnItsOwn_IsNotStartedAgain()
    {
        var rig = new Rig(heldResume: TimeSpan.FromMilliseconds(100));
        await rig.Controller.EnableAsync(80);

        rig.Workers[0].EndOnItsOwn("Stopped", "限充已停止，已恢复原值。已断开电源或电池状态无效，停止并恢复。");
        await Until(() => !rig.Controller.Status.Enabled);
        await Task.Delay(500);

        Assert.Equal([80], rig.Started);
    }

    // A stop that was asked for goes ahead, even when Windows asks about a shutdown before the
    // worker has finished putting the original values back.
    [Fact]
    public async Task Shutdown_AfterAStopWasAskedFor_DoesNotHold()
    {
        var rig = new Rig { SlowStop = true };
        await rig.Controller.EnableAsync(80);
        rig.Controller.RequestStop("用户关闭");

        Assert.Null(rig.Controller.HoldForShutdown(TimeSpan.FromSeconds(4)));
        Assert.False(rig.Workers[0].HeldForShutdown);
    }

    private const string Held = "关机期间保持限充，最多 3 天。";

    private static async Task Until(Func<bool> condition)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException();
            await Task.Delay(10);
        }
    }

    private sealed class Rig
    {
        public readonly FakeLegacy Legacy = new();
        public readonly List<int> Started = [];
        public readonly List<FakeWorker> Workers = [];
        public string? Refuse;
        /// <summary>How long a worker takes to answer a shutdown; infinite for never.</summary>
        public TimeSpan? HoldDelay;
        public string ShutdownPhase = "Held";
        public string StopPhase = "Stopped";
        public volatile HoldChargeStatus? Wire;
        public volatile bool ShuttingDown;
        /// <summary>A stop is seen but takes a while: the worker has not ended yet.</summary>
        public bool SlowStop;
        public readonly HoldChargeController Controller;

        public Rig(TimeSpan? heldResume = null) => Controller = new(Legacy, limit =>
        {
            Started.Add(limit);
            var worker = new FakeWorker(this, limit);
            Workers.Add(worker);
            Wire = Refuse is null
                ? new("Active", "限充运行中：已达上限，当前未充电。", limit, DateTimeOffset.Now)
                : new("Error", Refuse, limit, DateTimeOffset.Now);
            worker.Exited = Refuse is not null;
            return worker;
        }, () => Wire, TimeSpan.FromMilliseconds(5), heldResume ?? TimeSpan.FromMinutes(10), () => ShuttingDown);
    }

    private sealed class FakeWorker(Rig rig, int limit) : IHoldWorker
    {
        public volatile bool Exited;
        public bool Stopped;
        public bool HasExited => Exited;
        public void SignalStop()
        {
            if (Exited) return;
            Stopped = true;
            if (rig.SlowStop) return;
            Exited = true;
            rig.Wire = new(rig.StopPhase, "限充已停止，已恢复原值。", limit, DateTimeOffset.Now);
        }
        /// <summary>Ends without being asked to: Windows asked it about a shutdown, or the power went.</summary>
        public void EndOnItsOwn(string phase, string message)
        {
            rig.Wire = new(phase, message, limit, DateTimeOffset.Now);
            Exited = true;
        }
        public bool HeldForShutdown;
        public void SignalShutdownHold()
        {
            HeldForShutdown = true;
            if (rig.HoldDelay == Timeout.InfiniteTimeSpan) return;
            // Like the real worker: the last status is written before the process ends. Below the
            // limit it answers first and goes on to restore.
            void Answer()
            {
                rig.Wire = new(rig.ShutdownPhase, "关机期间保持限充，最多 3 天。", limit, DateTimeOffset.Now);
                Exited = rig.ShutdownPhase != HoldChargeHost.ReleasingPhase;
            }
            if (rig.HoldDelay is { } delay) _ = Task.Delay(delay).ContinueWith(_ => Answer(), TaskScheduler.Default);
            else Answer();
        }
        public Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken token) => Task.FromResult(Exited);
        public void Dispose() { }
    }

    private sealed class FakeLegacy : IExperimentalChargeController
    {
        public ExperimentalChargeStatus Status { get; private set; } = new();
        public event EventHandler? StatusChanged;
        public int Recoveries;
        public void Set(ExperimentalChargeStatus status) { Status = status; StatusChanged?.Invoke(this, EventArgs.Empty); }
        public Task EnableAsync(int limitPercent, CancellationToken cancellationToken = default) => throw new InvalidOperationException("旧方式不应再被启用。");
        public Task StopAsync(string reason, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RecoverAsync(CancellationToken cancellationToken = default) { Recoveries++; Set(new(Message: "已恢复")); return Task.CompletedTask; }
        public void RequestStop(string reason) { }
    }
}
