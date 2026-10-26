using RevoHelper.App.Lifecycle;
using RevoHelper.App.Presentation;
using RevoHelper.Core.Control;
using RevoHelper.Core.Dashboard;
using RevoHelper.Core.Hardware;

namespace RevoHelper.App.Tests.Presentation;

public sealed class LateRuntimeCapabilityTests
{
    [Fact]
    public void LateModesSupplementEarlyCpuOnlyEvidenceAndEnablePlainTurboWithoutStrategyButtons()
    {
        var vm = new MainWindowViewModel(HomeDashboardFactory.Create(new("MECHREVO", "test", "", "CPU", ""), []));
        var early = OemRuntimeEvidence.None with { HasCpuPowerLimits = true };
        vm.ApplyRuntimeCapabilities(new(vm.Dashboard.Identity, new Dictionary<HardwareFeature, HardwareFeatureCapability>(), early.SupportedPlatformModes, []));
        Assert.False(vm.CanUseTurboQuietMode);
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
}
