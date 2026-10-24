using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RevoHelper.App.Controls;
using RevoHelper.App.Presentation;
using RevoHelper.App.Settings;

namespace RevoHelper.App.Tests.Presentation;

public sealed class AppThemeTests
{
    [Theory]
    [InlineData(null, AppTheme.Light)]
    [InlineData("", AppTheme.Light)]
    [InlineData("system", AppTheme.Light)]
    [InlineData("LIGHT", AppTheme.Light)]
    [InlineData("DaRk", AppTheme.Dark)]
    public void ThemeCode_UsesKnownLightOrDarkValue(string? value, string expected) =>
        Assert.Equal(expected, AppTheme.Normalize(value));

    [Fact]
    public void PaletteSwitch_ChangesOnlyNeutralResourcesAndIsReversible()
    {
        RunOnSta(() =>
        {
            var resources = new ResourceDictionary();
            var accent = new SolidColorBrush(Color.FromRgb(22, 119, 255));
            var pastel = new SolidColorBrush(Color.FromRgb(221, 248, 232));
            resources["CpuBrush"] = accent;
            resources["OfficeCardBrush"] = pastel;

            AppTheme.Apply(resources, AppTheme.Light);
            var lightSurface = ReadColor(resources, "SurfaceBrush");
            var lightText = ReadColor(resources, "TextBrush");
            Assert.Equal(Colors.White, lightSurface);
            Assert.True(Contrast(lightText, lightSurface) >= 7);

            AppTheme.Apply(resources, AppTheme.Dark);
            Assert.NotEqual(lightSurface, ReadColor(resources, "SurfaceBrush"));
            Assert.True(Contrast(ReadColor(resources, "TextBrush"), ReadColor(resources, "SurfaceBrush")) >= 7);
            Assert.True(Contrast(ReadColor(resources, "MutedBrush"), ReadColor(resources, "RaisedBrush")) >= 4.5);
            Assert.True(Contrast(ReadColor(resources, "ChartAxisBrush"), ReadColor(resources, "ChartSurfaceBrush")) >= 4.5);
            Assert.True(Contrast(ReadColor(resources, "AccentSurfaceTextBrush"), pastel.Color) >= 7);
            Assert.Same(accent, resources["CpuBrush"]);
            Assert.Same(pastel, resources["OfficeCardBrush"]);
            Assert.Same(resources["TextBrush"], resources[SystemColors.WindowTextBrushKey]);

            AppTheme.Apply(resources, AppTheme.Light);
            Assert.Equal(lightSurface, ReadColor(resources, "SurfaceBrush"));
            Assert.Equal(lightText, ReadColor(resources, "TextBrush"));
            Assert.Same(accent, resources["CpuBrush"]);
            Assert.Same(pastel, resources["OfficeCardBrush"]);
        });
    }

    [Fact]
    public void NeutralAliases_PreserveExactLightColorsAndNeverIncludeColoredSurfaces()
    {
        RunOnSta(() =>
        {
            var resources = new ResourceDictionary();
            AppTheme.Apply(resources, AppTheme.Light);
            foreach (var pair in AppTheme.NeutralBrushKeys)
            {
                Assert.Equal((Color)ColorConverter.ConvertFromString(pair.Key), ReadColor(resources, pair.Value));
            }

            Assert.DoesNotContain("#DDF8E8", AppTheme.NeutralBrushKeys.Keys);
            Assert.DoesNotContain("#DCEEFF", AppTheme.NeutralBrushKeys.Keys);
            Assert.DoesNotContain("#EEDCFF", AppTheme.NeutralBrushKeys.Keys);
            Assert.DoesNotContain("#FFF0D8", AppTheme.NeutralBrushKeys.Keys);
            Assert.DoesNotContain("#FFFFFF00", AppTheme.NeutralBrushKeys.Keys);
        });
    }

    [Fact]
    public void FanCurveChart_UsesLiveNeutralResourcesWithoutChangingAccent()
    {
        RunOnSta(() =>
        {
            var host = new Border();
            var chart = new FanCurveChart { Width = 280, Height = 180, AccentBrush = Brushes.Orange };
            host.Child = chart;
            AppTheme.Apply(host.Resources, AppTheme.Light);
            host.Measure(new Size(280, 180));
            host.Arrange(new Rect(0, 0, 280, 180));
            Assert.Equal(Colors.White, Assert.IsType<SolidColorBrush>(chart.SurfaceBrush).Color);

            AppTheme.Apply(host.Resources, AppTheme.Dark);
            Assert.Equal(ReadColor(host.Resources, "ChartSurfaceBrush"), Assert.IsType<SolidColorBrush>(chart.SurfaceBrush).Color);
            Assert.Equal(ReadColor(host.Resources, "ChartAxisBrush"), Assert.IsType<SolidColorBrush>(chart.AxisTextBrush).Color);
            Assert.Same(Brushes.Orange, chart.AccentBrush);
            AppTheme.Apply(host.Resources, AppTheme.Light);
            Assert.Equal(Colors.White, Assert.IsType<SolidColorBrush>(chart.SurfaceBrush).Color);
        });
    }

    [Fact]
    public void ApplicationSettings_OldJsonDefaultsToLightAndDarkPreferenceRoundTrips()
    {
        Assert.Equal(AppTheme.Light, JsonSerializer.Deserialize<ApplicationSettings>("{}")!.UiThemeCode);
        var value = new ApplicationSettings(UiThemeCode: AppTheme.Dark);
        Assert.Equal(AppTheme.Dark, JsonSerializer.Deserialize<ApplicationSettings>(JsonSerializer.Serialize(value))!.UiThemeCode);
    }

    private static Color ReadColor(ResourceDictionary resources, string key) =>
        Assert.IsType<SolidColorBrush>(resources[key]).Color;

    private static double Contrast(Color first, Color second)
    {
        static double Luminance(Color color)
        {
            static double Linear(byte value)
            {
                var channel = value / 255d;
                return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
            }

            return (0.2126 * Linear(color.R)) + (0.7152 * Linear(color.G)) + (0.0722 * Linear(color.B));
        }

        var a = Luminance(first);
        var b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
