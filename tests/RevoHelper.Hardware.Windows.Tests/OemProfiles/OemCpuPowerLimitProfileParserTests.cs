using RevoHelper.Hardware.Windows.OemProfiles;

namespace RevoHelper.Hardware.Windows.Tests.OemProfiles;

public sealed class OemCpuPowerLimitProfileParserTests
{
    [Fact]
    public void Parse_IntegerStringsPreservePowerAndDoNotEnableDisabledLimits()
    {
        var profile = OemCpuPowerLimitProfileParser.Parse("office:1", """
            {"CPU":{"PL1":"25","PL2":"35","PL1_Enabled":false,"PL2_Enabled":false}}
            """);

        Assert.NotNull(profile);
        Assert.Equal(25, profile.Pl1Watts);
        Assert.Equal(35, profile.Pl2Watts);
        Assert.False(profile.Pl1Enabled);
        Assert.False(profile.Pl2Enabled);
    }

    [Fact]
    public void Parse_ReadsPowerLimitsAndEnableFlagsWithoutWritingSource()
    {
        const string json = """
            {
              "Name": "Mode4_Profile2",
              "CustomizeName": "My profile",
              "Activated": true,
              "CPU": {
                "PL1": 70,
                "PL1_Enabled": true,
                "PL2": 80,
                "PL2_Enabled": false
              }
            }
            """;

        var result = OemCpuPowerLimitProfileParser.Parse("custom:2", json);

        Assert.NotNull(result);
        Assert.Equal("custom:2", result.Key);
        Assert.Equal(70, result.Pl1Watts);
        Assert.True(result.Pl1Enabled);
        Assert.Equal(80, result.Pl2Watts);
        Assert.False(result.Pl2Enabled);
        Assert.True(result.Activated);
        Assert.Equal("My profile", result.DisplayName);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"Name\":\"x\"}")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"CPU\":null}")]
    [InlineData("{\"CPU\":[]}")]
    [InlineData("{\"CPU\":{\"PL1\":{},\"PL2\":35}}")]
    [InlineData("{\"CPU\":{\"PL1\":\"bad\",\"PL2\":35}}")]
    [InlineData("{\"CPU\":{\"PL1\":\"25.5\",\"PL2\":35}}")]
    public void Parse_InvalidOrMissingCpuData_ReturnsNull(string json)
    {
        Assert.Null(OemCpuPowerLimitProfileParser.Parse("balanced:1", json));
    }
}
