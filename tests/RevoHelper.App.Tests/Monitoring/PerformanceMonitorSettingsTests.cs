using RevoHelper.App.Settings;

namespace RevoHelper.App.Tests.Monitoring;

public sealed class PerformanceMonitorSettingsTests
{
    [Fact]
    public void Defaults_KeepMonitoringOptIn_AndScreenshotBackground()
    {
        var normalized = new PerformanceMonitorSettings().Normalize();

        Assert.False(normalized.Enabled);
        Assert.True(normalized.AlwaysOnTop);
        Assert.Equal(1, normalized.SizePreset);
        Assert.Equal(0.5, normalized.BackgroundOpacity);
        Assert.Null(normalized.Left);
        Assert.Null(normalized.Top);
        Assert.Empty(normalized.ExcludedProcesses!);
        Assert.Equal(2, normalized.SchemaVersion);
        Assert.Equal(MonitorPositionPreset.TopCenter, normalized.PositionPreset);
        Assert.False(normalized.IsPositionLocked);
        Assert.True(normalized.IsAutomatic);
        Assert.Null(normalized.SelectedProgramId);
    }

    [Fact]
    public void Load_VersionTwoWithoutSourceFieldsDefaultsToAutomatic_AndNewFieldsRoundTrip()
    {
        WithStore("""{"SchemaVersion":2,"Enabled":true,"SizePreset":3}""", store =>
        {
            var legacy = store.Load();
            Assert.True(legacy.IsAutomatic);
            Assert.Null(legacy.SelectedProgramId);
            Assert.Equal(3, legacy.SizePreset);
            store.Save(legacy with { IsAutomatic = false, SelectedProgramId = "selected-program" });

            var restored = store.Load();
            Assert.True(restored.Enabled);
            Assert.False(restored.IsAutomatic);
            Assert.Equal("selected-program", restored.SelectedProgramId);
            Assert.Equal(3, restored.SizePreset);
        });
    }

    [Theory]
    [InlineData(-100, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(4, 4)]
    [InlineData(100, 4)]
    public void SizePreset_IsLimitedToFiveValues(int input, int expected)
    {
        Assert.Equal(expected, new PerformanceMonitorSettings(SizePreset: input).Normalize().SizePreset);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    public void Load_LegacyFontPreservesThePreviousPhysicalSize(int legacy, int expected)
    {
        WithStore($$"""{"SizePreset":{{legacy}},"ExcludedProcesses":["MiniMeters.exe"]}""", store =>
        {
            var result = store.Load();
            Assert.Equal(expected, result.SizePreset);
            Assert.Equal(2, result.SchemaVersion);
            Assert.Equal(["MiniMeters.exe"], result.ExcludedProcesses!);
        });
    }

    [Fact]
    public void Load_LegacySavedPositionBecomesCustom()
    {
        WithStore("""{"Left":-1800,"Top":-100,"SizePreset":1}""", store =>
        {
            var result = store.Load();
            Assert.Equal(MonitorPositionPreset.Custom, result.PositionPreset);
            Assert.Equal(-1800, result.Left);
            Assert.Equal(-100, result.Top);
        });
    }

    [Fact]
    public void Load_MissingFileUsesNewSmallDefault_AndSaveDoesNotMigrateAgain()
    {
        WithStore(null, store =>
        {
            Assert.Equal(1, store.Load().SizePreset);
            store.Save(new(SizePreset: 4, PositionPreset: MonitorPositionPreset.BottomRight, IsPositionLocked: true));
            var result = store.Load();
            Assert.Equal(4, result.SizePreset);
            Assert.Equal(2, result.SchemaVersion);
            Assert.True(result.IsPositionLocked);
            Assert.Equal(MonitorPositionPreset.BottomRight, result.PositionPreset);
        });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{}")]
    [InlineData("{\"SchemaVersion\":2}")]
    public void Load_MissingOpacityUsesFiftyPercentDefault(string? json)
    {
        WithStore(json, store => Assert.Equal(0.5, store.Load().BackgroundOpacity));
    }

    [Theory]
    [InlineData("{\"BackgroundOpacity\":0.45}")]
    [InlineData("{\"SchemaVersion\":2,\"BackgroundOpacity\":0.45}")]
    public void Load_PreservesExistingSavedOpacityWithoutForcedMigration(string json)
    {
        WithStore(json, store =>
        {
            var settings = store.Load();
            Assert.Equal(0.45, settings.BackgroundOpacity);
            store.Save(settings);
            Assert.Equal(0.45, store.Load().BackgroundOpacity);
        });
    }

    [Fact]
    public void PositionEnum_PreservesSavedNumericValuesAndAppendsBottomCenter()
    {
        Assert.Equal(0, (int)MonitorPositionPreset.Custom);
        Assert.Equal(1, (int)MonitorPositionPreset.TopLeft);
        Assert.Equal(2, (int)MonitorPositionPreset.TopCenter);
        Assert.Equal(3, (int)MonitorPositionPreset.TopRight);
        Assert.Equal(4, (int)MonitorPositionPreset.BottomLeft);
        Assert.Equal(5, (int)MonitorPositionPreset.BottomRight);
        Assert.Equal(6, (int)MonitorPositionPreset.BottomCenter);
        WithStore(null, store =>
        {
            store.Save(new(PositionPreset: MonitorPositionPreset.BottomCenter));
            Assert.Equal(MonitorPositionPreset.BottomCenter, store.Load().PositionPreset);
        });
    }

    [Fact]
    public void Normalize_RejectsUnknownPositionPresetAndUsesCurrentSchema()
    {
        var normalized = new PerformanceMonitorSettings(SchemaVersion: 0,
            PositionPreset: (MonitorPositionPreset)999).Normalize();
        Assert.Equal(2, normalized.SchemaVersion);
        Assert.Equal(MonitorPositionPreset.TopCenter, normalized.PositionPreset);
    }

    private static void WithStore(string? json, Action<PerformanceMonitorSettingsStore> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"RevoHelper.MonitorSettings.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "monitor.json");
        try
        {
            if (json is not null) File.WriteAllText(path, json);
            check(new PerformanceMonitorSettingsStore(path));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(.3, .3)]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(double.NaN, .5)]
    [InlineData(double.PositiveInfinity, .5)]
    [InlineData(double.NegativeInfinity, .5)]
    public void BackgroundOpacity_IsFiniteAndClamped(double input, double expected)
    {
        Assert.Equal(expected, new PerformanceMonitorSettings(BackgroundOpacity: input).Normalize().BackgroundOpacity);
    }

    [Fact]
    public void Position_PreservesNegativeMultiMonitorCoordinates_ButRejectsNonFiniteValues()
    {
        var negative = new PerformanceMonitorSettings(Left: -1920, Top: -120).Normalize();
        var invalid = new PerformanceMonitorSettings(Left: double.NaN, Top: double.PositiveInfinity).Normalize();

        Assert.Equal(-1920, negative.Left);
        Assert.Equal(-120, negative.Top);
        Assert.Null(invalid.Left);
        Assert.Null(invalid.Top);
    }

    [Fact]
    public void Exclusions_AreTrimmedUniqueBoundedAndDoNotModifyInputArray()
    {
        string[] input = ["  MiniMeters.exe ", "minimeters.EXE", "", "  ", "javaw.exe", .. Enumerable.Range(0, 100).Select(index => $"app{index}.exe")];
        var original = input.ToArray();

        var exclusions = new PerformanceMonitorSettings(ExcludedProcesses: input).Normalize().ExcludedProcesses!;

        Assert.Equal(64, exclusions.Length);
        Assert.Equal("MiniMeters.exe", exclusions[0]);
        Assert.Equal("javaw.exe", exclusions[1]);
        Assert.Equal(1, exclusions.Count(name => name.Equals("MiniMeters.exe", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(original, input);
    }
}
