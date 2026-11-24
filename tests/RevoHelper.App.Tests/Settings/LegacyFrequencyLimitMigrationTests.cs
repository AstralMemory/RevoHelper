using RevoHelper.App.Settings;
using RevoHelper.Core.Control;

namespace RevoHelper.App.Tests.Settings;

public sealed class LegacyFrequencyLimitMigrationTests
{
    [Fact]
    public void Run_UnmigratedSettings_RetiresTheLegacyCapsAndSavesThemBeforeAnythingReadsThem()
    {
        var store = new FakeSettingsStore(SettingsWithLimit("office:1", 4300));

        var settings = LegacyFrequencyLimitMigration.Run(store);

        Assert.Same(store.Current, settings);
        Assert.Equal(1, store.SaveCount);
        Assert.True(settings.FrequencySafetyMigrationCompleted);
        Assert.All(settings.PerformanceProfiles!, profile =>
        {
            Assert.False(profile.Value.MaximumProcessorFrequencyConfigured);
            Assert.Equal(0, profile.Value.MaximumProcessorFrequencyMhz);
        });
        // Everything else in the profile stays.
        Assert.Equal(60, settings.PerformanceProfiles!["office:1"].Pl1Watts);
    }

    [Fact]
    public void Run_CompletedMigration_ReturnsTheSavedSettingsUnchanged()
    {
        var saved = SettingsWithLimit("office:1", 4300) with { FrequencySafetyMigrationCompleted = true };
        var store = new FakeSettingsStore(saved);

        var settings = LegacyFrequencyLimitMigration.Run(store);

        Assert.Same(saved, settings);
        Assert.Equal(0, store.SaveCount);
        Assert.True(settings.PerformanceProfiles!["office:1"].MaximumProcessorFrequencyConfigured);
    }

    [Fact]
    public void Run_KeepsAProfileWithAPositiveEfficiencyCoreCap()
    {
        // Only V1.0.8 or later writes the E-core field, so that cap is the user's, not legacy.
        var profiles = new Dictionary<string, PerformanceProfileSettings>
        {
            ["office:1"] = new(60, 60, ProcessorBoostMode.Aggressive, 3600, true, EfficiencyCoreMaximumFrequencyMhz: 2200),
            ["balanced:1"] = new(60, 60, ProcessorBoostMode.Aggressive, 4300, true)
        };
        var store = new FakeSettingsStore(new(PerformanceProfiles: profiles));

        var settings = LegacyFrequencyLimitMigration.Run(store);

        Assert.Equal(profiles["office:1"], settings.PerformanceProfiles!["office:1"]);
        Assert.False(settings.PerformanceProfiles["balanced:1"].MaximumProcessorFrequencyConfigured);
        Assert.Equal(0, settings.PerformanceProfiles["balanced:1"].MaximumProcessorFrequencyMhz);
    }

    [Fact]
    public void Run_WhenTheSaveFails_StillHandsOverTheRetiredCopy()
    {
        var store = new FakeSettingsStore(SettingsWithLimit("office:1", 4300)) { FailSave = true };
        var failures = new List<string>();

        var settings = LegacyFrequencyLimitMigration.Run(store, failures.Add);

        Assert.True(settings.FrequencySafetyMigrationCompleted);
        Assert.False(settings.PerformanceProfiles!["office:1"].MaximumProcessorFrequencyConfigured);
        Assert.Single(failures);
        // Nothing reached the file, so the next start retires the caps again.
        Assert.False(store.Current.FrequencySafetyMigrationCompleted);
    }

    private static ApplicationSettings SettingsWithLimit(string key, int mhz) => new(
        PerformanceProfiles: new Dictionary<string, PerformanceProfileSettings>
        {
            [key] = new(60, 60, ProcessorBoostMode.Aggressive, mhz, true),
            ["balanced:1"] = new(60, 60, ProcessorBoostMode.Aggressive)
        });

    private sealed class FakeSettingsStore(ApplicationSettings initial) : IApplicationSettingsStore
    {
        public ApplicationSettings Current { get; private set; } = initial;
        public int SaveCount { get; private set; }
        public bool FailSave { get; init; }
        public ApplicationSettings Load() => Current;
        public void Save(ApplicationSettings settings)
        {
            if (FailSave) throw new IOException("disk full");
            Current = settings;
            SaveCount++;
        }
    }
}
