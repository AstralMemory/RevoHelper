using RevoHelper.Core.Control;

namespace RevoHelper.App.Settings;

public sealed record PerformanceProfileSettings(
    int Pl1Watts,
    int Pl2Watts,
    ProcessorBoostMode BoostMode,
    int MaximumProcessorFrequencyMhz = 0,
    bool MaximumProcessorFrequencyConfigured = false);

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
    string UiThemeCode = "light");
