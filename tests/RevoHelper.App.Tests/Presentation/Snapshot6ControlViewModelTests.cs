using RevoHelper.App.Presentation;
using RevoHelper.App.Settings;
using RevoHelper.Core.Control;
using RevoHelper.Core.Dashboard;
using RevoHelper.Core.Hardware;

namespace RevoHelper.App.Tests.Presentation;

public sealed class Snapshot6ControlViewModelTests
{
    [Theory]
    [InlineData(PlatformMode.TurboQuiet)]
    [InlineData(PlatformMode.TurboHigh)]
    public async Task MainTurboCardUsesBasicModeAndReconcilesFrequencyForActualStrategy(PlatformMode actual)
    {
        var platform = new TurboPlatform { BasicResultMode = actual };
        var frequency = new Frequency();
        var settings = new ApplicationSettings(PerformanceProfiles: new()
        {
            ["turbo:Quiet"] = new(75, 85, ProcessorBoostMode.Aggressive, 4200, true),
            ["turbo:HighEfficiency"] = new(75, 85, ProcessorBoostMode.Aggressive, 0, true)
        });
        var vm = Create(platform: platform, frequency: frequency, settings: settings);
        vm.ApplyRuntimeCapabilities(new(vm.Dashboard.Identity,
            new Dictionary<HardwareFeature, HardwareFeatureCapability>(),
            Enum.GetValues<PlatformMode>().ToHashSet(), []));
        await vm.InitializePlatformModeAsync(default);

        Assert.True(await vm.ApplyPlatformProfileAsync("turbo", default), vm.StatusMessage);

        Assert.Equal(1, platform.BasicCalls);
        Assert.Equal(0, platform.StrategyCalls);
        Assert.Equal(actual, vm.ConfirmedPlatformSelection?.Mode);
        Assert.Equal(actual == PlatformMode.TurboHigh ? TurboStrategy.HighEfficiency : TurboStrategy.Quiet,
            vm.SelectedTurboStrategy);
        Assert.Equal(ProcessorFrequencyLimitPolicy.Uniform(actual == PlatformMode.TurboHigh ? 0u : 4200u), frequency.Current);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectedExplicitStrategyKeepsConfirmedTurboAndDoesNotApplyUnconfirmedStrategySettings(bool scheduled)
    {
        var platform = new TurboPlatform();
        var frequency = scheduled ? new Frequency() : null;
        var vm = Create(platform: platform, frequency: frequency,
            settings: new(PerformanceProfiles: new()
            {
                ["turbo:Quiet"] = new(75, 85, ProcessorBoostMode.Aggressive, 4200, true),
                ["turbo:HighEfficiency"] = new(75, 85, ProcessorBoostMode.Aggressive, 0, true)
            }));
        vm.ApplyRuntimeCapabilities(new(vm.Dashboard.Identity,
            new Dictionary<HardwareFeature, HardwareFeatureCapability>(),
            Enum.GetValues<PlatformMode>().ToHashSet(), []));
        await vm.InitializePlatformModeAsync(default);

        Assert.False(await vm.ApplyTurboStrategyAsync(TurboStrategy.HighEfficiency, default));

        Assert.Equal(0, platform.BasicCalls);
        Assert.Equal(1, platform.StrategyCalls);
        Assert.Equal(PlatformMode.TurboQuiet, vm.ConfirmedPlatformSelection?.Mode);
        Assert.Equal("turbo", vm.SelectedProfileId);
        Assert.Equal(TurboStrategy.Quiet, vm.SelectedTurboStrategy);
        Assert.Contains("strategy unavailable", vm.StatusMessage);
        Assert.Equal(scheduled ? 1 : 0, frequency?.Writes ?? 0);
        if (frequency is not null) Assert.Equal(ProcessorFrequencyLimitPolicy.Uniform(4200), frequency.Current);
    }

    [Fact]
    public void StartupSaveFailureRestoresFullPreviousRegistration()
    {
        var startup = new Startup();
        var vm = Create(startup: startup, store: new Store { FailSave = true });

        vm.StartWithWindows = true;

        Assert.False(vm.StartWithWindows);
        Assert.Equal("old executable and disabled configuration", startup.Configuration);
        Assert.Equal(1, startup.Restores);
        Assert.Contains("已恢复", vm.StatusMessage);
    }

    [Fact]
    public void StartupPartialFailureShowsActualEnabledStateAndRecordsSpecificError()
    {
        var startup = new Startup { FailAfterWrite = true };
        var logs = new List<string>();
        var vm = Create(startup: startup, diagnostic: (area, message) => logs.Add(area + ":" + message));

        vm.StartWithWindows = true;

        Assert.True(vm.StartWithWindows);
        Assert.Contains("已启用", vm.StatusMessage);
        Assert.DoesNotContain("未修改", vm.StatusMessage);
        Assert.Contains(logs, entry => entry.Contains("startup", StringComparison.Ordinal) &&
            entry.Contains("legacy cleanup and rollback failed", StringComparison.Ordinal));
    }

    [Fact]
    public void StartupReadFailureDoesNotClaimUnchangedOrSuccessfulRollback()
    {
        var startup = new Startup { FailAfterWrite = true, FailReadsAfterWrite = true };
        var vm = Create(startup: startup);

        vm.StartWithWindows = true;

        Assert.Contains("无法确认", vm.StatusMessage);
        Assert.DoesNotContain("未修改", vm.StatusMessage);
    }

    [Fact]
    public void StartupSaveAndRestoreFailureSynchronizesActualState()
    {
        var startup = new Startup { FailRestore = true };
        var vm = Create(startup: startup, store: new Store { FailSave = true });

        vm.StartWithWindows = true;

        Assert.True(vm.StartWithWindows);
        Assert.Contains("恢复失败", vm.StatusMessage);
        Assert.Contains("已启用", vm.StatusMessage);
    }

    private static MainWindowViewModel Create(Startup? startup = null, Store? store = null,
        TurboPlatform? platform = null, Frequency? frequency = null, ApplicationSettings? settings = null,
        Action<string, string>? diagnostic = null) => new(
            HomeDashboardFactory.Create(new MachineIdentity("MECHREVO", "Test Series", "Test Board", "Intel CPU", "test", 8, 16), []),
            settings ?? new(), store ?? new Store(), startup ?? new Startup(),
            cpuPolicyController: frequency is null ? null : new Cpu(),
            boostController: frequency is null ? null : new Boost(),
            platformModeController: platform, frequencyLimitController: frequency,
            verificationDelay: (_, _) => Task.CompletedTask, diagnostic: diagnostic);

    private sealed class Store : IApplicationSettingsStore
    {
        public bool FailSave { get; init; }
        public ApplicationSettings Load() => new();
        public void Save(ApplicationSettings settings)
        {
            if (FailSave) throw new IOException("settings storage unavailable");
        }
    }

    private sealed record SavedStartup(bool IsEnabled, string Configuration) : StartupRegistrationState(IsEnabled);

    private sealed class Startup : IStartupRegistration
    {
        private bool enabled;
        private bool wrote;
        public string Configuration = "old executable and disabled configuration";
        public bool FailAfterWrite { get; init; }
        public bool FailReadsAfterWrite { get; init; }
        public bool FailRestore { get; init; }
        public int Restores;
        public bool IsEnabled() => wrote && FailReadsAfterWrite
            ? throw new IOException("task read unavailable") : enabled;
        public StartupRegistrationState CaptureState() => new SavedStartup(IsEnabled(), Configuration);
        public void SetEnabled(bool value)
        {
            enabled = value;
            wrote = true;
            Configuration = "new executable and configuration";
            if (FailAfterWrite) throw new IOException("legacy cleanup and rollback failed");
        }
        public void RestoreState(StartupRegistrationState state)
        {
            Restores++;
            if (FailRestore) throw new IOException("restore denied");
            var saved = Assert.IsType<SavedStartup>(state);
            enabled = saved.Enabled;
            Configuration = saved.Configuration;
        }
    }

    private sealed class TurboPlatform : IPlatformModeController
    {
        private PlatformModeSelection current = new(PlatformMode.Office);
        public PlatformMode BasicResultMode { get; init; } = PlatformMode.TurboQuiet;
        public int BasicCalls;
        public int StrategyCalls;
        public ValueTask<PlatformModeSelection?> ReadPlatformModeAsync(CancellationToken token) =>
            ValueTask.FromResult<PlatformModeSelection?>(current);
        public ValueTask<ControlResult> SetBasicTurboModeAsync(int index, CancellationToken token)
        {
            BasicCalls++;
            current = new(BasicResultMode, index);
            return ValueTask.FromResult(new ControlResult(true, "basic turbo confirmed") { ConfirmedPlatform = current });
        }
        public ValueTask<ControlResult> SetPlatformModeAsync(PlatformModeSelection target, CancellationToken token)
        {
            StrategyCalls++;
            current = new(PlatformMode.TurboQuiet);
            return ValueTask.FromResult(new ControlResult(false, "strategy unavailable; basic turbo retained")
                { ConfirmedPlatform = current });
        }
    }

    private sealed class Cpu : ICpuPolicyController
    {
        public ValueTask<CpuPolicy?> ReadCpuPolicyAsync(CancellationToken token) =>
            ValueTask.FromResult<CpuPolicy?>(new(75, 85, null));
        public ValueTask<ControlResult> ApplyCpuPolicyAsync(CpuPolicy value, CancellationToken token) =>
            throw new InvalidOperationException("Preset turbo must not write custom CPU power limits");
    }

    private sealed class Boost : IProcessorBoostController
    {
        private ProcessorBoostPolicy current = new(ProcessorBoostMode.Aggressive, ProcessorBoostMode.Aggressive);
        public ValueTask<ProcessorBoostPolicy> ReadAsync(CancellationToken token) => ValueTask.FromResult(current);
        public ValueTask<ControlResult> ApplyAsync(ProcessorBoostPolicy value, CancellationToken token)
        { current = value; return ValueTask.FromResult(new ControlResult(true, "ok")); }
    }

    private sealed class Frequency : IProcessorFrequencyLimitController
    {
        public ProcessorFrequencyLimitPolicy Current = ProcessorFrequencyLimitPolicy.Uniform(3400);
        public int Writes;
        public ValueTask<ProcessorFrequencyLimitPolicy> ReadAsync(CancellationToken token) => ValueTask.FromResult(Current);
        public ValueTask<ControlResult> ApplyAsync(ProcessorFrequencyLimitPolicy value, CancellationToken token)
        { Writes++; Current = value; return ValueTask.FromResult(new ControlResult(true, "ok")); }
    }
}
