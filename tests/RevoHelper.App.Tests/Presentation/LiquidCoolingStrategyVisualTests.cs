using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using RevoHelper.App.Presentation;
using RevoHelper.App.Settings;
using RevoHelper.Core.Control;
using RevoHelper.Core.Dashboard;
using RevoHelper.Core.Hardware;

namespace RevoHelper.App.Tests.Presentation;

[Collection("Display language")]
public sealed class LiquidCoolingStrategyVisualTests
{
    [Theory]
    [InlineData("zh-CN", AppTheme.Light)]
    [InlineData("zh-CN", AppTheme.Dark)]
    [InlineData("en-US", AppTheme.Light)]
    [InlineData("en-US", AppTheme.Dark)]
    public void OfflineWaterControlsAreFirstAndSmartModeDisablesFourOrderedManualPositions(string language, string theme)
    {
        RunOnSta(() =>
        {
            var vm = new MainWindowViewModel(HomeDashboardFactory.Create(
                new MachineIdentity("MECHREVO", "YAOSHI Series", "Offline water strategy", "Core Ultra 9 275HX", "preview", 24, 24),
                ["Intel Graphics", "NVIDIA GeForce RTX 5090 Laptop GPU"]),
                new ApplicationSettings(UiLanguageCode: language, UiThemeCode: theme), new MemorySettings(), new NoStartup());
            typeof(MainWindowViewModel).GetProperty(nameof(MainWindowViewModel.IsLiquidCoolingConnected))!.SetValue(vm, true);
            typeof(MainWindowViewModel).GetProperty(nameof(MainWindowViewModel.ActiveDrawer))!.SetValue(vm, HomeDrawer.LiquidCooling);
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
                var strategy = Assert.IsType<Border>(window.FindName("LiquidCoolingStrategySection"));
                var content = Assert.IsType<StackPanel>(window.FindName("LiquidCoolingContentStack"));
                Assert.Same(strategy, content.Children[1]);
                var smart = Assert.IsType<CheckBox>(window.FindName("LiquidCoolingSmartToggle"));
                var manual = Assert.IsType<StackPanel>(window.FindName("LiquidCoolingManualStrategyControls"));
                var slider = Assert.IsType<Slider>(window.FindName("LiquidCoolingManualStrategySlider"));
                Assert.Equal(38, smart.ActualWidth);
                Assert.Equal(22, smart.ActualHeight);
                Assert.Equal(false, smart.IsChecked);
                Assert.True(slider.IsEnabled);
                Assert.Equal(0, slider.Minimum);
                Assert.Equal(3, slider.Maximum);
                Assert.Equal(2, slider.Value);
                Assert.True(slider.IsSnapToTickEnabled);
                var labels = manual.Children.OfType<Grid>().Single().Children.OfType<TextBlock>().ToArray();
                Assert.Equal(new[] { "更安静", "安静", "均衡", "最大" }.Select(label => UiTextCatalog.Translate(label, language)), labels.Select(label => label.Text));
                for (var index = 1; index < labels.Length; index++)
                {
                    var before = labels[index - 1].TransformToAncestor(strategy).TransformBounds(new Rect(labels[index - 1].RenderSize));
                    var after = labels[index].TransformToAncestor(strategy).TransformBounds(new Rect(labels[index].RenderSize));
                    Assert.True(before.Right <= after.Left, "The four strategy labels must not overlap.");
                }
                var directory = new DirectoryInfo(AppContext.BaseDirectory);
                while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RevoHelper.sln"))) directory = directory.Parent;
                Assert.NotNull(directory);
                var output = Path.Combine(directory.FullName, "artifacts", "liquid-cooling-release-ui-preview");
                Directory.CreateDirectory(output);
                var bitmap = new RenderTargetBitmap(940, 550, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(host);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var file = File.Create(Path.Combine(output, $"water-{language}-{theme}.png"))) encoder.Save(file);

                // In-memory status only; no controller and no device methods are invoked.
                vm.SelectedLiquidCoolingFanMode = LiquidCoolingFanMode.Auto;
                Layout();
                Assert.Equal(true, smart.IsChecked);
                Assert.False(slider.IsEnabled);
                Assert.Equal(0.45, manual.Opacity);
                Assert.Equal(2, slider.Value);
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
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Offline water rendering timed out.");
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
