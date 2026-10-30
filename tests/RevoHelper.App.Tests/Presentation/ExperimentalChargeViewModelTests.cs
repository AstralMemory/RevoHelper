using RevoHelper.App.Presentation;
using RevoHelper.App.Settings;
using RevoHelper.App.SystemControl;
using RevoHelper.Core.Control.ExperimentalCharge;
using RevoHelper.Core.Control.DynamicPower;
using RevoHelper.Core.Dashboard;
using RevoHelper.Core.Hardware;
using RevoHelper.Core.Telemetry;

namespace RevoHelper.App.Tests.Presentation;

public sealed class ExperimentalChargeViewModelTests
{
    [Fact]
    public void ConstructingOrChoosingOnlyChangesTheLocalDraft()
    {
        var backend = new FakeController();
        using var vm = new ExperimentalChargeViewModel(backend);
        Assert.False(vm.IsEnabled);
        Assert.Equal(80, vm.SelectedLimitPercent);
        Assert.Equal(1, vm.SelectedLimitIndex);
        vm.SelectedLimitPercent = 60;
        vm.SelectedLimitPercent = 75;
        Assert.Equal(60, vm.SelectedLimitPercent);
        Assert.Equal(0, vm.SelectedLimitIndex);
        vm.SelectedLimitIndex = 2;
        Assert.Equal(100, vm.SelectedLimitPercent);
        Assert.True(vm.IsHundredSelected);
        vm.SelectedLimitIndex = 3;
        Assert.Equal(100, vm.SelectedLimitPercent);
        Assert.Equal(0, backend.Enables);
        Assert.Equal(0, backend.Stops);
        Assert.Equal(0, backend.Recoveries);
    }

    [Fact]
    public void UnconnectedPreviewCannotEnableButDoesNotDisableNativeCare()
    {
        using var vm = new ExperimentalChargeViewModel();
        Assert.False(vm.CanEnable);
        Assert.False(vm.CanStop);
        Assert.True(vm.CanUseNativeCareModes);
        Assert.Contains("默认关闭", vm.StatusText);
    }

    [Fact]
    public async Task ExplicitEnableUsesTheSelectedLimitAndLocksCareProfiles()
    {
        var backend = new FakeController();
        using var vm = new ExperimentalChargeViewModel(backend);
        vm.SelectedLimitPercent = 60;
        await vm.EnableAsync();
        Assert.Equal(1, backend.Enables);
        Assert.Equal(60, backend.Status.LimitPercent);
        Assert.True(vm.IsEnabled);
        Assert.True(vm.CanStop);
        Assert.False(vm.CanEnable);
        Assert.True(vm.CanChooseLimit);
        Assert.True(vm.CanToggle);
        Assert.False(vm.CanUseNativeCareModes);
        vm.SelectedLimitPercent = 80;
        Assert.Equal(80, vm.SelectedLimitPercent);
        Assert.Equal(60, backend.Status.LimitPercent); // Editing alone remains a draft.
        await vm.CommitSelectedLimitAsync();
        Assert.Equal(80, backend.Status.LimitPercent);
        await vm.StopAsync();
        Assert.False(vm.IsEnabled);
        Assert.True(vm.CanEnable);
        Assert.True(vm.CanUseNativeCareModes);
    }

    [Fact]
    public async Task BusyEnableRejectsSecondEnableButPreservesAlreadyQueuedDraftChanges()
    {
        var backend = new FakeController { EnableHold = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var vm = new ExperimentalChargeViewModel(backend);
        var enable = vm.EnableAsync();
        await backend.EnableEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(vm.IsBusy);
        Assert.False(vm.CanChooseLimit);
        vm.SelectedLimitPercent = 60;
        await vm.EnableAsync();
        backend.EnableHold.SetResult();
        await enable;
        Assert.Equal(1, backend.Enables);
        Assert.Equal(60, vm.SelectedLimitPercent);
        Assert.Equal(80, backend.Status.LimitPercent);
    }

    [Fact]
    public async Task BackendFailureIsShownAndRequiresExplicitRecovery()
    {
        var backend = new FakeController { EnableError = new InvalidOperationException("模拟异常") };
        using var vm = new ExperimentalChargeViewModel(backend);
        await vm.EnableAsync();
        Assert.Contains("模拟异常", vm.StatusText);
        Assert.True(vm.RecoveryPending);
        Assert.False(vm.CanEnable);
        Assert.False(vm.CanUseNativeCareModes);
        Assert.True(vm.CanRecover);
        await vm.RecoverAsync();
        Assert.Equal(1, backend.Recoveries);
        Assert.False(vm.RecoveryPending);
        Assert.True(vm.CanEnable);
    }

    [Fact]
    public async Task StopFailureDoesNotClaimRestorationOrAllowNativeChanges()
    {
        var backend = new FakeController { StopError = new InvalidOperationException("无法恢复") };
        using var vm = new ExperimentalChargeViewModel(backend);
        await vm.EnableAsync();
        await vm.StopAsync();
        Assert.True(vm.IsEnabled);
        Assert.True(vm.RecoveryPending);
        Assert.Contains("无法恢复", vm.StatusText);
        Assert.False(vm.CanUseNativeCareModes);
    }

    [Fact]
    public async Task StatusEventsAndUiUpdatesUseTheDispatcher()
    {
        var backend = new FakeController();
        var queue = new Queue<Action>();
        using var vm = new ExperimentalChargeViewModel(backend, queue.Enqueue);
        var notifications = new List<string?>();
        vm.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        await vm.EnableAsync();
        Assert.Empty(notifications);
        Assert.Single(queue);
        queue.Dequeue()();
        Assert.Contains(nameof(vm.IsEnabled), notifications);
        Assert.Contains(nameof(vm.CanUseNativeCareModes), notifications);
        notifications.Clear();
        backend.Set(new(RecoveryPending: true, Message: "需要恢复"));
        Assert.Empty(notifications);
        queue.Dequeue()();
        Assert.Contains(nameof(vm.RecoveryPending), notifications);
        Assert.Equal("需要恢复", vm.StatusText);
    }

    [Fact]
    public async Task DiscreteSelectionWhileOffNeverEnablesUntilTheToggleIsTurnedOn()
    {
        var backend = new FakeController(); using var vm = new ExperimentalChargeViewModel(backend);
        vm.SelectedLimitIndex = 2;
        await vm.CommitSelectedLimitAsync();
        Assert.Equal(0, backend.Enables); Assert.False(vm.IsEnabled);
        await vm.SetEnabledAsync(true);
        Assert.True(vm.IsEnabled); Assert.Equal(100, backend.Status.LimitPercent);
        Assert.False(vm.CanUseNativeCareModes);
        await vm.SetEnabledAsync(false);
        Assert.False(vm.IsEnabled); Assert.True(vm.CanUseNativeCareModes);
    }

    [Fact]
    public async Task ActiveChangeWaitsForConfirmedRestorationBeforeEnablingHundred()
    {
        var backend = new FakeController(); using var vm = new ExperimentalChargeViewModel(backend);
        await vm.SetEnabledAsync(true);
        backend.StopHold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.SelectedLimitIndex = 2;
        var change = vm.CommitSelectedLimitAsync();
        await backend.StopEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(vm.IsBusy); Assert.False(vm.CanChooseLimit); Assert.False(vm.CanToggle);
        Assert.Equal(1, backend.Enables);
        backend.Set(new(Enabled: true, Busy: true, LimitPercent: 80, Message: "正在恢复旧80%会话"));
        Assert.Equal(100, vm.SelectedLimitPercent);
        backend.StopHold.SetResult(); await change;
        Assert.Equal(new[] { "enable:80", "stop", "enable:100" }, backend.Calls);
        Assert.Equal(100, backend.Status.LimitPercent); Assert.True(vm.IsEnabled);
    }

    [Fact]
    public async Task RapidQueuedLimitChangesUseOnlyLatestDraftAfterRestore()
    {
        var backend = new FakeController(); using var vm = new ExperimentalChargeViewModel(backend);
        await vm.SetEnabledAsync(true);
        backend.StopHold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.SelectedLimitIndex = 0;
        var first = vm.CommitSelectedLimitAsync();
        await backend.StopEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        vm.SelectedLimitIndex = 2;
        var latest = vm.CommitSelectedLimitAsync();
        backend.StopHold.SetResult(); await Task.WhenAll(first, latest);
        Assert.Equal(new[] { "enable:80", "stop", "enable:100" }, backend.Calls);
        Assert.Equal(100, vm.SelectedLimitPercent); Assert.True(vm.IsEnabled);
    }

    [Fact]
    public async Task StopSupersedesQueuedLimitChangesAndNoSelectionReenablesAfterward()
    {
        var backend = new FakeController(); using var vm = new ExperimentalChargeViewModel(backend);
        await vm.SetEnabledAsync(true);
        backend.StopHold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.SelectedLimitIndex = 0; var first = vm.CommitSelectedLimitAsync();
        await backend.StopEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        vm.SelectedLimitIndex = 2; var latest = vm.CommitSelectedLimitAsync();
        var stop = vm.SetEnabledAsync(false);
        backend.StopHold.SetResult(); await Task.WhenAll(first, latest, stop);
        Assert.Equal(1, backend.Enables); Assert.False(vm.IsEnabled); Assert.False(vm.RecoveryPending);
        Assert.Equal(100, vm.SelectedLimitPercent);
    }

    [Fact]
    public async Task StopDuringAnInFlightEnableRestoresBeforeAnyQueuedLimitCanRun()
    {
        var backend = new FakeController { EnableHold = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var vm = new ExperimentalChargeViewModel(backend);
        var first = vm.SetEnabledAsync(true);
        await backend.EnableEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        vm.SelectedLimitIndex = 2; var latest = vm.CommitSelectedLimitAsync();
        vm.RequestStop("模拟睡眠");
        backend.EnableHold.SetResult(); await Task.WhenAll(first, latest);
        Assert.Equal(1, backend.Enables); Assert.False(vm.IsEnabled); Assert.Equal(1, backend.Stops);
        Assert.Equal(100, vm.SelectedLimitPercent);
    }

    [Fact]
    public async Task FailedRestoreCannotBeBypassedByAnAlreadyQueuedHundredSelection()
    {
        var backend = new FakeController(); using var vm = new ExperimentalChargeViewModel(backend);
        await vm.SetEnabledAsync(true);
        backend.StopHold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        backend.StopError = new InvalidOperationException("恢复失败");
        vm.SelectedLimitIndex = 0; var first = vm.CommitSelectedLimitAsync();
        await backend.StopEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        vm.SelectedLimitIndex = 2; var latest = vm.CommitSelectedLimitAsync();
        backend.StopHold.SetResult(); await Task.WhenAll(first, latest);
        Assert.Equal(1, backend.Enables); Assert.True(vm.RecoveryPending);
        Assert.False(vm.CanToggle); Assert.False(vm.CanChooseLimit); Assert.False(vm.CanUseNativeCareModes);
        Assert.Contains("恢复失败", vm.StatusText);
        await vm.SetEnabledAsync(true); await vm.CommitSelectedLimitAsync();
        Assert.Equal(1, backend.Enables); Assert.True(vm.RecoveryPending);
    }

    [Fact]
    public async Task NonThrowingButPendingRestoreCannotEnableTheNewLimit()
    {
        var backend = new FakeController { StopLeavesRecoveryPending = true };
        using var vm = new ExperimentalChargeViewModel(backend);
        await vm.SetEnabledAsync(true);
        vm.SelectedLimitIndex = 2; await vm.CommitSelectedLimitAsync();
        Assert.Equal(1, backend.Enables); Assert.True(vm.RecoveryPending); Assert.False(vm.CanToggle);
    }

    [Fact]
    public async Task SliderAfterAnUnexpectedHealthStopDoesNotReuseStaleEnableIntent()
    {
        var backend = new FakeController(); using var vm = new ExperimentalChargeViewModel(backend);
        await vm.SetEnabledAsync(true);
        backend.Set(new(Message: "health guard stopped the session"));
        vm.SelectedLimitPercent = 60;
        await vm.CommitSelectedLimitAsync();
        Assert.Equal(1, backend.Enables);
        Assert.False(vm.IsEnabled);
        Assert.Equal(60, vm.SelectedLimitPercent);
    }

    [Fact]
    public async Task PausedSliderChangesTheRememberedTargetWithoutStoppingTheSessionIntent()
    {
        var backend = new FakeController(); using var vm = new ExperimentalChargeViewModel(backend);
        backend.Set(new(Enabled: true, PowerPaused: true, LimitPercent: 80));
        vm.SelectedLimitPercent = 60;
        await vm.CommitSelectedLimitAsync();
        Assert.Equal(0, backend.Stops);
        Assert.Equal(0, backend.StopRequests);
        Assert.Equal(new[] { "enable:60" }, backend.Calls);
    }

    [Fact]
    public void DisposeRequestsStopAndUnsubscribesWithoutASecondEnable()
    {
        var backend = new FakeController();
        var vm = new ExperimentalChargeViewModel(backend);
        var notifications = 0;
        vm.PropertyChanged += (_, _) => notifications++;
        vm.Dispose();
        vm.Dispose();
        backend.Set(new(Enabled: true));
        Assert.Equal(1, backend.StopRequests);
        Assert.Equal(0, notifications);
        Assert.False(vm.CanEnable);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(80)]
    [InlineData(100)]
    public async Task ManualPerformanceChangePreservesActiveCharge(int limitPercent)
    {
        var backend = new FakeController();
        using var vm = new ExperimentalControlsViewModel(chargeController: backend);
        vm.Charge.SelectedLimitPercent = limitPercent;
        await vm.Charge.EnableAsync();
        Assert.True(await vm.PauseForManualControlAsync());
        Assert.Equal(0, backend.Stops);
        Assert.Equal(0, backend.StopRequests);
        Assert.True(vm.Charge.IsEnabled);
        Assert.Equal(limitPercent, backend.Status.LimitPercent);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ManualPerformanceChangeIsBlockedWhileChargeIsBusyOrRecoveryPendingWithoutStopping(
        bool busy, bool recoveryPending)
    {
        var backend = new FakeController();
        var dynamic = new ShutdownDynamicController();
        using var vm = new ExperimentalControlsViewModel(dynamic, chargeController: backend);
        backend.Set(new(Enabled: true, Busy: busy, RecoveryPending: recoveryPending));

        Assert.False(await vm.PauseForManualControlAsync());

        Assert.Equal(0, backend.Stops);
        Assert.Equal(0, backend.StopRequests);
        Assert.Equal(0, dynamic.Preparations);
        Assert.True(vm.Charge.IsEnabled);
        Assert.Equal(recoveryPending, vm.Charge.RecoveryPending);
    }

    [Fact]
    public async Task ManualPerformanceChangeStillPreparesDynamicFrequencyWithoutStoppingCharge()
    {
        var backend = new FakeController();
        var dynamic = new ShutdownDynamicController();
        using var vm = new ExperimentalControlsViewModel(dynamic, chargeController: backend);
        await vm.Charge.EnableAsync();

        Assert.True(await vm.PauseForManualControlAsync());

        Assert.Equal(1, dynamic.Preparations);
        Assert.True(vm.IsDynamicEnabled);
        Assert.True(vm.Charge.IsEnabled);
        Assert.Equal(0, backend.Stops);
        Assert.Equal(0, backend.StopRequests);
    }

    [Theory]
    [InlineData(true, false, "正在切换或恢复")]
    [InlineData(false, true, "恢复未完成")]
    public async Task BlockedManualControlReportsChargeStateInsteadOfDynamicFrequency(
        bool busy, bool recoveryPending, string expected)
    {
        var backend = new FakeController();
        using var experiments = new ExperimentalControlsViewModel(chargeController: backend);
        backend.Set(new(Enabled: true, Busy: busy, RecoveryPending: recoveryPending));
        var vm = new MainWindowViewModel(
            HomeDashboardFactory.Create(new MachineIdentity("MECHREVO", "fake", "fake", "fake", "fake"), []),
            new(), new ReadOnlySettings(), new ReadOnlyStartup(), experiments: experiments);

        await vm.ApplyCustomPerformanceAsync(default);

        Assert.Contains("限充", vm.StatusMessage, StringComparison.Ordinal);
        Assert.Contains(expected, vm.StatusMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("动态频率", vm.StatusMessage, StringComparison.Ordinal);
        Assert.Equal(0, backend.Stops);
        Assert.Equal(0, backend.StopRequests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShutdownRequestsChargeStopWithoutWaitingOrChangingFrequencyRecoveryResult(bool frequencyRecoveryFails)
    {
        var backend = new FakeController { StopHold = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var dynamic = new ShutdownDynamicController { FailRecovery = frequencyRecoveryFails };
        using var vm = new ExperimentalControlsViewModel(dynamic, chargeController: backend);
        await vm.Charge.EnableAsync();
        backend.Set(backend.Status with { RecoveryPending = true });
        var shutdown = vm.ShutdownAsync(CancellationToken.None);
        try
        {
            Assert.Equal(!frequencyRecoveryFails, await shutdown.WaitAsync(TimeSpan.FromSeconds(1)));
            Assert.Equal(1, dynamic.Pauses);
            Assert.True(backend.StopRequests > 0);
            Assert.Equal(0, backend.Stops);
            Assert.True(vm.Charge.RecoveryPending);
            Assert.False(vm.Charge.CanEnable);
        }
        finally { backend.StopHold.TrySetResult(); await shutdown; }
    }

    private sealed class ShutdownDynamicController : IDynamicPowerController
    {
        public bool FailRecovery;
        public int Pauses;
        public int Preparations;
        public DynamicPowerStatus Status { get; private set; } = new(true, "测试频率限制", 0, true, false);
        public Task PauseAsync(string reason, CancellationToken token)
        {
            Pauses++;
            Status = new(false, reason, 0, FailRecovery, FailRecovery);
            return Task.CompletedTask;
        }
        public Task SetEnabledAsync(bool enabled, CancellationToken token) => Task.CompletedTask;
        public Task ObserveAsync(HardwareTelemetrySnapshot sample, CancellationToken token) => Task.CompletedTask;
        public Task RecoverAsync(CancellationToken token) => Task.CompletedTask;
        public Task PrepareForExternalChangeAsync(string reason, CancellationToken token)
        { Preparations++; return Task.CompletedTask; }
    }

    private sealed class ReadOnlySettings : IApplicationSettingsStore
    {
        public ApplicationSettings Load() => new();
        public void Save(ApplicationSettings settings) => throw new InvalidOperationException("Unexpected save");
    }

    private sealed class ReadOnlyStartup : IStartupRegistration
    {
        public bool IsEnabled() => false;
        public void SetEnabled(bool enabled) => throw new InvalidOperationException("Unexpected system write");
    }

    private sealed class FakeController : IExperimentalChargeController
    {
        public ExperimentalChargeStatus Status { get; private set; } = new();
        public event EventHandler? StatusChanged;
        public int Enables;
        public int Stops;
        public int Recoveries;
        public int StopRequests;
        public bool StopTokenWasCancelled;
        public bool StopLeavesRecoveryPending;
        public readonly List<string> Calls = [];
        public Exception? EnableError;
        public Exception? StopError;
        public TaskCompletionSource? EnableHold;
        public TaskCompletionSource? StopHold;
        public TaskCompletionSource EnableEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource StopEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Set(ExperimentalChargeStatus status) { Status = status; StatusChanged?.Invoke(this, EventArgs.Empty); }
        public async Task EnableAsync(int limitPercent, CancellationToken token = default)
        {
            Enables++;
            Calls.Add("enable:" + limitPercent);
            EnableEntered.TrySetResult();
            if (EnableHold is not null) await EnableHold.Task;
            if (EnableError is not null) throw EnableError;
            Set(new(Enabled: true, LimitPercent: limitPercent, Message: "正在限充"));
        }
        public async Task StopAsync(string reason, CancellationToken token = default)
        {
            Stops++;
            Calls.Add("stop");
            StopEntered.TrySetResult();
            StopTokenWasCancelled = token.IsCancellationRequested;
            if (StopHold is not null) await StopHold.Task;
            if (StopError is not null) throw StopError;
            Set(new(RecoveryPending: StopLeavesRecoveryPending, Message: StopLeavesRecoveryPending ? "待恢复" : "已恢复"));
        }
        public Task RecoverAsync(CancellationToken token = default)
        { Recoveries++; Set(new(Message: "已恢复")); return Task.CompletedTask; }
        public void RequestStop(string reason) => StopRequests++;
    }
}
