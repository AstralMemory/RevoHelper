using System.Text.Json;
using System.Globalization;
using RevoHelper.Core.Control;

namespace RevoHelper.Hardware.Windows.OemProfiles;

public static class OemCpuPowerLimitProfileParser
{
    public static CpuPowerLimitProfile? Parse(string key, string json)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("CPU", out var cpu) || cpu.ValueKind != JsonValueKind.Object ||
                !TryReadInt(cpu, "PL1", out var pl1) ||
                !TryReadInt(cpu, "PL2", out var pl2))
            {
                return null;
            }

            var sourceName = ReadString(root, "Name") ?? key;
            var displayName = ReadString(root, "CustomizeName") ?? sourceName;
            return new(
                key,
                sourceName,
                displayName,
                pl1,
                ReadBool(cpu, "PL1_Enabled"),
                pl2,
                ReadBool(cpu, "PL2_Enabled"),
                ReadBool(root, "Activated"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryReadInt(JsonElement element, string name, out int value)
    {
        value = 0;
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var property))
            return false;
        return property.ValueKind switch
        {
            JsonValueKind.Number => property.TryGetInt32(out value),
            JsonValueKind.String => int.TryParse(property.GetString(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out value),
            _ => false
        };
    }

    private static bool ReadBool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind is JsonValueKind.True;

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind is JsonValueKind.String
            ? property.GetString()
            : null;
}
