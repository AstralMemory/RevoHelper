using RevoHelper.App.Settings;
using RevoHelper.Core.Control;

namespace RevoHelper.App.Tests.Settings;

public sealed class JsonApplicationSettingsStoreTests
{
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
