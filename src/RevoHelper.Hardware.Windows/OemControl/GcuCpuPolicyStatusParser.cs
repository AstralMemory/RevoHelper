using System.Globalization;
using System.Text.Json;

namespace RevoHelper.Hardware.Windows.OemControl;

public static class GcuCpuPolicyStatusParser
{
    public static GcuCpuPolicyStatus? Parse(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            return new GcuCpuPolicyStatus(
                ReadRequiredInt(root, "CPU_PL1"),
                ReadRequiredInt(root, "CPU_PL2"),
                ReadRequiredInt(root, "CPU_PL1Minimum"),
                ReadRequiredInt(root, "CPU_PL1Maximum"),
                ReadRequiredInt(root, "CPU_PL2Minimum"),
                ReadRequiredInt(root, "CPU_PL2Maximum"),
                ReadOptionalInt(root, "OperatingMode"),
                ReadOptionalInt(root, "GamingProfileIndex"),
                ReadOptionalInt(root, "OfficeProfileIndex"),
                ReadOptionalInt(root, "TurboProfileIndex"),
                ReadOptionalInt(root, "CustomProfileIndex"),
                ReadOptionalInt(root, "Extreme"),
                ReadOptionalInt(root, "Silent"),
                ReadOptionalInt(root, "BatteryChargingLimit"),
                ReadOptionalInt(root, "FanBoostEnable"));
        }
        catch (Exception exception) when (exception is JsonException or FormatException or OverflowException)
        {
            return null;
        }
    }

    private static int ReadRequiredInt(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value))
        {
            throw new FormatException($"Missing {propertyName}.");
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String => int.Parse(value.GetString()!, NumberStyles.Integer, CultureInfo.InvariantCulture),
            _ => throw new FormatException($"Invalid {propertyName}.")
        };
    }

    private static int? ReadOptionalInt(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var number) => number,
            _ => null
        };
    }
}
