using RevoHelper.App.Settings;
using RevoHelper.Core.Control;

namespace RevoHelper.App.Tests.Settings;

public sealed class JsonApplicationSettingsStoreTests
{
    // A power cut once left a 459-byte file of zero bytes in this folder. For settings that used
    // to mean: load defaults silently, then overwrite everything the user had chosen.
    [Theory]
    [InlineData("")]
    [InlineData("\0\0\0\0\0\0\0\0")]
    [InlineData("{ \"StartWithWindows\": tru")]
    public void AnUnreadableFile_FallsBackToTheLastGoodCopy_AndKeepsTheEvidence(string damaged)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"revohelper-tests-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "settings.json");
        var store = new JsonApplicationSettingsStore(path);
        try
        {
            var first = new ApplicationSettings(StartWithWindows: true, UiLanguageCode: "en-US", ChargeLimitResumePercent: 80);
            store.Save(first);
            store.Save(first with { WindowScalePercent = 120 });   // the first save is now the backup
            File.WriteAllText(path, damaged);

            var loaded = new JsonApplicationSettingsStore(path).Load();

            Assert.True(loaded.StartWithWindows);
            Assert.Equal("en-US", loaded.UiLanguageCode);
            Assert.Equal(80, loaded.ChargeLimitResumePercent);
            Assert.Equal(damaged, File.ReadAllText(path + ".unreadable"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void ADamagedFileNeverReplacesTheBackup()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"revohelper-tests-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "settings.json");
        var store = new JsonApplicationSettingsStore(path);
        try
        {
            store.Save(new ApplicationSettings(AlwaysOnTop: true));
            store.Save(new ApplicationSettings(AlwaysOnTop: true, WindowScalePercent: 110));
            File.WriteAllText(path, "\0\0\0");
            store.Save(new ApplicationSettings(WindowScalePercent: 90));   // must not copy the zeros over the backup

            File.WriteAllText(path, "");
            Assert.True(store.Load().AlwaysOnTop);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void SaveAndLoad_RoundTripsGeneralPreferences()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"revohelper-tests-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "settings.json");
        var store = new JsonApplicationSettingsStore(path);
        var expected = new ApplicationSettings(
            StartWithWindows: true,
            AlwaysOnTop: true,
            RememberWindowPosition: true,
            WindowLeft: 128.5,
            WindowTop: 256.25,
            AutomaticRefreshRate: true,
            PreferredAcRefreshRateHz: 300,
            AutoConnectLiquidCooling: false,
            WindowScalePercent: 120,
            UiLanguageCode: "en-US");

        try
        {
            store.Save(expected);

            Assert.Equal(expected, store.Load());
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void Load_LegacySettingsWithoutLiquidCoolingPreference_DefaultsAutoConnectOn()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"revohelper-tests-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "settings.json");

        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, "{\"StartWithWindows\":true}");

            var settings = new JsonApplicationSettingsStore(path).Load();

            Assert.True(settings.AutoConnectLiquidCooling);
            Assert.Equal(100, settings.WindowScalePercent);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void Load_WhenFileDoesNotExist_ReturnsDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}", "settings.json");

        var settings = new JsonApplicationSettingsStore(path).Load();

        Assert.Equal(new ApplicationSettings(), settings);
        Assert.Equal(100, settings.WindowScalePercent);
    }

    [Fact]
    public void SaveAndLoad_RoundTripsIndependentPerformanceProfiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"revohelper-tests-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "settings.json");
        var store = new JsonApplicationSettingsStore(path);
        var settings = new ApplicationSettings(PerformanceProfiles: new()
        {
            ["office:1"] = new(35, 55, ProcessorBoostMode.EfficientEnabled, 3100),
            ["balanced:1"] = new(45, 65, ProcessorBoostMode.Aggressive, 0)
        });

        try
        {
            store.Save(settings);
            var loaded = store.Load();

            Assert.Equal(settings.PerformanceProfiles!["office:1"], loaded.PerformanceProfiles!["office:1"]);
            Assert.Equal(settings.PerformanceProfiles["balanced:1"], loaded.PerformanceProfiles["balanced:1"]);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
