using RevoHelper.Hardware.Windows.Observation;

namespace RevoHelper.Hardware.Windows.Tests.Observation;

public sealed class OemMainOptionParserTests
{
    [Fact]
    public void Parse_ReturnsOperatingModeAndKnownSelections()
    {
        const string json = """
            {
              "GamingProfileIndex": 2,
              "OfficeProfileIndex": 1,
              "TurboProfileIndex": 3,
              "CustomProfileIndex": 4,
              "OperatingMode": 2,
              "FanBoostEnable": 1
            }
            """;

        var result = OemMainOptionParser.Parse(json);

        Assert.Equal(2, result.OperatingMode);
        Assert.Equal(2, result.Selections["GamingProfileIndex"]);
        Assert.Equal(1, result.Selections["OfficeProfileIndex"]);
        Assert.Equal(3, result.Selections["TurboProfileIndex"]);
        Assert.Equal(4, result.Selections["CustomProfileIndex"]);
        Assert.Equal(1, result.Selections["FanBoostEnable"]);
    }

    [Fact]
    public void Parse_InvalidJson_ReturnsEmptyResult()
    {
        var result = OemMainOptionParser.Parse("not-json");

        Assert.Null(result.OperatingMode);
        Assert.Empty(result.Selections);
    }
}
