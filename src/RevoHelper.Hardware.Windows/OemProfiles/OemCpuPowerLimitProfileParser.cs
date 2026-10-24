using System.Text.Json;
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
            if (!root.TryGetProperty("CPU", out var cpu) ||
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
        return element.TryGetProperty(name, out var property) && property.TryGetInt32(out value);
    }

    private static bool ReadBool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind is JsonValueKind.True;

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind is JsonValueKind.String
            ? property.GetString()
            : null;
}
