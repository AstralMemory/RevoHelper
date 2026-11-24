using RevoHelper.Core.Control;

namespace RevoHelper.App.Settings;

public sealed record PerformanceProfileSettings(
    int Pl1Watts,
    int Pl2Watts,
    ProcessorBoostMode BoostMode,
    int MaximumProcessorFrequencyMhz = 0,
    bool MaximumProcessorFrequencyConfigured = false,
    // E-core cap on processors with P- and E-cores; MaximumProcessorFrequencyMhz is then the
    // P-core cap. 0 means the E-cores take the P-core value, as in settings saved before V1.0.8.
    int EfficiencyCoreMaximumFrequencyMhz = 0);

public sealed record ApplicationSettings(
    bool StartWithWindows = false,
    bool AlwaysOnTop = false,
    bool RememberWindowPosition = false,
    double? WindowLeft = null,
    double? WindowTop = null,
    bool AutomaticRefreshRate = false,
    int PreferredAcRefreshRateHz = 0,
    Dictionary<string, PerformanceProfileSettings>? PerformanceProfiles = null,
    bool AutoConnectLiquidCooling = true,
    int WindowScalePercent = 100,
    bool FrequencySafetyMigrationCompleted = false,
    string UiLanguageCode = "zh-CN",
    string UiThemeCode = "light",
    bool CheckForUpdatesAutomatically = true,
    DateTimeOffset? LastUpdateCheckUtc = null,
    string? LatestReleaseTag = null,
    DateTimeOffset? LastSuccessfulUpdateCheckUtc = null,
    string? VersionFirstSeen = null,
    DateTimeOffset? VersionFirstSeenUtc = null,
    string? PossibleUpdateNoticeReadVersion = null,
    // 60, 80 or 100 while the user has the charge limit switched on; 0 after they switch it off.
    int ChargeLimitResumePercent = 0,
    // Close the official control center's window and tray after RevoHelper starts; its services stay.
    bool ExitOfficialConsole = false);
