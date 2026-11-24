namespace RevoHelper.App.Settings;

/// <summary>
/// Settings saved before the frequency safety migration existed can hold profile caps that this
/// build cannot tell apart from user intent. They are retired once, at start-up and before the
/// view model reads the settings, and the migration is then complete. A positive E-core cap can
/// only have been written by V1.0.8 or later, so such a profile is kept as saved.
/// </summary>
/// <remarks>
/// Windows itself is not touched. Up to V1.0.7 the migration also lifted a leftover Windows cap
/// that matched a saved profile, for one session only: the exit restored it, and later sessions
/// left it alone. That write ran in the background after the start-up recovery and could land
/// after the view model had taken over, or after the exit restore. A cap such a build left in
/// Windows now stays until the user sets the frequency limit, which records and restores it like
/// any other value.
/// </remarks>
public static class LegacyFrequencyLimitMigration
{
    public static ApplicationSettings Run(
        IApplicationSettingsStore settingsStore,
        Action<string>? reportFailure = null)
    {
        var settings = settingsStore.Load();
        if (settings.FrequencySafetyMigrationCompleted) return settings;

        var migrated = RetireLegacyIntent(settings);
        try
        {
            settingsStore.Save(migrated);
        }
        catch (Exception exception)
        {
            // The view model still starts from the retired copy, and its next save keeps it.
            reportFailure?.Invoke($"{exception.GetType().Name}: {exception.Message}");
        }
        return migrated;
    }

    /// <summary>Clears the frequency caps of profiles saved before the migration.</summary>
    public static ApplicationSettings RetireLegacyIntent(ApplicationSettings settings) => settings with
    {
        PerformanceProfiles = settings.PerformanceProfiles?.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.EfficiencyCoreMaximumFrequencyMhz > 0
                ? pair.Value
                : pair.Value with
                {
                    MaximumProcessorFrequencyMhz = 0,
                    MaximumProcessorFrequencyConfigured = false
                }),
        FrequencySafetyMigrationCompleted = true
    };
}
