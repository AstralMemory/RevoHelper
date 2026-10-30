using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using RevoHelper.App.Presentation;
using RevoHelper.App.Settings;
using RevoHelper.Core.Dashboard;
using RevoHelper.Core.Hardware;

namespace RevoHelper.App.Tests.Presentation;

[Collection("Display language")]
public sealed class ExperimentalChargeVisualTests
{
    [Theory]
    [InlineData("zh-CN", AppTheme.Light)]
    [InlineData("zh-CN", AppTheme.Dark)]
    [InlineData("en-US", AppTheme.Light)]
    [InlineData("en-US", AppTheme.Dark)]
    public void OfflineCardRendersHoverOnlyNotesAndAlwaysVisibleDelayWithThreePositionSlider(string language, string theme)
    {
        RunOnSta(() =>
        {
            // Presentation only: no App startup, native window, controller, settings write or EC call.
            var vm = new MainWindowViewModel(HomeDashboardFactory.Create(
                new MachineIdentity("MECHREVO", "YAOSHI Series", "Offline charge card", "Core Ultra 9 275HX", "preview", 24, 24),
                ["Intel Graphics", "NVIDIA GeForce RTX 5090 Laptop GPU"]),
                new ApplicationSettings(UiLanguageCode: language, UiThemeCode: theme), new MemorySettings(), new NoStartup());
            typeof(MainWindowViewModel).GetProperty(nameof(MainWindowViewModel.SupportsHighPerformancePowerMode))!.SetValue(vm, true);
            typeof(MainWindowViewModel).GetProperty(nameof(MainWindowViewModel.ActiveDrawer))!.SetValue(vm, HomeDrawer.Battery);
            var window = new MainWindow(vm) { AllowApplicationClose = true };
            try
            {
                typeof(MainWindow).GetMethod("OpenDrawer", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, null);
                var viewport = Assert.IsType<Viewbox>(window.Content);
                window.Content = null;
                var host = new Border { Child = viewport, DataContext = vm };
                host.Resources.MergedDictionaries.Add(window.Resources);
                void Layout()
                {
                    host.Measure(new Size(940, 550));
                    host.Arrange(new Rect(0, 0, 940, 550));
                    host.UpdateLayout();
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
                }
                Layout();
                var scroll = Assert.IsType<ScrollViewer>(window.FindName("BatteryDrawerContent"));
                scroll.ScrollToEnd();
                Layout();
                var card = Assert.IsType<Border>(window.FindName("ExperimentalChargeCard"));
                var help = Assert.IsType<Border>(window.FindName("ExperimentalChargeHelpIcon"));
                var tooltip = Assert.IsType<ToolTip>(help.ToolTip);
                tooltip.PlacementTarget = help;
                Layout();
                var risk = Assert.IsType<StackPanel>(tooltip.Content).Children.OfType<TextBlock>().First();
                var toggle = Assert.IsType<CheckBox>(window.FindName("ExperimentalChargeToggle"));
                var slider = Assert.IsType<Slider>(window.FindName("ExperimentalChargeLimitSlider"));
                var delay = Assert.IsType<TextBlock>(window.FindName("ExperimentalChargeDelayText"));
                Assert.Equal(vm.UiCopy.ExperimentalChargeRisk, risk.Text);
                Assert.False(tooltip.IsOpen);
                Assert.True(card.ActualWidth > 250);
                Assert.Equal(38, toggle.ActualWidth);
                Assert.Equal(22, toggle.ActualHeight);
                Assert.False(toggle.IsEnabled);
                Assert.Equal(false, toggle.IsChecked);
                Assert.Equal(0, slider.Minimum);
                Assert.Equal(2, slider.Maximum);
                Assert.Equal(1, slider.Value);
                Assert.True(slider.IsSnapToTickEnabled);
                Assert.Equal(vm.UiCopy.ExperimentalChargeDelay, delay.Text);
                Assert.True(delay.ActualHeight > 0);
                Assert.Equal(Color.FromRgb(0xD9, 0x36, 0x3E), Assert.IsType<SolidColorBrush>(delay.Foreground).Color);
                Assert.Equal(Color.FromRgb(0x17, 0x20, 0x2D), Assert.IsType<SolidColorBrush>(risk.Foreground).Color);
                var powerHelp = Assert.IsType<Border>(window.FindName("PowerModeFollowHelpIcon"));
                var powerTooltip = Assert.IsType<ToolTip>(powerHelp.ToolTip);
                powerTooltip.PlacementTarget = powerHelp;
                Layout();
                Assert.Equal(vm.UiCopy.PowerModeFollowHint, Assert.IsType<TextBlock>(powerTooltip.Content).Text);
                Assert.False(powerTooltip.IsOpen);
                var powerRow = Assert.IsType<Grid>(window.FindName("HighPerformancePowerControlRow"));
                var powerToggle = powerRow.Children.OfType<CheckBox>().Single();
                var helpBounds = powerHelp.TransformToAncestor(powerRow).TransformBounds(new Rect(powerHelp.RenderSize));
                var toggleBounds = powerToggle.TransformToAncestor(powerRow).TransformBounds(new Rect(powerToggle.RenderSize));
                Assert.True(helpBounds.Right <= toggleBounds.Left, "The hover note must not cover the power-mode switch.");
                var directory = new DirectoryInfo(AppContext.BaseDirectory);
                while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RevoHelper.sln"))) directory = directory.Parent;
                Assert.NotNull(directory);
                var output = Path.Combine(directory.FullName, "artifacts", "charge-release-ui-preview");
                Directory.CreateDirectory(output);
                var bitmap = new RenderTargetBitmap(940, 550, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(host);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(output, $"charge-{language}-{theme}.png"));
                encoder.Save(file);
            }
            finally { window.Close(); vm.Experiments.Dispose(); UiLanguageService.Current.SetLanguage("zh-CN"); }
        });
    }

    [Fact]
    public void OfflineLightingKeepsProfilesButOnlyOneZonePowerToggle()
    {
        RunOnSta(() =>
        {
            var vm = new MainWindowViewModel(HomeDashboardFactory.Create(
                new MachineIdentity("MECHREVO", "YAOSHI Series", "Offline lighting", "Core Ultra 9 275HX", "preview", 24, 24),
                ["Intel Graphics", "NVIDIA GeForce RTX 5090 Laptop GPU"]), new(), new MemorySettings(), new NoStartup());
            typeof(MainWindowViewModel).GetProperty(nameof(MainWindowViewModel.ActiveDrawer))!.SetValue(vm, HomeDrawer.Lighting);
            var window = new MainWindow(vm) { AllowApplicationClose = true };
            try
            {
                typeof(MainWindow).GetMethod("OpenDrawer", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, null);
                var viewport = Assert.IsType<Viewbox>(window.Content);
                window.Content = null;
                var host = new Border { Child = viewport, DataContext = vm };
                host.Resources.MergedDictionaries.Add(window.Resources);
                void Layout()
                {
                    host.Measure(new Size(940, 550));
                    host.Arrange(new Rect(0, 0, 940, 550));
                    host.UpdateLayout();
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
                }
                Layout();
                Assert.Null(window.FindName("KeyboardLightingToggle"));
                var power = Assert.IsType<CheckBox>(window.FindName("LightingZonePowerToggle"));
                Assert.Equal(38, power.ActualWidth);
                Assert.Equal(22, power.ActualHeight);
                Assert.IsType<Button>(window.FindName("OemDefaultLightingButton"));
                Assert.IsType<StackPanel>(window.FindName("KeyboardLightingProfilesSection"));
                var scroll = Assert.IsType<ScrollViewer>(window.FindName("LightingDrawerContent"));
                scroll.ScrollToVerticalOffset(Math.Max(0, power.TranslatePoint(new Point(), scroll).Y - 140));
                Layout();
                var directory = new DirectoryInfo(AppContext.BaseDirectory);
                while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RevoHelper.sln"))) directory = directory.Parent;
                Assert.NotNull(directory);
                var output = Path.Combine(directory.FullName, "artifacts", "lighting-release-ui-preview");
                Directory.CreateDirectory(output);
                var bitmap = new RenderTargetBitmap(940, 550, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(host);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(output, "lighting-zh-CN-light.png"));
                encoder.Save(file);
            }
            finally { window.Close(); vm.Experiments.Dispose(); UiLanguageService.Current.SetLanguage("zh-CN"); }
        });
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Offline charge rendering timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed class MemorySettings : IApplicationSettingsStore
    {
        public ApplicationSettings Load() => new();
        public void Save(ApplicationSettings settings) => throw new InvalidOperationException("No settings writes in offline previews.");
    }

    private sealed class NoStartup : IStartupRegistration
    {
        public bool IsEnabled() => false;
        public void SetEnabled(bool enabled) => throw new InvalidOperationException("No startup changes in offline previews.");
    }
}
