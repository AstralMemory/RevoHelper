using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using RevoHelper.Core.Control;

namespace RevoHelper.App.Settings;

public sealed class JsonLightingProfileStore(string path) : ILightingProfileStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RevoHelper", "lighting", "my-lighting.revolight.json");

    public LightingProfile? Load() => File.Exists(path) ? Import(path) : null;

    public void Save(LightingProfile profile) => WriteAtomically(path, profile);

    public LightingProfile Import(string importPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(importPath);
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(importPath));
            MigrateLegacySleepField(root);
            var profile = root?.Deserialize<LightingProfile>(Options);
            if (!LightingProfileCatalog.Validate(profile, out var reason)) throw new InvalidDataException(reason);
            return profile!;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("灯光方案不是有效的 .revolight.json 文件。", exception);
        }
    }

    private static void MigrateLegacySleepField(JsonNode? root)
    {
        if (root?["zones"] is not JsonArray zones) return;
        foreach (var zone in zones.OfType<JsonObject>())
        {
            if (zone["sleepSeconds"] is null && zone["sleepMinutes"] is { } legacyValue)
                zone["sleepSeconds"] = legacyValue.DeepClone();
            zone.Remove("sleepMinutes");
        }
    }

    public void Export(string exportPath, LightingProfile profile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exportPath);
        WriteAtomically(exportPath, profile);
    }

    private static void WriteAtomically(string targetPath, LightingProfile profile)
    {
        if (!LightingProfileCatalog.Validate(profile, out var reason)) throw new InvalidDataException(reason);
        var directory = Path.GetDirectoryName(Path.GetFullPath(targetPath))!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(profile, Options));
            File.Move(temporaryPath, targetPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
