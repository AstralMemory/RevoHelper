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
            if (root.ValueKind != JsonValueKind.Object) return null;

            var pl1 = ReadOptionalInt(root, "CPU_PL1");
            var pl2 = ReadOptionalInt(root, "CPU_PL2");
            var pl1Minimum = ReadOptionalInt(root, "CPU_PL1Minimum");
            var pl1Maximum = ReadOptionalInt(root, "CPU_PL1Maximum");
            var pl2Minimum = ReadOptionalInt(root, "CPU_PL2Minimum");
            var pl2Maximum = ReadOptionalInt(root, "CPU_PL2Maximum");
            var hasPowerSnapshot = pl1.HasValue && pl2.HasValue &&
                                   pl1Minimum.HasValue && pl1Maximum.HasValue &&
                                   pl2Minimum.HasValue && pl2Maximum.HasValue;
            var status = new GcuCpuPolicyStatus(
                pl1 ?? 0,
                pl2 ?? 0,
                pl1Minimum ?? 0,
                pl1Maximum ?? 0,
                pl2Minimum ?? 0,
                pl2Maximum ?? 0,
                ReadOptionalInt(root, "OperatingMode"),
                ReadOptionalInt(root, "GamingProfileIndex"),
                ReadOptionalInt(root, "OfficeProfileIndex"),
                ReadOptionalInt(root, "TurboProfileIndex"),
                ReadOptionalInt(root, "CustomProfileIndex"),
                ReadOptionalInt(root, "Extreme"),
                ReadOptionalInt(root, "Silent"),
                ReadOptionalInt(root, "BatteryChargingLimit"),
                ReadOptionalInt(root, "FanBoostEnable"))
            {
                HasCpuPowerLimitSnapshot = hasPowerSnapshot
            };
            // Mode, fan and strategy updates are independently useful. Partial
            // power data alone cannot be consumed as a complete CPU snapshot.
            return hasPowerSnapshot || status.OperatingMode.HasValue ||
                   status.GamingProfileIndex.HasValue || status.OfficeProfileIndex.HasValue ||
                   status.TurboProfileIndex.HasValue || status.CustomProfileIndex.HasValue ||
                   status.Extreme.HasValue || status.Silent.HasValue ||
                   status.BatteryChargingLimitPercent.HasValue || status.FanBoostEnable.HasValue
                ? status
                : null;
        }
        catch (Exception exception) when (exception is JsonException or FormatException or OverflowException)
        {
            return null;
        }
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
