using RevoHelper.Hardware.Windows.OemControl;

namespace RevoHelper.Hardware.Windows.Tests.OemControl;

public sealed class GcuFanResponseStatusParserTests
{
    [Fact]
    public void Parse_ReadsTheOfficialFanStatusFieldNames()
    {
        var result = GcuFanResponseStatusParser.Parse("""
            {"FAN_FanSwitchSpeedEnabled":"1","FAN_FanSwitchSpeed":"18"}
            """);

        Assert.NotNull(result);
        Assert.True(result.TransitionSensitivityEnabled);
        Assert.Equal(18, result.TransitionSensitivity);
    }

    [Fact]
    public void Parse_RequiresBothResponseFields()
    {
        Assert.Null(GcuFanResponseStatusParser.Parse("{\"FAN_FanSwitchSpeedEnabled\":\"1\"}"));
    }
}
