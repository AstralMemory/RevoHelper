using RevoHelper.App.Presentation;
using RevoHelper.App.Settings;
using RevoHelper.Core.Control;
using RevoHelper.Core.Dashboard;
using RevoHelper.Core.Hardware;

namespace RevoHelper.App.Tests.Presentation;

public sealed class Snapshot7ControlViewModelTests
{
    private static readonly ProcessorBoostPolicy OfficeBoost =
        new(ProcessorBoostMode.EfficientEnabled, ProcessorBoostMode.EfficientEnabled);

    [Fact]
    public async Task MainTurboCard_PolicyFailureKeepsActualModeAndRemovesOfficeCap()
    {
        var platform = new Platform();
        var frequency = new Frequency(3400);
        var boost = new Boost { RejectAggressive = true };
        var vm = await Create(platform, frequency, boost, OfficeCapSettings(), Enum.GetValues<PlatformMode>());

        Assert.False(await vm.ApplyPlatformProfileAsync("turbo", default));

        Assert.Equal(PlatformMode.TurboQuiet, platform.Current.Mode);
        Assert.Equal(PlatformMode.TurboQuiet, vm.ConfirmedPlatformSelection?.Mode);
        Assert.Equal("turbo", vm.SelectedProfileId);
        Assert.Equal(ProcessorFrequencyLimitPolicy.Uniform(0), frequency.Current);
        Assert.Empty(platform.StrategyWrites);
        Assert.Contains("已保留", vm.StatusMessage);
        Assert.DoesNotContain("已恢复原整机模式", vm.StatusMessage);
    }

    [Fact]
    public async Task MainTurboCard_FrequencyOverwrittenRightAfterWriteIsReconciled()
    {
        var platform = new Platform();
        var frequency = new Frequency(3400) { OverwriteNextWriteWith = 3400 };
        var vm = await Create(platform, frequency, new Boost(), OfficeCapSettings(), Enum.GetValues<PlatformMode>());

        Assert.False(await vm.ApplyPlatformProfileAsync("turbo", default));

        Assert.Equal(PlatformMode.TurboQuiet, platform.Current.Mode);
        Assert.Equal(ProcessorFrequencyLimitPolicy.Uniform(0), frequency.Current);
        Assert.Empty(platform.StrategyWrites);
        Assert.Contains("已保留", vm.StatusMessage);
    }

    [Fact]
    public async Task MainTurboCard_PolicyReadFailureStillRemovesOfficeCap()
    {
        var platform = new Platform();
        var frequency = new Frequency(3400);
        var boost = new Boost();
        var vm = await Create(platform, frequency, boost, OfficeCapSettings(), Enum.GetValues<PlatformMode>());
        boost.ThrowOnRead = true;

        Assert.False(await vm.ApplyPlatformProfileAsync("turbo", default));

        Assert.Equal(PlatformMode.TurboQuiet, platform.Current.Mode);
        Assert.Equal(ProcessorFrequencyLimitPolicy.Uniform(0), frequency.Current);
        Assert.Contains("已保留", vm.StatusMessage);
    }

    [Fact]
    public async Task MainTurboCard_FrequencySyncFailureIsReportedSeparately()
    {
        var platform = new Platform();
        var frequency = new Frequency(3400) { Reject = true };
        var vm = await Create(platform, frequency, new Boost(), OfficeCapSettings(), Enum.GetValues<PlatformMode>());

        Assert.False(await vm.ApplyPlatformProfileAsync("turbo", default));

        Assert.Equal(PlatformMode.TurboQuiet, vm.ConfirmedPlatformSelection?.Mode);
        Assert.Equal(ProcessorFrequencyLimitPolicy.Uniform(3400), frequency.Current);
        Assert.Contains("simulated frequency failure", vm.StatusMessage);
    }

    [Fact]
    public async Task ModeOnly_RemovesCapThePreviousProfileApplied()
    {
        var platform = new Platform();
        var frequency = new Frequency(3400);
        var boost = new Boost();
        var vm = await Create(platform, frequency, boost, OfficeCapSettings(), PlatformMode.Office);

        Assert.True(await vm.ApplyPlatformProfileAsync("turbo", default), vm.StatusMessage);

        Assert.Equal(PlatformMode.TurboQuiet, platform.Current.Mode);
        Assert.Equal(ProcessorFrequencyLimitPolicy.Uniform(0), frequency.Current);
        Assert.Equal(OfficeBoost, boost.Current); // Mode-only still appends no Boost or PL.
        var writes = frequency.Writes;
        await vm.ObservePlatformModeAsync(platform.Current, default);
        Assert.Equal(writes, frequency.Writes);
    }

    [Fact]
    public async Task ModeOnly_AppliesTargetProfilesSavedFrequency()
    {
        var platform = new Platform();
        var frequency = new Frequency(0);
        var settings = new ApplicationSettings(PerformanceProfiles: new()
        {
            ["turbo:Quiet"] = new(75, 85, ProcessorBoostMode.Aggressive, 4200, true)
        });
        var vm = await Create(platform, frequency, new Boost(), settings, PlatformMode.Office);

        Assert.True(await vm.ApplyPlatformProfileAsync("turbo", default), vm.StatusMessage);

        Assert.Equal(ProcessorFrequencyLimitPolicy.Uniform(4200), frequency.Current);
    }

    [Fact]
    public async Task ModeOnly_WithoutAnySavedSettingLeavesUnknownWindowsValueAlone()
    {
        var platform = new Platform();
        var frequency = new Frequency(3600);
        var vm = await Create(platform, frequency, new Boost(), new(), PlatformMode.Office);

        Assert.True(await vm.ApplyPlatformProfileAsync("turbo", default), vm.StatusMessage);

        Assert.Equal(0, frequency.Writes);
        Assert.Equal(ProcessorFrequencyLimitPolicy.Uniform(3600), frequency.Current);
    }

    [Fact]
    public async Task ModeOnly_FrequencyFailureDoesNotClaimSuccessOrHideConfirmedMode()
    {
        var platform = new Platform();
        var frequency = new Frequency(3400) { Reject = true };
        var vm = await Create(platform, frequency, new Boost(), OfficeCapSettings(), PlatformMode.Office);

        Assert.False(await vm.ApplyPlatformProfileAsync("turbo", default));

        Assert.Equal(PlatformMode.TurboQuiet, vm.ConfirmedPlatformSelection?.Mode);
        Assert.Contains("simulated frequency failure", vm.StatusMessage);
    }

    private static ApplicationSettings OfficeCapSettings() => new(PerformanceProfiles: new()
    {
        ["office:1"] = new(75, 85, ProcessorBoostMode.EfficientEnabled, 3400, true)
    });

    private static async Task<MainWindowViewModel> Create(Platform platform, Frequency frequency, Boost boost,
        ApplicationSettings settings, params PlatformMode[] observedModes)
    {
        var vm = new MainWindowViewModel(
            HomeDashboardFactory.Create(new MachineIdentity("MECHREVO", "Test Series", "Test Board", "Intel CPU", "test", 8, 16), []),
            settings, new Store(), new Startup(),
            cpuPolicyController: new Cpu(), boostController: boost,
            platformModeController: platform, frequencyLimitController: frequency,
            verificationDelay: (_, _) => Task.CompletedTask);
        vm.ApplyRuntimeCapabilities(new(vm.Dashboard.Identity,
            new Dictionary<HardwareFeature, HardwareFeatureCapability>(), observedModes.ToHashSet(), []));
        await vm.InitializePlatformModeAsync(default);
        return vm;
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

    private sealed class Platform : IPlatformModeController
    {
        public PlatformModeSelection Current { get; private set; } = new(PlatformMode.Office);
        public List<PlatformMode> StrategyWrites { get; } = [];
        public ValueTask<PlatformModeSelection?> ReadPlatformModeAsync(CancellationToken token) =>
            ValueTask.FromResult<PlatformModeSelection?>(Current);
        public ValueTask<ControlResult> SetBasicTurboModeAsync(int index, CancellationToken token)
        {
            Current = new(PlatformMode.TurboQuiet, index);
            return ValueTask.FromResult(new ControlResult(true, "basic turbo confirmed") { ConfirmedPlatform = Current });
        }
        public ValueTask<ControlResult> SetPlatformModeAsync(PlatformModeSelection target, CancellationToken token)
        {
            StrategyWrites.Add(target.Mode);
            Current = target;
            return ValueTask.FromResult(new ControlResult(true, "ok") { ConfirmedPlatform = target });
        }
    }

    private sealed class Cpu : ICpuPolicyController
    {
        public ValueTask<CpuPolicy?> ReadCpuPolicyAsync(CancellationToken token) =>
            ValueTask.FromResult<CpuPolicy?>(new(75, 85, null));
        public ValueTask<ControlResult> ApplyCpuPolicyAsync(CpuPolicy value, CancellationToken token) =>
            throw new InvalidOperationException("Preset modes must not write custom CPU power limits");
    }

    private sealed class Boost : IProcessorBoostController
    {
        public ProcessorBoostPolicy Current { get; private set; } = OfficeBoost;
        public bool RejectAggressive { get; init; }
        public bool ThrowOnRead { get; set; }
        public ValueTask<ProcessorBoostPolicy> ReadAsync(CancellationToken token) => ThrowOnRead
            ? ValueTask.FromException<ProcessorBoostPolicy>(new InvalidOperationException("simulated boost read failure"))
            : ValueTask.FromResult(Current);
        public ValueTask<ControlResult> ApplyAsync(ProcessorBoostPolicy value, CancellationToken token)
        {
            if (RejectAggressive && value.AcMode == ProcessorBoostMode.Aggressive)
                return ValueTask.FromResult(ControlResult.Rejected("simulated boost failure"));
            Current = value;
            return ValueTask.FromResult(new ControlResult(true, "ok"));
        }
    }

    private sealed class Frequency(uint initial) : IProcessorFrequencyLimitController
    {
        public ProcessorFrequencyLimitPolicy Current { get; private set; } = ProcessorFrequencyLimitPolicy.Uniform(initial);
        public uint? OverwriteNextWriteWith { get; set; }
        public bool Reject { get; init; }
        public int Writes { get; private set; }
        public ValueTask<ProcessorFrequencyLimitPolicy> ReadAsync(CancellationToken token) => ValueTask.FromResult(Current);
        public ValueTask<ControlResult> ApplyAsync(ProcessorFrequencyLimitPolicy value, CancellationToken token)
        {
            Writes++;
            if (Reject) return ValueTask.FromResult(ControlResult.Rejected("simulated frequency failure"));
            Current = OverwriteNextWriteWith is { } forced ? ProcessorFrequencyLimitPolicy.Uniform(forced) : value;
            OverwriteNextWriteWith = null;
            return ValueTask.FromResult(new ControlResult(true, "ok"));
        }
    }
}
