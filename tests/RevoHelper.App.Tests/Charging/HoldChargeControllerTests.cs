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
        Assert.False(rig.Controller.HoldForShutdown());

        await rig.Controller.EnableAsync(80);
        Assert.True(rig.Controller.HoldForShutdown());
        Assert.True(rig.Workers.Single().HeldForShutdown);
        Assert.False(rig.Workers.Single().Stopped);
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

    private sealed class Rig
    {
        public readonly FakeLegacy Legacy = new();
        public readonly List<int> Started = [];
        public readonly List<FakeWorker> Workers = [];
        public string? Refuse;
        public HoldChargeStatus? Wire;
        public readonly HoldChargeController Controller;

        public Rig() => Controller = new(Legacy, limit =>
        {
            Started.Add(limit);
            var worker = new FakeWorker(this, limit);
            Workers.Add(worker);
            Wire = Refuse is null
                ? new("Active", "限充运行中：已达上限，当前未充电。", limit, DateTimeOffset.Now)
                : new("Error", Refuse, limit, DateTimeOffset.Now);
            worker.Exited = Refuse is not null;
            return worker;
        }, () => Wire, TimeSpan.FromMilliseconds(5));
    }

    private sealed class FakeWorker(Rig rig, int limit) : IHoldWorker
    {
        public bool Exited, Stopped;
        public bool HasExited => Exited;
        public void SignalStop()
        {
            if (Exited) return;
            Stopped = true; Exited = true;
            rig.Wire = new("Stopped", "限充已停止，已恢复原值。", limit, DateTimeOffset.Now);
        }
        public bool HeldForShutdown;
        public void SignalShutdownHold()
        {
            HeldForShutdown = true; Exited = true;
            rig.Wire = new("Held", "关机期间保持限充，最多 3 天。", limit, DateTimeOffset.Now);
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
