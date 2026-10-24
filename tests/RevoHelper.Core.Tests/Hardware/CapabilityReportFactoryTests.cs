using RevoHelper.Core.Hardware;

namespace RevoHelper.Core.Tests.Hardware;

public sealed class CapabilityReportFactoryTests
{
    [Fact]
    public void Create_SupportedMachine_StillReportsReadOnlyMode()
    {
        var identity = new MachineIdentity(
            "MECHREVO",
            "YAOSHI Series",
            "YAOSHI Series-X6AR55xY",
            "Intel(R) Core(TM) Ultra 9 275HX",
            "N.1.18MRO24");

        var report = CapabilityReportFactory.Create(identity);

        Assert.Equal(MachineSupportStatus.Supported, report.Support.Status);
        Assert.True(report.ReadOnly);
        Assert.Empty(report.EnabledWriteCapabilities);
    }
}
