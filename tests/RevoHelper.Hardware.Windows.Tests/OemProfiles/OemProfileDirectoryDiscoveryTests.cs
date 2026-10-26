using RevoHelper.Core.Discovery;
using RevoHelper.Hardware.Windows.OemProfiles;

namespace RevoHelper.Hardware.Windows.Tests.OemProfiles;

public sealed class OemProfileDirectoryDiscoveryTests
{
    [Fact]
    public void UsesActualServiceDirectoryInsteadOfDefaultCDrive()
    {
        var inventory = new OemControlInventory(
            [new("GCUBridge", "service", true, "\"D:\\OEM Console\\AiStoneService\\GCUBridge.exe\" --service", null, "test")], true, "test");
        Assert.Equal(@"D:\OEM Console\AiStoneService\MyControlCenter\UserPofiles",
            OemCpuPowerLimitProfileReader.ResolveProfileDirectory(inventory));
    }

    [Fact]
    public void PrefersDiscoveredProfileStoreEvenWhenCurrentlyMissing()
    {
        var inventory = new OemControlInventory(
            [new("OEM profile store", "directory", false, @"D:\OEM\UserPofiles", null, "missing")], false, "test");
        Assert.Equal(@"D:\OEM\UserPofiles", OemCpuPowerLimitProfileReader.ResolveProfileDirectory(inventory));
    }

    [Fact]
    public void UsesDefaultOnlyWithoutDiscoveredInstallPath()
    {
        Assert.Equal(OemCpuPowerLimitProfileReader.DefaultProfileDirectory,
            OemCpuPowerLimitProfileReader.ResolveProfileDirectory(new([], false, "unknown")));
    }
}
