using RevoHelper.Core.Control;
using RevoHelper.Core.Discovery;
using RevoHelper.Core.Hardware;

namespace RevoHelper.Core.Tests.Hardware;

public sealed class RuntimeCapabilityFactoryTests
{
    [Fact]
    public void Create_UnknownMechrevoModel_WithPositiveRuntimeEvidence_EnablesOnlyConfirmedFeatures()
    {
        var identity = new MachineIdentity(
            "MECHREVO", "Unknown Gaming Model", "Unknown Board", "CPU", "BIOS");
        var inventory = Inventory(complete: true);
        var evidence = new OemRuntimeEvidence(
            new HashSet<PlatformMode>
            {
                PlatformMode.Office,
                PlatformMode.Balanced,
                PlatformMode.Custom
            },
            HasCpuPowerLimits: true,
            HasFanCurves: false,
            HasGpuOutputModes: false,
            HasLighting: false,
            HasLiquidCooling: false);

        var snapshot = RuntimeCapabilityFactory.Create(identity, inventory, evidence,
        [
            new(CpuControlBackend.WindowsPowerPolicy, CapabilityAvailability.Available, "available")
        ]);

        Assert.True(snapshot.Supports(HardwareFeature.OemPlatformModes));
        Assert.True(snapshot.Supports(HardwareFeature.OemCpuPowerLimits));
        Assert.False(snapshot.Supports(HardwareFeature.OemFanCurves));
        Assert.False(snapshot.Supports(HardwareFeature.Lighting));
        Assert.Equal(3, snapshot.SupportedPlatformModes.Count);
    }

    [Fact]
    public void Create_KnownModel_WithIncompleteChain_DoesNotEnableOemWrites()
    {
        var identity = new MachineIdentity(
            "MECHREVO", "YAOSHI Series", "YAOSHI Series-X6AR55xY", "CPU", "BIOS");
        var evidence = new OemRuntimeEvidence(
            new HashSet<PlatformMode> { PlatformMode.Office }, true, true, true, true, true);

        var snapshot = RuntimeCapabilityFactory.Create(identity, Inventory(false), evidence, []);

        Assert.False(snapshot.Supports(HardwareFeature.OemPlatformModes));
        Assert.False(snapshot.Supports(HardwareFeature.OemCpuPowerLimits));
        Assert.Contains("control chain", snapshot[HardwareFeature.OemPlatformModes].Reason,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_NonMechrevo_DoesNotEnableOemWritesEvenWhenACompatibleChainIsPresent()
    {
        var identity = new MachineIdentity("OTHER", "Model", "Board", "CPU", "BIOS");
        var evidence = new OemRuntimeEvidence(
            new HashSet<PlatformMode> { PlatformMode.Office }, true, true, true, true, true);

        var snapshot = RuntimeCapabilityFactory.Create(identity, Inventory(true), evidence, []);

        Assert.False(snapshot.Supports(HardwareFeature.OemPlatformModes));
        Assert.Equal(CapabilityAvailability.Unavailable,
            snapshot[HardwareFeature.OemPlatformModes].Availability);
    }

    private static OemControlInventory Inventory(bool complete) =>
        new([], complete, "test-chain");
}
