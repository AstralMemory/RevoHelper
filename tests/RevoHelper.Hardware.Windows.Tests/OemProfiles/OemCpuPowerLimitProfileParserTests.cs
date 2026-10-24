using RevoHelper.Hardware.Windows.OemProfiles;

namespace RevoHelper.Hardware.Windows.Tests.OemProfiles;

public sealed class OemCpuPowerLimitProfileParserTests
{
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
    public void Parse_InvalidOrMissingCpuData_ReturnsNull(string json)
    {
        Assert.Null(OemCpuPowerLimitProfileParser.Parse("balanced:1", json));
    }
}
