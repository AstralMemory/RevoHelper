using RevoHelper.Hardware.Windows.OemControl;

namespace RevoHelper.Hardware.Windows.Tests.OemControl;

public sealed class GcuCpuPolicyStatusParserTests
{
    [Fact]
    public void Parse_ModeOnlyPayload_PreservesModeAndProfiles()
    {
        var status = GcuCpuPolicyStatusParser.Parse("""
            {"OperatingMode":1,"OfficeProfileIndex":0,"GamingProfileIndex":0}
            """);

        Assert.NotNull(status);
        Assert.Equal(1, status.OperatingMode);
        Assert.Equal(0, status.GamingProfileIndex);
        Assert.False(status.HasCpuPowerLimitSnapshot);
    }

    [Fact]
    public void Parse_StrategyOnlyPayload_PreservesReportedFlag()
    {
        var status = GcuCpuPolicyStatusParser.Parse("""{"Extreme":1}""");

        Assert.NotNull(status);
        Assert.Equal(1, status.Extreme);
        Assert.Null(status.Silent);
        Assert.False(status.HasCpuPowerLimitSnapshot);
    }

    [Fact]
    public void Parse_InvalidPowerField_DoesNotDiscardValidModeOrClaimPowerSnapshot()
    {
        var status = GcuCpuPolicyStatusParser.Parse("""
            {"CPU_PL1":"unknown","CPU_PL2":65,"CPU_PL1Minimum":5,"CPU_PL1Maximum":210,
             "CPU_PL2Minimum":5,"CPU_PL2Maximum":210,"OperatingMode":1,"GamingProfileIndex":0}
            """);

        Assert.NotNull(status);
        Assert.Equal(1, status.OperatingMode);
        Assert.False(status.HasCpuPowerLimitSnapshot);
    }

    [Fact]
    public void Parse_MapsOptionalBatteryChargingLimit()
    {
        var status = GcuCpuPolicyStatusParser.Parse("""
            {"CPU_PL1":85,"CPU_PL2":85,"CPU_PL1Minimum":10,"CPU_PL1Maximum":174,
             "CPU_PL2Minimum":10,"CPU_PL2Maximum":174,"BatteryChargingLimit":"80"}
            """);

        Assert.Equal(80, status!.BatteryChargingLimitPercent);
    }

    [Fact]
    public void Parse_MapsOptionalFanBoostEnable()
    {
        var status = GcuCpuPolicyStatusParser.Parse("""
            {"CPU_PL1":85,"CPU_PL2":85,"CPU_PL1Minimum":10,"CPU_PL1Maximum":174,
             "CPU_PL2Minimum":10,"CPU_PL2Maximum":174,"FanBoostEnable":"1"}
            """);

        Assert.Equal(1, status!.FanBoostEnable);
    }
    [Fact]
    public void Parse_ReadsCurrentValuesAndLiveBoundsFromNumericStrings()
    {
        var status = GcuCpuPolicyStatusParser.Parse("""
            {
              "CPU_PL1":"45", "CPU_PL2":"65",
              "CPU_PL1Minimum":"5", "CPU_PL1Maximum":"210",
              "CPU_PL2Minimum":"5", "CPU_PL2Maximum":"210"
            }
            """);

        Assert.Equal(new GcuCpuPolicyStatus(45, 65, 5, 210, 5, 210), status);
        Assert.True(status!.HasCpuPowerLimitSnapshot);
    }

    [Fact]
    public void Parse_ReadsOperatingModeProfilesAndTurboStrategy()
    {
        var status = GcuCpuPolicyStatusParser.Parse("""
            {
              "CPU_PL1":"45", "CPU_PL2":"65",
              "CPU_PL1Minimum":"5", "CPU_PL1Maximum":"210",
              "CPU_PL2Minimum":"5", "CPU_PL2Maximum":"210",
              "OperatingMode":"2", "GamingProfileIndex":"0",
              "OfficeProfileIndex":"0", "TurboProfileIndex":"0",
              "CustomProfileIndex":"2", "Extreme":1, "Silent":0
            }
            """);

        Assert.NotNull(status);
        Assert.Equal(2, status.OperatingMode);
        Assert.Equal(2, status.CustomProfileIndex);
        Assert.Equal(1, status.Extreme);
        Assert.Equal(0, status.Silent);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"CPU_PL1\":\"x\",\"CPU_PL2\":\"65\"}")]
    public void Parse_MalformedOrIncompletePayload_ReturnsNull(string payload) =>
        Assert.Null(GcuCpuPolicyStatusParser.Parse(payload));
}
