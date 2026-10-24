using RevoHelper.App.Lifecycle;
using RevoHelper.App.Presentation;
using RevoHelper.App.Settings;
using RevoHelper.App.SystemControl;
using RevoHelper.Core.Control;
using RevoHelper.Core.Dashboard;
using RevoHelper.Core.Hardware;

namespace RevoHelper.App.Tests.Presentation;

public sealed class OtherControlsViewModelTests
{
    [Fact]
    public async Task OpenOther_ReadsFanBoostWithoutWriting()
    {
        var fanBoost = new FakeFanBoostController { State = new(true, true) };
        var vm = CreateViewModel(fanBoost: fanBoost);

        await vm.OpenOtherAsync(CancellationToken.None);

        Assert.Equal(HomeDrawer.Other, vm.ActiveDrawer);
        Assert.True(vm.IsFanBoostSupported);
        Assert.True(vm.IsFanBoostEnabled);
        Assert.Equal(1, fanBoost.Reads);
        Assert.Empty(fanBoost.Writes);
    }

    [Fact]
    public async Task ScheduleAndCancelShutdown_TracksNativeScheduleAndCountdown()
    {
        var now = new DateTimeOffset(2026, 9, 3, 4, 0, 0, TimeSpan.Zero);
        var scheduler = new FakeShutdownScheduler(now);
        var vm = CreateViewModel(scheduler: scheduler, utcNow: () => now);
        vm.SelectedShutdownMinutes = 15;

        await vm.ScheduleShutdownAsync(CancellationToken.None);

        Assert.True(vm.IsShutdownScheduled);
        Assert.Equal("15:00", vm.ShutdownCountdownText);
        Assert.Equal([15], scheduler.ScheduledMinutes);

        now = now.AddMinutes(2).AddSeconds(1);
        vm.RefreshShutdownCountdown();
        Assert.Equal("12:59", vm.ShutdownCountdownText);

        await vm.CancelShutdownAsync(CancellationToken.None);
        Assert.False(vm.IsShutdownScheduled);
        Assert.Equal("未设置", vm.ShutdownCountdownText);
        Assert.Equal(1, scheduler.Cancels);
    }

    [Fact]
    public async Task FanBoostToggle_UsesConfirmedReadback()
    {
        var fanBoost = new FakeFanBoostController { State = new(true, false) };
        var vm = CreateViewModel(fanBoost: fanBoost);
        await vm.OpenOtherAsync(CancellationToken.None);

        await vm.SetFanBoostAsync(true, CancellationToken.None);

        Assert.Equal([true], fanBoost.Writes);
        Assert.True(vm.IsFanBoostEnabled);
        Assert.Contains("强冷", vm.FanBoostStatusText);
    }

    private static MainWindowViewModel CreateViewModel(
        IFanBoostController? fanBoost = null,
        IShutdownScheduler? scheduler = null,
        Func<DateTimeOffset>? utcNow = null) => new(
        HomeDashboardFactory.Create(
            new MachineIdentity("MECHREVO", "YAOSHI", "X", "CPU", "BIOS", 24, 24), []),
        new ApplicationSettings(), new NullStore(), new NullStartup(),
        fanBoostController: fanBoost,
        shutdownScheduler: scheduler,
        utcNow: utcNow);

    private sealed class FakeShutdownScheduler(DateTimeOffset now) : IShutdownScheduler
    {
        public List<int> ScheduledMinutes { get; } = [];
        public int Cancels { get; private set; }

        public Task<ShutdownScheduleResult> ScheduleAsync(int minutes, CancellationToken cancellationToken)
        {
            ScheduledMinutes.Add(minutes);
            return Task.FromResult(new ShutdownScheduleResult(true, true, now.AddMinutes(minutes), "已设置"));
        }

        public Task<ShutdownScheduleResult> CancelAsync(CancellationToken cancellationToken)
        {
            Cancels++;
            return Task.FromResult(new ShutdownScheduleResult(true, false, null, "已取消"));
        }
    }

    private sealed class FakeFanBoostController : IFanBoostController
    {
        public FanBoostState State { get; set; } = new(false, false);
        public int Reads { get; private set; }
        public List<bool> Writes { get; } = [];

        public ValueTask<FanBoostState> ReadAsync(CancellationToken cancellationToken)
        {
            Reads++;
            return ValueTask.FromResult(State);
        }

        public ValueTask<ControlResult> SetAsync(bool enabled, CancellationToken cancellationToken)
        {
            Writes.Add(enabled);
            State = new(true, enabled);
            return ValueTask.FromResult(new ControlResult(true, enabled ? "强冷已开启" : "强冷已关闭"));
        }
    }

    private sealed class NullStore : IApplicationSettingsStore
    {
        public ApplicationSettings Load() => new();
        public void Save(ApplicationSettings settings) { }
    }

    private sealed class NullStartup : IStartupRegistration
    {
        public bool IsEnabled() => false;
        public void SetEnabled(bool enabled) { }
    }
}
