using RevoHelper.Hardware.Windows.OemControl;

namespace RevoHelper.Hardware.Windows.Tests.OemControl;

public sealed class GcuFanBoostCommandTests
{
    [Theory]
    [InlineData(true, "{\"Action\":\"FAN_BOOST_ON\"}")]
    [InlineData(false, "{\"Action\":\"FAN_BOOST_OFF\"}")]
    public void Set_UsesOfficialFanControlAction(bool enabled, string expected) =>
        Assert.Equal(expected, GcuFanBoostCommand.Set(enabled));
}
