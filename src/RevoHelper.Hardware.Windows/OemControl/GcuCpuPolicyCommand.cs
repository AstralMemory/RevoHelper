using System.Globalization;
using System.Text.Json;

namespace RevoHelper.Hardware.Windows.OemControl;

public static class GcuCpuPolicyCommand
{
    public static string GetStatus() => "{\"Action\":\"GETSTATUS\"}";

    public static string SetPowerLimit(bool isPl1, int watts)
    {
        var propertyName = isPl1 ? "PL1" : "PL2";
        return JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["Action"] = "SET_OPERATING_MODE_DETAIL",
            [propertyName] = watts.ToString(CultureInfo.InvariantCulture)
        });
    }
}
