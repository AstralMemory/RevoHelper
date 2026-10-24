using RevoHelper.Hardware.Windows.Discovery;

namespace RevoHelper.Hardware.Windows.Tests.Discovery;

public sealed class OemControlInventoryReaderTests
{
    [Fact]
    public void Read_ReportsCompleteTypedControlChainWhenRequiredComponentsExist()
    {
        var probe = new StubProbe(present:
        [
            "package:CCU.WinUI",
            "service:GCUBridge",
            "file:GCUService.exe",
            "file:ACPIDriverDll.dll",
            "service:UWACPIDriver",
            "pnp:ACPI\\INOU0000",
            "directory:UserPofiles",
            "file:LibreHardwareMonitorLib.dll"
        ]);

        var inventory = new OemControlInventoryReader(probe).Read();

        Assert.True(inventory.HasCompletePlatformModeChain);
        Assert.Equal(
            "CCU.WinUI -> GCUBridge/GCUService -> ACPIDriverDll -> UWACPIDriver -> ACPI\\INOU0000",
            inventory.PlatformModeChain);
        Assert.All(inventory.Components, component => Assert.True(component.Present));
    }

    [Fact]
    public void Read_ReportsIncompleteChainWhenAcpiEndpointIsMissing()
    {
        var probe = new StubProbe(present:
        [
            "package:CCU.WinUI",
            "service:GCUBridge",
            "file:GCUService.exe",
            "file:ACPIDriverDll.dll",
            "service:UWACPIDriver"
        ]);

        var inventory = new OemControlInventoryReader(probe).Read();

        Assert.False(inventory.HasCompletePlatformModeChain);
        Assert.False(inventory.Components.Single(component => component.Name == "ACPI\\INOU0000").Present);
    }

    [Fact]
    public void Read_OfficialUiIsEvidenceButIsNotRequiredForReplacementRuntime()
    {
        var probe = new StubProbe(present:
        [
            "service:GCUBridge",
            "file:GCUService.exe",
            "file:ACPIDriverDll.dll",
            "service:UWACPIDriver",
            "pnp:ACPI\\INOU0000"
        ]);

        var inventory = new OemControlInventoryReader(probe).Read();

        Assert.True(inventory.HasCompletePlatformModeChain);
        Assert.False(inventory.Components.Single(component => component.Name == "CCU.WinUI").Present);
    }

    private sealed class StubProbe(IEnumerable<string> present) : IOemEnvironmentProbe
    {
        private readonly HashSet<string> present = new(present, StringComparer.OrdinalIgnoreCase);

        public OemComponentEvidence Inspect(string probeKey) =>
            this.present.Contains(probeKey)
                ? new(true, $"C:\\evidence\\{probeKey.Replace(':', '_')}", "1.0", "present")
                : new(false, null, null, "not found");
    }
}
