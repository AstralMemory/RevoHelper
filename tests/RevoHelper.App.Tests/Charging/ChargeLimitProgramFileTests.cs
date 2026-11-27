using RevoHelper.App.Charging;
using RevoHelper.App.Lifecycle;
using RevoHelper.App.Presentation;
using RevoHelper.Core.Control.ExperimentalCharge;

namespace RevoHelper.App.Tests.Charging;

/// <summary>
/// The charge limit runs in a helper copy of RevoHelper. When the program file cannot be found
/// (moved to the Recycle Bin, for example), the switch says why and can be used again. Checked
/// through the same path the window uses: view model, session wrapper, hold controller.
/// </summary>
public sealed class ChargeLimitProgramFileTests
{
    [Fact]
    public async Task AFailedFirstStart_SaysWhy_AndWorksOnceTheFileIsFoundAgain()
    {
        var rig = new Rig { Missing = true };
        await rig.ViewModel.SetEnabledAsync(true);

        Assert.Empty(rig.Workers);
        Assert.False(rig.ViewModel.IsEnabled);
        Assert.False(rig.ViewModel.RecoveryPending);
        Assert.True(rig.ViewModel.CanEnable);
        Assert.Contains(RunningExecutable.NotFoundMessage, rig.ViewModel.StatusText);

        rig.Missing = false;
        await rig.ViewModel.SetEnabledAsync(true);
        Assert.True(rig.ViewModel.IsEnabled);
        Assert.Single(rig.Workers);
    }

    // The order stays as it is: the running limit is put back first, then the new one starts.
    // When the new one cannot start, the switch is off with nothing pending, and it can be retried.
    [Fact]
    public async Task ChangingTheLimitWhenTheFileIsGone_EndsOffWithTheOldLimitPutBack()
    {
        var rig = new Rig();
        rig.ViewModel.SelectedLimitPercent = 60;
        await rig.ViewModel.SetEnabledAsync(true);
        Assert.True(rig.ViewModel.IsEnabled);

        rig.Missing = true;
        rig.ViewModel.SelectedLimitPercent = 80;
        await rig.ViewModel.CommitSelectedLimitAsync();

        Assert.True(rig.Workers.Single().Stopped);
        Assert.False(rig.Session.Status.Enabled);
        Assert.False(rig.ViewModel.IsEnabled);
        Assert.False(rig.ViewModel.RecoveryPending);
        Assert.True(rig.ViewModel.CanEnable);
        Assert.Contains(RunningExecutable.NotFoundMessage, rig.ViewModel.StatusText);

        rig.Missing = false;
        await rig.ViewModel.SetEnabledAsync(true);
        Assert.True(rig.ViewModel.IsEnabled);
        Assert.Equal(80, rig.Session.Status.LimitPercent);
    }

    private sealed class Rig
    {
        public volatile bool Missing;
        public volatile HoldChargeStatus? Wire;
        public readonly List<Worker> Workers = [];
        public readonly SessionChargeController Session;
        public readonly ExperimentalChargeViewModel ViewModel;

        public Rig()
        {
            // The real start finds the file first (RunningExecutable.Locate) and throws this.
            var hold = new HoldChargeController(new Legacy(), limit =>
            {
                if (Missing) throw new InvalidOperationException(RunningExecutable.NotFoundMessage);
                var worker = new Worker(this, limit);
                lock (Workers) Workers.Add(worker);
                Wire = new("Active", "限充运行中：已达上限，当前未充电。", limit, DateTimeOffset.Now);
                return worker;
            }, () => Wire, TimeSpan.FromMilliseconds(5));
            Session = new SessionChargeController(hold, () => true, (_, _) => Task.CompletedTask);
            ViewModel = new ExperimentalChargeViewModel(Session);
        }
    }

    private sealed class Worker(Rig rig, int limit) : IHoldWorker
    {
        public volatile bool Exited;
        public bool Stopped;
        public bool HasExited => Exited;
        public void SignalStop()
        {
            if (Exited) return;
            Stopped = true;
            rig.Wire = new("Stopped", "限充已停止，已恢复原值。", limit, DateTimeOffset.Now);
            Exited = true;
        }
        public void SignalShutdownHold() { }
        public Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken token) => Task.FromResult(Exited);
        public void Dispose() { }
    }

    private sealed class Legacy : IExperimentalChargeController
    {
        public ExperimentalChargeStatus Status { get; } = new();
        public event EventHandler? StatusChanged { add { } remove { } }
        public Task EnableAsync(int limitPercent, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("旧方式不应再被启用。");
        public Task StopAsync(string reason, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RecoverAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void RequestStop(string reason) { }
    }
}
