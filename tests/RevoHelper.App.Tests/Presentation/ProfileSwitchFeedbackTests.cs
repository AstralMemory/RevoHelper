using RevoHelper.App.Presentation;
using RevoHelper.App.Settings;
using RevoHelper.Core.Control;
using RevoHelper.Core.Dashboard;
using RevoHelper.Core.Hardware;

namespace RevoHelper.App.Tests.Presentation;

public sealed class ProfileSwitchFeedbackTests
{
    [Fact]
    public async Task ClickUpdatesPresentationBeforeTheHardwareConfirms()
    {
        var release = NewCompletion<ControlResult>();
        var platform = new Platform { OnApply = (_, _, _) => release.Task };
        var vm = Create(platform);
        await vm.InitializePlatformModeAsync(default);
        var pendingNotifications = new List<bool>();
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(vm.IsPlatformSwitchPending))
                pendingNotifications.Add(vm.IsPlatformSwitchPending);
        };
        Assert.False(vm.IsPlatformSwitchPending);

        var switching = vm.ApplyPlatformProfileAsync("balanced", default);

        Assert.True(vm.IsPlatformSwitchPending);
        Assert.Equal("balanced", vm.SelectedProfileId);
        Assert.Equal(new PlatformModeSelection(PlatformMode.Office), platform.Current);
        Assert.Equal(new PlatformModeSelection(PlatformMode.Office), vm.ConfirmedPlatformSelection);
        Assert.False(switching.IsCompleted);
        release.SetResult(new(true, "confirmed"));
        Assert.True(await switching);
        Assert.False(vm.IsPlatformSwitchPending);
        Assert.Equal([true, false], pendingNotifications);
        Assert.Equal(new PlatformModeSelection(PlatformMode.Balanced), platform.Current);
        Assert.Equal(platform.Current, vm.ConfirmedPlatformSelection);
    }

    [Fact]
    public async Task OptimisticCustomModeKeepsEditorsLockedAndDoesNotWriteParametersBeforeConfirmation()
    {
        var release = NewCompletion<ControlResult>();
        var platform = new Platform { OnApply = (_, _, _) => release.Task };
        var cpu = new Cpu();
        var boost = new Boost();
        var custom = new Custom();
        var vm = new MainWindowViewModel(
            HomeDashboardFactory.Create(new MachineIdentity("MECHREVO", "test", "test", "CPU", ""), []),
            new(), new Store(), new Startup(), cpuPolicyController: cpu, boostController: boost,
            platformModeController: platform, customPerformanceController: custom);
        await vm.InitializePlatformModeAsync(default);

        var switching = vm.ApplyCustomProfileAsync(2, default);

        Assert.True(vm.IsPlatformSwitchPending);
        Assert.Equal("custom", vm.SelectedProfileId);
        Assert.Equal(2, vm.SelectedCustomProfileIndex);
        Assert.False(vm.CanEditAdvancedPerformance);
        Assert.False(vm.IsPerformanceApplyEnabled);
        Assert.Equal(new PlatformModeSelection(PlatformMode.Office), vm.ConfirmedPlatformSelection);
        Assert.Equal(0, cpu.Writes);
        Assert.Equal(0, boost.Writes);
        Assert.Equal(0, custom.Writes);
        release.SetResult(new(true, "confirmed"));
        Assert.True(await switching);
        Assert.False(vm.IsPlatformSwitchPending);
        Assert.Equal(0, custom.Writes);
    }

    [Fact]
    public async Task FailedSwitchRestoresTheActualModeAfterShowingTheRequestedMode()
    {
        var release = NewCompletion<ControlResult>();
        var platform = new Platform { OnApply = (_, _, _) => release.Task };
        var vm = Create(platform);
        await vm.InitializePlatformModeAsync(default);
        var switching = vm.ApplyPlatformProfileAsync("balanced", default);
        Assert.Equal("balanced", vm.SelectedProfileId);
        Assert.True(vm.IsPlatformSwitchPending);

        release.SetResult(ControlResult.Rejected("firmware rejected"));

        Assert.False(await switching);
        Assert.False(vm.IsPlatformSwitchPending);
        Assert.Equal("office", vm.SelectedProfileId);
        Assert.Contains("firmware rejected", vm.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RapidClicksShowTheLatestTargetAndSkipSupersededQueuedWrites()
    {
        var release = NewCompletion<ControlResult>();
        var latestRelease = NewCompletion<ControlResult>();
        var platform = new Platform
        {
            OnApply = (_, call, _) => call == 1 ? release.Task : latestRelease.Task
        };
        var vm = Create(platform);
        await vm.InitializePlatformModeAsync(default);
        var first = vm.ApplyPlatformProfileAsync("balanced", default);
        var superseded = vm.ApplyCustomProfileAsync(2, default);
        var latest = vm.ApplyTurboStrategyAsync(TurboStrategy.HighEfficiency, default);

        Assert.Equal("turbo", vm.SelectedProfileId);
        Assert.Equal(TurboStrategy.HighEfficiency, vm.SelectedTurboStrategy);
        Assert.True(vm.IsPlatformSwitchPending);
        Assert.Single(platform.Writes);
        release.SetResult(new(true, "old confirmed"));

        Assert.True(await first);
        Assert.False(await superseded);
        Assert.True(vm.IsPlatformSwitchPending);
        Assert.False(latest.IsCompleted);
        latestRelease.SetResult(new(true, "latest confirmed"));
        Assert.True(await latest);
        Assert.False(vm.IsPlatformSwitchPending);
        Assert.Equal([new(PlatformMode.Balanced), new PlatformModeSelection(PlatformMode.TurboHigh)], platform.Writes);
        Assert.Equal("turbo", vm.SelectedProfileId);
        Assert.Equal(TurboStrategy.HighEfficiency, vm.SelectedTurboStrategy);
    }

    [Fact]
    public async Task FailedLatestRequestFallsBackToTheModeConfirmedByTheEarlierRequest()
    {
        var release = NewCompletion<ControlResult>();
        var platform = new Platform
        {
            OnApply = (_, call, _) => call == 1 ? release.Task : Task.FromResult(ControlResult.Rejected("latest rejected"))
        };
        var vm = Create(platform);
        await vm.InitializePlatformModeAsync(default);
        var first = vm.ApplyPlatformProfileAsync("balanced", default);
        var latest = vm.ApplyCustomProfileAsync(3, default);
        Assert.Equal("custom", vm.SelectedProfileId);
        Assert.Equal(3, vm.SelectedCustomProfileIndex);
        Assert.True(vm.IsPlatformSwitchPending);

        release.SetResult(new(true, "first confirmed"));

        Assert.True(await first);
        Assert.False(await latest);
        Assert.False(vm.IsPlatformSwitchPending);
        Assert.Equal("balanced", vm.SelectedProfileId);
        Assert.Contains("latest rejected", vm.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadbackNotificationsCannotReplacePendingUiButLaterExternalChangesStillSynchronize()
    {
        var release = NewCompletion<ControlResult>();
        var platform = new Platform { OnApply = (_, _, _) => release.Task };
        var vm = Create(platform);
        await vm.InitializePlatformModeAsync(default);
        var switching = vm.ApplyCustomProfileAsync(2, default);

        Assert.False(await vm.ObservePlatformModeAsync(new(PlatformMode.Office), default));
        Assert.Equal("custom", vm.SelectedProfileId);
        Assert.Equal(2, vm.SelectedCustomProfileIndex);
        release.SetResult(new(true, "confirmed"));
        Assert.True(await switching);

        platform.Current = new(PlatformMode.TurboHigh);
        Assert.True(await vm.ObservePlatformModeAsync(platform.Current, default));
        Assert.Equal("turbo", vm.SelectedProfileId);
        Assert.Equal(TurboStrategy.HighEfficiency, vm.SelectedTurboStrategy);
    }

    [Fact]
    public async Task FailureUsesFreshHardwareReadbackWhenAnotherControllerChangedTheMode()
    {
        var release = NewCompletion<ControlResult>();
        var platform = new Platform { OnApply = (_, _, _) => release.Task };
        var vm = Create(platform);
        await vm.InitializePlatformModeAsync(default);
        var switching = vm.ApplyPlatformProfileAsync("balanced", default);
        platform.Current = new(PlatformMode.Custom, 2);
        release.SetResult(ControlResult.Rejected("changed externally"));

        Assert.False(await switching);
        Assert.Equal("custom", vm.SelectedProfileId);
        Assert.Equal(2, vm.SelectedCustomProfileIndex);
        Assert.Single(platform.Writes);
    }

    [Fact]
    public async Task DelayedInitializationCannotOverwriteANewerManualSelection()
    {
        var initialRead = NewCompletion<PlatformModeSelection?>();
        var release = NewCompletion<ControlResult>();
        var platform = new Platform
        {
            OnRead = call => call == 1 ? initialRead.Task : Task.FromResult<PlatformModeSelection?>(new(PlatformMode.Office)),
            OnApply = (_, _, _) => release.Task
        };
        var vm = Create(platform);
        var initialization = vm.InitializePlatformModeAsync(default);
        var switching = vm.ApplyPlatformProfileAsync("balanced", default);
        Assert.Equal("balanced", vm.SelectedProfileId);

        initialRead.SetResult(new(PlatformMode.Office));
        await initialization;
        Assert.Equal("balanced", vm.SelectedProfileId);
        release.SetResult(new(true, "confirmed"));
        Assert.True(await switching);
    }

    [Fact]
    public async Task CancellingTheLatestQueuedRequestWaitsForTheInFlightModeBeforeFallingBack()
    {
        var release = NewCompletion<ControlResult>();
        var platform = new Platform { OnApply = (_, _, _) => release.Task };
        var vm = Create(platform);
        await vm.InitializePlatformModeAsync(default);
        var first = vm.ApplyPlatformProfileAsync("balanced", default);
        using var cancellation = new CancellationTokenSource();
        var latest = vm.ApplyCustomProfileAsync(2, cancellation.Token);
        cancellation.Cancel();
        Assert.False(latest.IsCompleted);
        Assert.True(vm.IsPlatformSwitchPending);

        release.SetResult(new(true, "first confirmed"));

        Assert.True(await first);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => latest);
        Assert.False(vm.IsPlatformSwitchPending);
        Assert.Equal("balanced", vm.SelectedProfileId);
        Assert.Equal(platform.Current, vm.ConfirmedPlatformSelection);
        Assert.Single(platform.Writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DelayedPolicyInitializationCannotPopulateTheNewSlotsDraftOrFeedback(bool readFails)
    {
        var initialRead = NewCompletion<CpuPolicy?>();
        var modeRelease = NewCompletion<ControlResult>();
        var platform = new Platform { Current = new(PlatformMode.Custom, 1), OnApply = (_, _, _) => modeRelease.Task };
        var cpu = new Cpu { OnRead = (call, _) => call == 1 ? initialRead.Task : Task.FromResult<CpuPolicy?>(new(45, 65, null)) };
        var vm = CreatePolicy(platform, cpu);
        await vm.InitializePlatformModeAsync(default);
        vm.SelectCustomProfile(2);
        vm.SelectedPl2Watts = 150;
        vm.SelectedPl1Watts = 125;
        vm.SelectedProcessorBoostMode = ProcessorBoostMode.Disabled;
        vm.SelectCustomProfile(1);
        var initialization = vm.InitializePerformancePolicyAsync(default);
        var switching = vm.ApplyCustomProfileAsync(2, default);
        var pendingFeedback = vm.StatusMessage;
        try
        {
            if (readFails) initialRead.SetException(new InvalidOperationException("old read failed"));
            else initialRead.SetResult(new(45, 65, null));
            await initialization;

            Assert.Equal(125, vm.SelectedPl1Watts);
            Assert.Equal(150, vm.SelectedPl2Watts);
            Assert.Equal(ProcessorBoostMode.Disabled, vm.SelectedProcessorBoostMode);
            Assert.Equal(pendingFeedback, vm.StatusMessage);
        }
        finally
        {
            modeRelease.TrySetResult(new(true, "confirmed"));
            await switching;
        }
    }

    [Fact]
    public async Task OldPolicyApplyCompletionCannotPopulateTheNewModesReadback()
    {
        var cpuRelease = NewCompletion<bool>();
        var verificationEntered = NewCompletion<bool>();
        var verificationRelease = NewCompletion<bool>();
        var modeRelease = NewCompletion<ControlResult>();
        var platform = new Platform
        {
            Current = new(PlatformMode.Custom, 1),
            OnApply = (_, call, _) => call == 1 ? Task.FromResult(new ControlResult(true, "old profile confirmed")) : modeRelease.Task
        };
        var cpu = new Cpu { BeforeApply = (call, _) => call == 1 ? cpuRelease.Task : Task.CompletedTask };
        var vm = CreatePolicy(platform, cpu, (_, _) => { verificationEntered.TrySetResult(true); return verificationRelease.Task; });
        await vm.InitializePlatformModeAsync(default);
        vm.SelectedPl2Watts = 90;
        vm.SelectedPl1Watts = 80;
        var applying = vm.ApplyPerformancePolicyAsync(default);
        Assert.Equal(1, cpu.Writes);
        var switching = vm.ApplyCustomProfileAsync(2, default);
        var newSlotsReadback = vm.PerformanceReadbackText;
        try
        {
            cpuRelease.SetResult(true);
            await Task.WhenAny(applying, verificationEntered.Task).WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal("custom", vm.SelectedProfileId);
            Assert.Equal(2, vm.SelectedCustomProfileIndex);
            Assert.Equal(newSlotsReadback, vm.PerformanceReadbackText);
            Assert.Equal(new PlatformModeSelection(PlatformMode.Custom, 1), vm.ConfirmedPlatformSelection);
        }
        finally
        {
            cpuRelease.TrySetResult(true);
            verificationRelease.TrySetResult(true);
            modeRelease.TrySetResult(new(true, "new profile confirmed"));
            await Task.WhenAll(applying, switching);
        }
    }

    [Fact]
    public async Task OldPolicyDelayedVerificationCannotReplaceTheNewSwitchFeedback()
    {
        var verificationEntered = NewCompletion<bool>();
        var verificationRelease = NewCompletion<bool>();
        var modeRelease = NewCompletion<ControlResult>();
        var platform = new Platform
        {
            Current = new(PlatformMode.Custom, 1),
            OnApply = (_, call, _) => call == 1 ? Task.FromResult(new ControlResult(true, "old profile confirmed")) : modeRelease.Task
        };
        var vm = CreatePolicy(platform, new Cpu(), (_, _) => { verificationEntered.TrySetResult(true); return verificationRelease.Task; });
        await vm.InitializePlatformModeAsync(default);
        var applying = vm.ApplyPerformancePolicyAsync(default);
        await verificationEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var switching = vm.ApplyCustomProfileAsync(2, default);
        var feedbackAfterNewClick = new List<string>();
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(vm.StatusMessage)) feedbackAfterNewClick.Add(vm.StatusMessage);
        };
        try
        {
            verificationRelease.SetResult(true);
            await applying;

            Assert.DoesNotContain(feedbackAfterNewClick, text => text.Contains("1 秒复查", StringComparison.Ordinal));
            Assert.Equal(2, vm.SelectedCustomProfileIndex);
        }
        finally
        {
            verificationRelease.TrySetResult(true);
            modeRelease.TrySetResult(new(true, "new profile confirmed"));
            await Task.WhenAll(applying, switching);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TraySlotSwitchAndFailedFallbackRequireTheDisplayedSlotsOwnGpuRanges(bool rejected)
    {
        var release = NewCompletion<ControlResult>();
        var platform = new Platform { Current = new(PlatformMode.Custom, 1), OnApply = (_, _, _) => release.Task };
        var custom = new Custom
        {
            State = new(true, true, 95, 145, true, 130, true, 25, 100, 500),
            Gpu = new(true, true, new(100, 500), new(-300, 250, -1000, 1500), new(95, 150, 5, 25))
        };
        var vm = new MainWindowViewModel(
            HomeDashboardFactory.Create(new MachineIdentity("MECHREVO", "test", "test", "CPU", ""), []),
            new(), new Store(), new Startup(), platformModeController: platform, customPerformanceController: custom);
        await vm.InitializePlatformModeAsync(default);
        await vm.OpenPerformanceAsync(default);
        Assert.True(vm.CanEditGpuPower);
        Assert.True(vm.CanEditGpuClockOffsets);
        var switching = vm.ApplyPlatformSelectionFromTrayAsync(new(PlatformMode.Custom, 2), default);
        Assert.False(vm.CanEditGpuPower);
        release.SetResult(rejected ? ControlResult.Rejected("rejected") : new(true, "confirmed"));
        Assert.Equal(!rejected, await switching);

        Assert.Equal(rejected ? 1 : 2, vm.SelectedCustomProfileIndex);
        Assert.False(vm.CanEditGpuPower);
        Assert.False(vm.CanEditGpuClockOffsets);
        Assert.Equal([1], custom.ReadProfiles);
        await vm.OpenPerformanceAsync(default);
        Assert.True(vm.CanEditGpuPower);
        Assert.True(vm.CanEditGpuClockOffsets);
        Assert.Equal([1, rejected ? 1 : 2], custom.ReadProfiles);
        Assert.Equal(0, custom.Writes);
    }

    private static TaskCompletionSource<T> NewCompletion<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static MainWindowViewModel Create(IPlatformModeController platform) => new(
        HomeDashboardFactory.Create(new MachineIdentity("MECHREVO", "test", "test", "CPU", ""), []),
        new(), new Store(), new Startup(), platformModeController: platform);

    private static MainWindowViewModel CreatePolicy(IPlatformModeController platform, Cpu cpu,
        Func<TimeSpan, CancellationToken, Task>? delay = null) => new(
        HomeDashboardFactory.Create(new MachineIdentity("MECHREVO", "test", "test", "CPU", ""), []),
        new(), new Store(), new Startup(), cpuPolicyController: cpu, boostController: new Boost(),
        platformModeController: platform, verificationDelay: delay ?? ((_, _) => Task.CompletedTask));

    private sealed class Platform : IPlatformModeController
    {
        private int reads;
        public PlatformModeSelection Current { get; set; } = new(PlatformMode.Office);
        public List<PlatformModeSelection> Writes { get; } = [];
        public Func<PlatformModeSelection, int, CancellationToken, Task<ControlResult>> OnApply { get; init; } =
            (_, _, _) => Task.FromResult(new ControlResult(true, "confirmed"));
        public Func<int, Task<PlatformModeSelection?>>? OnRead { get; init; }
        public async ValueTask<PlatformModeSelection?> ReadPlatformModeAsync(CancellationToken token) =>
            OnRead is null ? Current : await OnRead(++reads).WaitAsync(token);
        public async ValueTask<ControlResult> SetPlatformModeAsync(PlatformModeSelection selection, CancellationToken token)
        {
            Writes.Add(selection);
            var result = await OnApply(selection, Writes.Count, token).WaitAsync(token);
            if (result.Succeeded) Current = selection;
            return result;
        }
    }

    private sealed class Cpu : ICpuPolicyController
    {
        private CpuPolicy current = new(45, 65, null);
        private int reads;
        public int Writes { get; private set; }
        public Func<int, CancellationToken, Task<CpuPolicy?>>? OnRead { get; init; }
        public Func<int, CancellationToken, Task>? BeforeApply { get; init; }
        public async ValueTask<CpuPolicy?> ReadCpuPolicyAsync(CancellationToken token) =>
            OnRead is null ? current : await OnRead(++reads, token).WaitAsync(token);
        public async ValueTask<ControlResult> ApplyCpuPolicyAsync(CpuPolicy policy, CancellationToken token)
        {
            Writes++;
            if (BeforeApply is not null) await BeforeApply(Writes, token).WaitAsync(token);
            current = policy;
            return new(true, "confirmed");
        }
    }

    private sealed class Boost : IProcessorBoostController
    {
        private ProcessorBoostPolicy current = new(ProcessorBoostMode.Aggressive, ProcessorBoostMode.Aggressive);
        public int Writes { get; private set; }
        public ValueTask<ProcessorBoostPolicy> ReadAsync(CancellationToken token) => ValueTask.FromResult(current);
        public ValueTask<ControlResult> ApplyAsync(ProcessorBoostPolicy policy, CancellationToken token)
        {
            Writes++;
            current = policy;
            return ValueTask.FromResult(new ControlResult(true, "confirmed"));
        }
    }

    private sealed class Custom : ICustomPerformanceController
    {
        public int Writes { get; private set; }
        public CustomPerformanceState? State { get; init; }
        public GpuClockOffsetState? Gpu { get; init; }
        public List<int> ReadProfiles { get; } = [];
        public ValueTask<CustomPerformanceState?> ReadAsync(int profileIndex, CancellationToken token)
        {
            ReadProfiles.Add(profileIndex);
            return ValueTask.FromResult(State);
        }
        public ValueTask<GpuClockOffsetState?> ReadGpuClockOffsetsAsync(CancellationToken token) => ValueTask.FromResult(Gpu);
        public ValueTask<GpuPerformanceState?> ReadGpuAsync(CancellationToken token) =>
            ValueTask.FromResult<GpuPerformanceState?>(null);
        public ValueTask<ControlResult> ApplyAsync(int profileIndex, CustomPerformanceState state, CancellationToken token)
        {
            Writes++;
            return ValueTask.FromResult(new ControlResult(true, "unexpected custom write"));
        }
        public ValueTask<ControlResult> ApplyGpuAsync(GpuPerformanceState state, CancellationToken token)
        {
            Writes++;
            return ValueTask.FromResult(new ControlResult(true, "unexpected GPU write"));
        }
    }

    private sealed class Store : IApplicationSettingsStore
    {
        public ApplicationSettings Load() => new();
        public void Save(ApplicationSettings settings) { }
    }
    private sealed class Startup : IStartupRegistration
    {
        public bool IsEnabled() => false;
        public void SetEnabled(bool enabled) { }
    }
}
