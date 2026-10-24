using System.Text.Json;

namespace RevoHelper.Hardware.Windows.OemControl;

public static class GcuFanBoostCommand
{
    public static string Set(bool enabled) => JsonSerializer.Serialize(new
    {
        Action = enabled ? "FAN_BOOST_ON" : "FAN_BOOST_OFF"
    });
}
