using RevoHelper.App.Presentation;
using RevoHelper.App.Settings;
using RevoHelper.Core.Control;
using RevoHelper.Core.Dashboard;
using RevoHelper.Core.Fans;
using RevoHelper.Core.Hardware;
using RevoHelper.Core.Control.DynamicPower;
using RevoHelper.Core.Telemetry;

namespace RevoHelper.App.Tests.Presentation;

public sealed class SnapshotProfileIsolationTests
{
    [Theory]
    [InlineData(CustomPerformanceSetting.GpuTgpWatts, 110, 100)]
    [InlineData(CustomPerformanceSetting.GpuDynamicBoostEnabled, 0, 1)]
    public async Task SingleGpuReadback_DoesNotOverwriteANewerDraft(CustomPerformanceSetting setting, int submitted, int newer)
    {
        var custom = new PendingSetting();
        var vm = Create(custom: custom);
        vm.SelectCustomProfile(1);
        await vm.OpenPerformanceAsync(default);
        SetDraft(submitted);
        var pending = vm.ApplyCustomPerformanceSettingAsync(setting, submitted, default);
        await custom.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        SetDraft(newer);
        custom.Release.TrySetResult();
        await pending;
        Assert.Equal(newer, setting == CustomPerformanceSetting.GpuTgpWatts ? vm.CustomGpuTgpWatts : vm.CustomGpuDynamicBoostEnabled ? 1 : 0);

        void SetDraft(int value)
        {
            if (setting == CustomPerformanceSetting.GpuTgpWatts) vm.CustomGpuTgpWatts = value;
            else vm.CustomGpuDynamicBoostEnabled = value == 1;
        }
    }

    [Fact]
    public async Task SingleGpuWriteException_RefreshesTheRealFieldInsteadOfLeavingTheFailedDraft()
    {
        var custom = new PendingSetting { ThrowOnApply = true };
        var vm = Create(custom: custom);
        vm.SelectCustomProfile(1);
        await vm.OpenPerformanceAsync(default);
        vm.CustomGpuTgpWatts = 70;
        var pending = vm.ApplyCustomPerformanceSettingAsync(CustomPerformanceSetting.GpuTgpWatts, 70, default);
        await custom.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        custom.Release.TrySetResult();
        await pending;
        Assert.Equal(130, vm.CustomGpuTgpWatts);
        Assert.Contains("failed", vm.CustomPerformanceStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GpuLowPowerRange_ReadOnlyRefreshExposesSeventyWithoutChangingSettings()
    {
        var custom = new PendingCustom("none");
        var vm = Create(custom: custom);
        vm.SelectCustomProfile(1);
        await vm.OpenPerformanceAsync(default);

        Assert.Equal(70, vm.CustomGpuTgpMinimum);
        Assert.Equal(150, vm.CustomGpuTgpMaximum);
        Assert.True(vm.CustomGpuDynamicBoostEnabled);
        Assert.Equal(130, vm.CustomGpuTgpWatts);
        Assert.Empty(custom.WriteProfiles);
    }

    [Fact]
    public void FanCustomSlots_DoNotReuseAnUnreadSlotsDraft()
    {
        var viewModel = Create(fan: new Fan());
        viewModel.SelectCustomProfile(1);
        var points = Enumerable.Range(0, 16).Select(i => new FanCurvePoint(i, i < 11 ? 40 + i * 4 : 255, 35, i < 11 ? i * 10 : 100)).ToArray();
        viewModel.ApplyFanTable(new("Profile1", true, points, points));
        Assert.True(viewModel.CanEditFanSettings);
        viewModel.SelectCustomProfile(2);
        Assert.False(viewModel.CanEditFanSettings);
        viewModel.SelectCustomProfile(1);
        Assert.True(viewModel.CanEditFanSettings);
    }

    [Fact]
    public async Task GpuOutput_ConfirmedTargetDoesNotConsumeNewerSelection()
    {
        var gpu = new Gpu();
        var viewModel = Create(gpu: gpu);
        viewModel.ApplyGpuOutputState(new(GpuOutputMode.Hybrid, new(true, true, true)));
        viewModel.SelectedGpuOutputMode = GpuOutputMode.Discrete;
        var pending = viewModel.ApplyGpuOutputModeAsync(default);
        viewModel.SelectedGpuOutputMode = GpuOutputMode.Integrated;
        gpu.Complete.SetResult(new(true, "confirmed"));
        await pending;
        Assert.Equal(GpuOutputMode.Discrete, gpu.Sent);
        Assert.True(viewModel.IsGpuOutputApplyEnabled);
    }

    [Theory]
    [InlineData("apply")]
    [InlineData("table")]
    [InlineData("response")]
    [InlineData("restore")]
    public async Task FanCompletionFromPreviousSlot_DoesNotPopulateNewSlotsCache(string phase)
    {
        var fan = new PendingFan(phase);
        var vm = Create(fan: fan);
        vm.SelectCustomProfile(1);
        vm.ApplyFanTable(FanTable());
        var pending = phase == "restore" ? vm.RestoreOemFanCurveAsync(default) : vm.ApplyFanCurveAsync(default);
        await fan.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        vm.SelectCustomProfile(2);
        fan.Release.SetResult();
        await pending;

        Assert.Equal(2, vm.SelectedCustomProfileIndex);
        Assert.False(vm.CanEditFanSettings);
        Assert.Empty(vm.FanPoints);
    }

    [Theory]
    [InlineData("apply")]
    [InlineData("read")]
    public async Task WholeCustomCompletionFromPreviousSlot_DoesNotReadOrOverwriteNewSlot(string phase)
    {
        var custom = new PendingCustom(phase);
        var vm = Create(custom: custom);
        vm.SelectCustomProfile(1);
        await vm.OpenPerformanceAsync(default);
        vm.CustomCpuBurstPowerLimitWatts = 150;
        var pending = vm.ApplyCustomPerformanceAsync(default);
        await custom.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        vm.SelectCustomProfile(2);
        vm.CustomCpuBurstPowerLimitWatts = 177;
        var feedbackAfterSwitch = vm.CustomPerformanceStatusText;
        custom.Release.SetResult();
        await pending;

        Assert.Equal(177, vm.CustomCpuBurstPowerLimitWatts);
        Assert.DoesNotContain(2, custom.ReadProfiles);
        Assert.Equal(feedbackAfterSwitch, vm.CustomPerformanceStatusText);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CustomEdit_ProfileSwitchDuringDynamicPauseDoesNotWriteNewSlot(bool offsets)
    {
        var custom = new PendingCustom("none");
        var dynamic = new PendingDynamic();
        using var experiments = new ExperimentalControlsViewModel(dynamic);
        var vm = Create(custom: custom, experiments: experiments);
        vm.SelectCustomProfile(1);
        await vm.OpenPerformanceAsync(default);
        vm.CustomCpuBurstPowerLimitWatts = 150;
        vm.CustomGpuCoreOffsetMhz = 120;
        var pending = offsets ? vm.ApplyGpuClockOffsetsAsync(default) : vm.ApplyCustomPerformanceAsync(default);
        await dynamic.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        vm.SelectCustomProfile(2);
        dynamic.Release.SetResult();
        await pending;

        Assert.Empty(custom.WriteProfiles);
    }

    [Fact]
    public async Task OffsetCompletionFromPreviousSlot_DoesNotReplaceCurrentFeedback()
    {
        var custom = new PendingCustom("offsets");
        var vm = Create(custom: custom);
        vm.SelectCustomProfile(1);
        await vm.OpenPerformanceAsync(default);
        vm.CustomGpuCoreOffsetMhz = 120;
        var pending = vm.ApplyGpuClockOffsetsAsync(default);
        await custom.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        vm.SelectCustomProfile(2);
        custom.Release.SetResult();
        await pending;

        Assert.DoesNotContain("old-profile-confirmed", vm.CustomPerformanceStatusText);
        Assert.DoesNotContain("old-profile-confirmed", vm.StatusMessage);
    }

    private static MainWindowViewModel Create(IOemFanController? fan = null, IGpuOutputModeController? gpu = null,
        ICustomPerformanceController? custom = null, ExperimentalControlsViewModel? experiments = null) => new(
        HomeDashboardFactory.Create(new MachineIdentity("MECHREVO", "test", "test", "CPU", ""), []),
        new(), new Store(), new Startup(), gpuOutputModeController: gpu,
        fanController: fan, lightingProfileStore: new Lighting(), customPerformanceController: custom, experiments: experiments);

    private static FanCurveTable FanTable()
    {
        var points = Enumerable.Range(0, 16).Select(i => new FanCurvePoint(i, i < 11 ? 40 + i * 4 : 255,
            35, i < 11 ? i * 10 : 100)).ToArray();
        return new("Profile1", true, points, points);
    }

    private sealed class PendingFan(string phase) : IOemFanController
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private async Task Pause(string current) { if (phase == current) { Entered.TrySetResult(); await Release.Task; } }
        public async ValueTask<FanCurveTable?> ReadAsync(CancellationToken token) { await Pause("table"); return FanTable(); }
        public async ValueTask<FanResponseSettings?> ReadResponseAsync(CancellationToken token) { await Pause("response"); return new(true, true, 300); }
        public async ValueTask<ControlResult> ApplyAsync(FanCurveTable table, FanResponseSettings response, CancellationToken token)
        { await Pause("apply"); return new(true, "old-profile-confirmed"); }
        public async ValueTask<FanRestoreResult> RestoreAsync(string name, CancellationToken token)
        { await Pause("restore"); return new(new(true, "old-profile-confirmed"), FanTable()); }
    }

    private sealed class PendingSetting : ICustomPerformanceController
    {
        private GpuPerformanceState state = new(true, 130, true, 25, 100, 500);
        public bool ThrowOnApply { get; init; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<CustomPerformanceState?> ReadAsync(int profile, CancellationToken token) => ValueTask.FromResult<CustomPerformanceState?>(
            new(true, true, 95, 145, true, state.GpuConfigurableTgpWatts, state.GpuDynamicBoostEnabled, 25, 100, 500));
        public ValueTask<ControlResult> ApplyAsync(int profile, CustomPerformanceState value, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ControlResult> ApplyGpuAsync(GpuPerformanceState value, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<GpuPerformanceState?> ReadGpuAsync(CancellationToken token) => ValueTask.FromResult<GpuPerformanceState?>(state);
        public ValueTask<GpuClockOffsetState?> ReadGpuClockOffsetsAsync(CancellationToken token) => ValueTask.FromResult<GpuClockOffsetState?>(
            new(true, true, new(100, 500), new(-300, 250, -1000, 1500), new(95, 150, 5, 25)));
        public async ValueTask<ControlResult> ApplySettingAsync(int profile, CustomPerformanceSetting setting, int value, CancellationToken token)
        {
            Entered.TrySetResult();
            await Release.Task;
            if (ThrowOnApply) throw new IOException("failed");
            state = setting switch
            {
                CustomPerformanceSetting.GpuTgpWatts => state with { GpuConfigurableTgpWatts = value },
                CustomPerformanceSetting.GpuDynamicBoostEnabled => state with { GpuDynamicBoostEnabled = value == 1 },
                _ => state
            };
            return new(true, "confirmed");
        }
    }

    private sealed class PendingCustom(string phase) : ICustomPerformanceController
    {
        private CustomPerformanceState state = new(true, true, 95, 145, true, 130, true, 25, 100, 500);
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<int> ReadProfiles { get; } = [];
        public List<int> WriteProfiles { get; } = [];
        private async Task Pause(string current) { if (phase == current) { Entered.TrySetResult(); await Release.Task; } }
        public async ValueTask<CustomPerformanceState?> ReadAsync(int profile, CancellationToken token)
        { ReadProfiles.Add(profile); if (WriteProfiles.Count > 0) await Pause("read"); return state; }
        public async ValueTask<ControlResult> ApplyAsync(int profile, CustomPerformanceState value, CancellationToken token)
        { WriteProfiles.Add(profile); state = value; await Pause("apply"); return new(true, "old-profile-confirmed"); }
        public ValueTask<GpuPerformanceState?> ReadGpuAsync(CancellationToken token) => ValueTask.FromResult<GpuPerformanceState?>(null);
        public ValueTask<ControlResult> ApplyGpuAsync(GpuPerformanceState value, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<GpuClockOffsetState?> ReadGpuClockOffsetsAsync(CancellationToken token) => ValueTask.FromResult<GpuClockOffsetState?>(
            new(true, true, new(100, 500), new(-300, 250, -1000, 1500), new(95, 150, 5, 25)));
        public async ValueTask<ControlResult> ApplyGpuClockOffsetsAsync(int profile, GpuClockOffsets offsets, CancellationToken token)
        { WriteProfiles.Add(profile); await Pause("offsets"); return new(true, "old-profile-confirmed"); }
    }

    private sealed class PendingDynamic : IDynamicPowerController
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public DynamicPowerStatus Status { get; } = new(false, "", 0, false, false);
        public async Task PrepareForExternalChangeAsync(string reason, CancellationToken token) { Entered.TrySetResult(); await Release.Task; }
        public Task SetEnabledAsync(bool enabled, CancellationToken token) => Task.CompletedTask;
        public Task ObserveAsync(HardwareTelemetrySnapshot sample, CancellationToken token) => Task.CompletedTask;
        public Task RecoverAsync(CancellationToken token) => Task.CompletedTask;
        public Task PauseAsync(string reason, CancellationToken token) => Task.CompletedTask;
    }

    private sealed class Fan : IOemFanController
    {
        public ValueTask<FanCurveTable?> ReadAsync(CancellationToken token) => ValueTask.FromResult<FanCurveTable?>(null);
        public ValueTask<FanResponseSettings?> ReadResponseAsync(CancellationToken token) => ValueTask.FromResult<FanResponseSettings?>(null);
        public ValueTask<ControlResult> ApplyAsync(FanCurveTable table, FanResponseSettings response, CancellationToken token) => ValueTask.FromResult(new ControlResult(true, ""));
        public ValueTask<FanRestoreResult> RestoreAsync(string name, CancellationToken token) => ValueTask.FromResult(new FanRestoreResult(new(false, ""), null));
    }
    private sealed class Gpu : IGpuOutputModeController
    {
        public TaskCompletionSource<ControlResult> Complete { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public GpuOutputMode Sent { get; private set; }
        public ValueTask<GpuOutputModeState?> ReadStateAsync(CancellationToken token) => ValueTask.FromResult<GpuOutputModeState?>(null);
        public async ValueTask<ControlResult> SetModeAsync(GpuOutputMode mode, CancellationToken token) { Sent = mode; return await Complete.Task; }
    }
    private sealed class Store : IApplicationSettingsStore { public ApplicationSettings Load() => new(); public void Save(ApplicationSettings settings) { } }
    private sealed class Startup : IStartupRegistration { public bool IsEnabled() => false; public void SetEnabled(bool enabled) { } }
    private sealed class Lighting : ILightingProfileStore
    {
        public LightingProfile? Load() => null;
        public void Save(LightingProfile profile) { }
        public LightingProfile Import(string path) => throw new NotSupportedException();
        public void Export(string path, LightingProfile profile) => throw new NotSupportedException();
    }
}
