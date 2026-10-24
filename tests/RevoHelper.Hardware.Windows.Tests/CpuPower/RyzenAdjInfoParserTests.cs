using RevoHelper.Hardware.Windows.CpuPower;

namespace RevoHelper.Hardware.Windows.Tests.CpuPower;

public sealed class RyzenAdjInfoParserTests
{
    [Fact]
    public void Parse_ReadsTheThreeOfficialPowerLimitRows()
    {
        const string output = """
            |        STAPM LIMIT |        35.000 | stapm-limit |
            |     PPT LIMIT FAST |        65.000 | fast-limit  |
            |     PPT LIMIT SLOW |        35.000 | slow-limit  |
            """;

        var result = RyzenAdjInfoParser.TryParsePowerLimits(output, out var limits);

        Assert.True(result);
        Assert.Equal(new RyzenAdjPowerLimits(35, 35, 65), limits);
    }

    [Fact]
    public void Parse_DoesNotDiscardFractionalWattsNeededForExactRestore()
    {
        const string output = "STAPM LIMIT 45.125\nPPT LIMIT SLOW 42.750\nPPT LIMIT FAST 65.500";
        Assert.True(RyzenAdjInfoParser.TryParsePowerLimits(output, out var limits));
        Assert.Equal(45.125m, (decimal)limits.StapmWatts);
        Assert.Equal(42.750m, (decimal)limits.SlowWatts);
        Assert.Equal(65.500m, (decimal)limits.FastWatts);
    }

    [Fact]
    public void Parse_RejectsIncompleteOutput()
    {
        Assert.False(RyzenAdjInfoParser.TryParsePowerLimits("STAPM LIMIT 35", out _));
    }
}
