using System.Text.Json;
using RevoHelper.Core.Fans;

namespace RevoHelper.Hardware.Windows.OemControl;

public static class GcuFanResponseStatusParser
{
    public static FanResponseSettings? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!TryReadBool(root, ["FAN_FanSwitchSpeedEnabled", "FanSwitchSpeedEnabled"], out var enabled) ||
                !TryReadInt(root, ["FAN_FanSwitchSpeed", "FanSwitchSpeed"], out var sensitivity))
            {
                return null;
            }
            var independent = TryReadBool(root, ["FanControlRespective"], out var value) && value;
            return new(independent, enabled, sensitivity);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryReadBool(JsonElement root, IReadOnlyList<string> names, out bool result)
    {
        result = false;
        foreach (var name in names)
        {
            if (!root.TryGetProperty(name, out var value)) continue;
            if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                result = value.GetBoolean();
                return true;
            }
            if (value.ValueKind is JsonValueKind.String)
            {
                var text = value.GetString();
                if (text is "1")
                {
                    result = true;
                    return true;
                }
                if (text is "0")
                {
                    result = false;
                    return true;
                }
                if (bool.TryParse(text, out result)) return true;
            }
            if (value.ValueKind is JsonValueKind.Number && value.TryGetInt32(out var number))
            {
                result = number != 0;
                return true;
            }
        }
        return false;
    }

    private static bool TryReadInt(JsonElement root, IReadOnlyList<string> names, out int result)
    {
        result = default;
        foreach (var name in names)
        {
            if (!root.TryGetProperty(name, out var value)) continue;
            if (value.ValueKind is JsonValueKind.Number && value.TryGetInt32(out result)) return true;
            if (value.ValueKind is JsonValueKind.String && int.TryParse(value.GetString(), out result)) return true;
        }
        return false;
    }
}
