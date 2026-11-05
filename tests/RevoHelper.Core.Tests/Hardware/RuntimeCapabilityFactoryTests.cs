using RevoHelper.Core.Control;
using RevoHelper.Core.Discovery;
using RevoHelper.Core.Hardware;

namespace RevoHelper.Core.Tests.Hardware;

public sealed class RuntimeCapabilityFactoryTests
{
    [Fact]
    public void ConfirmedFanEvidenceDoesNotDependOnModeList()
    {
        var identity = new MachineIdentity("MECHREVO", "Test", "", "CPU", "");
        var evidence = OemRuntimeEvidence.None with { HasFanCurves = true };
        var snapshot = RuntimeCapabilityFactory.Create(identity, Inventory(true), evidence, []);

        Assert.True(snapshot.Supports(HardwareFeature.OemFanCurves));
        Assert.False(snapshot.Supports(HardwareFeature.OemPlatformModes));
        Assert.Empty(snapshot.SupportedPlatformModes);
        Assert.False(snapshot.Supports(HardwareFeature.OemCpuPowerLimits));
        Assert.True(RuntimeCapabilityFactory.Create(identity, Inventory(false), evidence, [])
            .Supports(HardwareFeature.OemFanCurves));
    }

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
    public void Create_KnownModel_WithIncompleteInventory_UsesPositiveFeatureEvidence()
    {
        var identity = new MachineIdentity(
            "MECHREVO", "YAOSHI Series", "YAOSHI Series-X6AR55xY", "CPU", "BIOS");
        var evidence = new OemRuntimeEvidence(
            new HashSet<PlatformMode> { PlatformMode.Office }, true, true, true, true, true);

        var snapshot = RuntimeCapabilityFactory.Create(identity, Inventory(false), evidence, []);

        Assert.True(snapshot.Supports(HardwareFeature.OemPlatformModes));
        Assert.True(snapshot.Supports(HardwareFeature.OemCpuPowerLimits));
        Assert.True(snapshot.Supports(HardwareFeature.OemFanCurves));
        Assert.True(snapshot.Supports(HardwareFeature.GpuOutputModes));
        Assert.True(snapshot.Supports(HardwareFeature.Lighting));
        Assert.True(snapshot.Supports(HardwareFeature.LiquidCooling));
        Assert.Equal(new[] { PlatformMode.Office }, snapshot.SupportedPlatformModes);
    }

    [Theory]
    [InlineData("OTHER", true)]
    [InlineData("OTHER", false)]
    [InlineData("UNKNOWN", true)]
    [InlineData("UNKNOWN", false)]
    [InlineData("", true)]
    [InlineData("", false)]
    [InlineData("MECHREVO", false)]
    [InlineData("MECHREVO INC.", false)]
    public void Create_RuntimeEvidenceIsNotVetoedByManufacturerOrInventory(
        string manufacturer, bool completeInventory)
    {
        var identity = new MachineIdentity(manufacturer, "Model", "Board", "CPU", "BIOS");
        var evidence = new OemRuntimeEvidence(
            new HashSet<PlatformMode> { PlatformMode.Balanced }, true, false, false, false, false);

        var snapshot = RuntimeCapabilityFactory.Create(identity, Inventory(completeInventory), evidence, []);

        Assert.True(snapshot.Supports(HardwareFeature.OemPlatformModes));
        Assert.True(snapshot.Supports(HardwareFeature.OemCpuPowerLimits));
        Assert.Equal(new[] { PlatformMode.Balanced }, snapshot.SupportedPlatformModes);
        Assert.Equal(CapabilityAvailability.Available,
            snapshot[HardwareFeature.OemPlatformModes].Availability);
        Assert.Equal(CapabilityDiagnostic.None,
            snapshot[HardwareFeature.OemPlatformModes].Diagnostic);
        Assert.Equal(CapabilityAvailability.Unverified,
            snapshot[HardwareFeature.OemFanCurves].Availability);
        Assert.Equal(CapabilityAvailability.Unverified,
            snapshot[HardwareFeature.Lighting].Availability);
    }

    [Theory]
    [InlineData("MECHREVO", true)]
    [InlineData("MECHREVO", false)]
    [InlineData("OTHER", true)]
    [InlineData("UNKNOWN", false)]
    [InlineData("", false)]
    public void Create_NoRuntimeEvidence_DoesNotClaimOemFeaturesAreConfirmed(
        string manufacturer, bool completeInventory)
    {
        var identity = new MachineIdentity(manufacturer, "Model", "Board", "CPU", "BIOS");

        var snapshot = RuntimeCapabilityFactory.Create(identity, Inventory(completeInventory),
            OemRuntimeEvidence.None, []);

        foreach (var feature in new[]
                 {
                     HardwareFeature.OemPlatformModes, HardwareFeature.OemCpuPowerLimits,
                     HardwareFeature.OemFanCurves, HardwareFeature.GpuOutputModes,
                     HardwareFeature.Lighting, HardwareFeature.LiquidCooling
                 })
        {
            Assert.False(snapshot.Supports(feature));
            Assert.Equal(CapabilityAvailability.Unverified, snapshot[feature].Availability);
            Assert.Equal(CapabilityDiagnostic.AwaitingEvidence, snapshot[feature].Diagnostic);
        }
        Assert.Empty(snapshot.SupportedPlatformModes);
    }

    private static OemControlInventory Inventory(bool complete) =>
        new([], complete, "test-chain");
}
