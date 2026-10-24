using RevoHelper.App.Settings;
using RevoHelper.Core.Control;

namespace RevoHelper.App.Tests.Settings;

public sealed class JsonLightingProfileStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"revo-lighting-{Guid.NewGuid():N}");

    [Fact]
    public void ExportThenImport_PreservesPortableScheme()
    {
        var store = new JsonLightingProfileStore(Path.Combine(directory, "local.revolight.json"));
        var path = Path.Combine(directory, "shared.revolight.json");

        store.Export(path, LightingProfileCatalog.BuiltIns[0]);

        AssertProfileEqual(LightingProfileCatalog.BuiltIns[0], store.Import(path));
        Assert.DoesNotContain("Keyboard/Ctrl", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void SaveThenLoad_UsesValidatedLocalDraft()
    {
        var store = new JsonLightingProfileStore(Path.Combine(directory, "local.revolight.json"));

        store.Save(LightingProfileCatalog.BuiltIns[1]);

        AssertProfileEqual(LightingProfileCatalog.BuiltIns[1], store.Load()!);
    }

    [Fact]
    public void Import_RejectsUnknownSchemaVersion()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "future.revolight.json");
        File.WriteAllText(path, "{\"schemaVersion\":99,\"id\":\"x\",\"name\":\"x\",\"zones\":[]}");
        var store = new JsonLightingProfileStore(Path.Combine(directory, "local.revolight.json"));

        var exception = Assert.Throws<InvalidDataException>(() => store.Import(path));

        Assert.Contains("版本", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_LegacySleepMinutes_MigratesValueAsSecondsAndExportUsesCorrectFieldName()
    {
        Directory.CreateDirectory(directory);
        var legacyPath = Path.Combine(directory, "legacy.revolight.json");
        var exportPath = Path.Combine(directory, "migrated.revolight.json");
        File.WriteAllText(legacyPath, """
            {
              "schemaVersion": 1,
              "id": "legacy",
              "name": "旧方案",
              "zones": [{
                "zone": "Keyboard", "enabled": true, "effect": "Static",
                "color": { "r": 1, "g": 2, "b": 3 }, "brightness": 3, "speed": 2,
                "direction": "None", "sleepMinutes": 120
              }]
            }
            """);
        var store = new JsonLightingProfileStore(Path.Combine(directory, "local.revolight.json"));

        var profile = store.Import(legacyPath);
        store.Export(exportPath, profile);

        Assert.Equal(120, profile.Zones[0].SleepSeconds);
        var exported = File.ReadAllText(exportPath);
        Assert.Contains("\"sleepSeconds\": 120", exported, StringComparison.Ordinal);
        Assert.DoesNotContain("sleepMinutes", exported, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }

    private static void AssertProfileEqual(LightingProfile expected, LightingProfile actual)
    {
        Assert.Equal(expected.SchemaVersion, actual.SchemaVersion);
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.Zones, actual.Zones);
    }
}
