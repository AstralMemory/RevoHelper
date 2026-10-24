using RevoHelper.Hardware.Windows.OemControl;

namespace RevoHelper.Hardware.Windows.Tests.OemControl;

public sealed class GcuCpuPolicyCommandTests
{
    [Fact]
    public void GetStatus_MatchesOfficialFanControlPayload() =>
        Assert.Equal("{\"Action\":\"GETSTATUS\"}", GcuCpuPolicyCommand.GetStatus());

    [Theory]
    [InlineData(true, 45, "{\"Action\":\"SET_OPERATING_MODE_DETAIL\",\"PL1\":\"45\"}")]
    [InlineData(false, 65, "{\"Action\":\"SET_OPERATING_MODE_DETAIL\",\"PL2\":\"65\"}")]
    public void SetPowerLimit_MatchesOfficialStringValuedPayload(
        bool isPl1,
        int watts,
        string expected) =>
        Assert.Equal(expected, GcuCpuPolicyCommand.SetPowerLimit(isPl1, watts));
}
