using RevoHelper.App.Lifecycle;
using RevoHelper.App.Presentation;
using RevoHelper.App.Settings;
using RevoHelper.Core.Control;
using RevoHelper.Core.Dashboard;
using RevoHelper.Core.Hardware;

namespace RevoHelper.App.Tests.Presentation;

public sealed class LateRuntimeCapabilityTests
{
    [Fact]
    public void LateModesSupplementEarlyCpuOnlyEvidenceAndEnablePlainTurboWithoutStrategyButtons()
    {
        var vm = new MainWindowViewModel(HomeDashboardFactory.Create(new("MECHREVO", "test", "", "CPU", ""), []),
            new(), new MemorySettings(), new NoStartup(), platformModeController: new AvailablePlatform());
        var early = OemRuntimeEvidence.None with { HasCpuPowerLimits = true };
        vm.ApplyRuntimeCapabilities(new(vm.Dashboard.Identity, new Dictionary<HardwareFeature, HardwareFeatureCapability>(), early.SupportedPlatformModes, []));
        Assert.True(vm.CanUseTurboQuietMode);
        Assert.False(vm.CanUseTurboHighMode);
        var complete = RuntimeEvidenceMerger.Merge(early, OemRuntimeEvidence.None with
        { SupportedPlatformModes = new HashSet<PlatformMode> { PlatformMode.Office, PlatformMode.Balanced, PlatformMode.TurboQuiet } });
        vm.ApplyRuntimeCapabilities(new(vm.Dashboard.Identity, new Dictionary<HardwareFeature, HardwareFeatureCapability>(), complete.SupportedPlatformModes, []));
        Assert.True(complete.HasCpuPowerLimits);
        Assert.True(vm.CanUseTurboQuietMode);
        Assert.True(vm.ShowTurboMode);
        Assert.False(vm.ShowTurboStrategies);
        Assert.False(vm.CanUseTurboHighMode);
        Assert.Same(complete, RuntimeEvidenceMerger.Merge(complete, OemRuntimeEvidence.None));
        Assert.Same(complete, RuntimeEvidenceMerger.Merge(complete, complete));
    }

    private sealed class AvailablePlatform : IPlatformModeController
    {
        public ValueTask<PlatformModeSelection?> ReadPlatformModeAsync(CancellationToken token) =>
            ValueTask.FromResult<PlatformModeSelection?>(new(PlatformMode.Balanced));
        public ValueTask<ControlResult> SetPlatformModeAsync(PlatformModeSelection selection, CancellationToken token) =>
            ValueTask.FromResult(new ControlResult(true, "ok"));
    }

    private sealed class MemorySettings : IApplicationSettingsStore
    {
        public ApplicationSettings Load() => new();
        public void Save(ApplicationSettings settings) { }
    }

    private sealed class NoStartup : IStartupRegistration
    {
        public bool IsEnabled() => false;
        public void SetEnabled(bool enabled) { }
    }
}
