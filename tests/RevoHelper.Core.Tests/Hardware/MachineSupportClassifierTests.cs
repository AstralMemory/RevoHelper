using RevoHelper.Core.Hardware;

namespace RevoHelper.Core.Tests.Hardware;

public sealed class MachineSupportClassifierTests
{
    [Fact]
    public void Classify_RecognizedMechrevoYaoshi_ReturnsSupported()
    {
        var identity = new MachineIdentity(
            Manufacturer: "MECHREVO",
            ProductName: "YAOSHI Series",
            BaseboardProduct: "YAOSHI Series-X6AR55xY",
            ProcessorName: "Intel(R) Core(TM) Ultra 9 275HX",
            BiosVersion: "N.1.18MRO24");

        var result = MachineSupportClassifier.Classify(identity);

        Assert.Equal(MachineSupportStatus.Supported, result.Status);
        Assert.Equal("MECHREVO YAOSHI Series", result.DeviceKey);
    }

    [Fact]
    public void Classify_MechrevoWithUnknownSeries_ReturnsUnverified()
    {
        var identity = new MachineIdentity(
            Manufacturer: "MECHREVO",
            ProductName: "UNKNOWN Series",
            BaseboardProduct: "UNKNOWN",
            ProcessorName: "Unknown CPU",
            BiosVersion: "Unknown BIOS");

        var result = MachineSupportClassifier.Classify(identity);

        Assert.Equal(MachineSupportStatus.Unverified, result.Status);
        Assert.Contains("not yet verified", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Classify_YaoshiWithDifferentBaseboard_ReturnsUnverified()
    {
        var identity = new MachineIdentity(
            Manufacturer: "MECHREVO",
            ProductName: "YAOSHI Series",
            BaseboardProduct: "YAOSHI Series-OTHER",
            ProcessorName: "Intel CPU",
            BiosVersion: "Other BIOS");

        var result = MachineSupportClassifier.Classify(identity);

        Assert.Equal(MachineSupportStatus.Unverified, result.Status);
    }

    [Fact]
    public void Classify_NonMechrevoMachine_ReturnsUnsupported()
    {
        var identity = new MachineIdentity(
            Manufacturer: "OTHER OEM",
            ProductName: "Gaming Laptop",
            BaseboardProduct: "Board",
            ProcessorName: "CPU",
            BiosVersion: "BIOS");

        var result = MachineSupportClassifier.Classify(identity);

        Assert.Equal(MachineSupportStatus.Unsupported, result.Status);
    }
}
