using RevoHelper.App.Lifecycle;
using RevoHelper.Core.Hardware;

namespace RevoHelper.App.Tests.Lifecycle;

public sealed class DiscoveryIdentityMergerTests
{
    [Fact]
    public void FirstFailedScan_NeverInfersManufacturer()
    {
        var unknown = new MachineIdentity("", "", "Board", "CPU", "");
        Assert.Empty(DiscoveryIdentityMerger.Merge(null, unknown).Manufacturer);
    }

    [Fact]
    public void TransientRecheckFailure_RetainsPreviouslyObservedIdentity()
    {
        var previous = new MachineIdentity("MECHREVO", "AURORA X", "Board", "CPU", "1.0", 8, 16);
        var failed = new MachineIdentity("", "", "", "", "");
        var merged = DiscoveryIdentityMerger.Merge(previous, failed);

        Assert.Equal("MECHREVO", merged.Manufacturer);
        Assert.Equal("AURORA X", merged.ProductName);
        Assert.Equal("Board", merged.BaseboardProduct);
        Assert.Equal("CPU", merged.ProcessorName);
        Assert.Equal(8, merged.ProcessorCoreCount);
    }

    [Fact]
    public void ExplicitOtherBrand_IsAuthoritative_AndDoesNotBorrowMechrevoFields()
    {
        var previous = new MachineIdentity("MECHREVO", "AURORA X", "Board", "CPU", "1.0");
        var different = new MachineIdentity("OTHER", "Other model", "", "", "");

        Assert.Same(different, DiscoveryIdentityMerger.Merge(previous, different));
    }
}
