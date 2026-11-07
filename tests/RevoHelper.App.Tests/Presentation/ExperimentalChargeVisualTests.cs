using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
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

    [Theory]
    [InlineData("zh-CN", AppTheme.Light)]
    [InlineData("zh-CN", AppTheme.Dark)]
    [InlineData("en-US", AppTheme.Light)]
    [InlineData("en-US", AppTheme.Dark)]
    public void LightingDrawer_RendersEveryZoneWithEffectChipsThatFit(string language, string theme)
    {
        RunOnSta(() =>
        {
            // Presentation only: zone states are injected, nothing is written to hardware.
            var vm = new MainWindowViewModel(HomeDashboardFactory.Create(
                new MachineIdentity("MECHREVO", "YAOSHI Series", "Offline lighting", "Core Ultra 9 275HX", "preview", 24, 24),
                ["Intel Graphics", "NVIDIA GeForce RTX 5090 Laptop GPU"]),
                new ApplicationSettings(UiLanguageCode: language, UiThemeCode: theme), new MemorySettings(), new NoStartup());
            UiLanguageService.Current.SetLanguage(language);
            RevoHelper.Core.Control.LightingZoneState State(
                RevoHelper.Core.Control.LightingZone zone, RevoHelper.Core.Control.LightingEffect effect,
                params RevoHelper.Core.Control.LightingEffect[] effects) => new(true, "preview",
                new(zone, true, effect, new(0, 176, 255), 3, 2, RevoHelper.Core.Control.LightingDirection.Left, 120), effects);
            var keyboardEffects = new[]
            {
                RevoHelper.Core.Control.LightingEffect.OemDefault, RevoHelper.Core.Control.LightingEffect.Static,
                RevoHelper.Core.Control.LightingEffect.Breathing, RevoHelper.Core.Control.LightingEffect.Wave,
                RevoHelper.Core.Control.LightingEffect.Reactive, RevoHelper.Core.Control.LightingEffect.Rainbow,
                RevoHelper.Core.Control.LightingEffect.Ripple, RevoHelper.Core.Control.LightingEffect.Raindrop,
                RevoHelper.Core.Control.LightingEffect.ColorMarquee, RevoHelper.Core.Control.LightingEffect.Fireworks,
                RevoHelper.Core.Control.LightingEffect.Aurora, RevoHelper.Core.Control.LightingEffect.Game
            };
            vm.ApplyLightingZoneState(RevoHelper.Core.Control.LightingZone.Keyboard,
                State(RevoHelper.Core.Control.LightingZone.Keyboard, RevoHelper.Core.Control.LightingEffect.Wave, keyboardEffects));
            vm.ApplyLightingZoneState(RevoHelper.Core.Control.LightingZone.CoastLightbar,
                State(RevoHelper.Core.Control.LightingZone.CoastLightbar, RevoHelper.Core.Control.LightingEffect.Breathing,
                    RevoHelper.Core.Control.LightingEffect.Static, RevoHelper.Core.Control.LightingEffect.Breathing,
                    RevoHelper.Core.Control.LightingEffect.Wave, RevoHelper.Core.Control.LightingEffect.Impact,
                    RevoHelper.Core.Control.LightingEffect.Raindrop));
            vm.ApplyLightingZoneState(RevoHelper.Core.Control.LightingZone.Logo,
                State(RevoHelper.Core.Control.LightingZone.Logo, RevoHelper.Core.Control.LightingEffect.Static,
                    RevoHelper.Core.Control.LightingEffect.Static, RevoHelper.Core.Control.LightingEffect.Breathing));
            vm.ApplyLightingZoneState(RevoHelper.Core.Control.LightingZone.LiquidCooler,
                State(RevoHelper.Core.Control.LightingZone.LiquidCooler, RevoHelper.Core.Control.LightingEffect.Static,
                    RevoHelper.Core.Control.LightingEffect.Static, RevoHelper.Core.Control.LightingEffect.Breathing,
                    RevoHelper.Core.Control.LightingEffect.Colorful, RevoHelper.Core.Control.LightingEffect.Rotate,
                    RevoHelper.Core.Control.LightingEffect.Rainbow));
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
                var directory = new DirectoryInfo(AppContext.BaseDirectory);
                while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RevoHelper.sln"))) directory = directory.Parent;
                Assert.NotNull(directory);
                var output = Path.Combine(directory.FullName, "artifacts", "lighting-release-ui-preview");
                Directory.CreateDirectory(output);
                foreach (var zone in Enum.GetValues<RevoHelper.Core.Control.LightingZone>())
                {
                    vm.SelectLightingZone(zone);
                    Layout();
                    var chips = Assert.IsType<ListBox>(window.FindName("LightingEffectChips"));
                    var stack = Assert.IsType<StackPanel>(window.FindName("LightingControlsStack"));
                    Assert.Equal(vm.LightingEffectOptions.Count, chips.Items.Count);
                    Assert.Equal(vm.SelectedLightingEffect, chips.SelectedValue);
                    foreach (var item in chips.Items)
                    {
                        var chip = Assert.IsType<ListBoxItem>(chips.ItemContainerGenerator.ContainerFromItem(item));
                        Assert.True(chip.ActualHeight >= 30, $"{zone} chip is too short: {chip.ActualHeight}");
                        Assert.True(chip.ActualWidth >= 80, $"{zone} chip is too narrow: {chip.ActualWidth}");
                    }
                    var picture = new DrawingVisual();
                    using (var context = picture.RenderOpen())
                    {
                        var area = new Rect(0, 0, stack.ActualWidth, stack.ActualHeight);
                        context.DrawRectangle((Brush)window.FindResource("SurfaceBrush"), null, area);
                        context.DrawRectangle(new VisualBrush(stack), null, area);
                    }
                    var bitmap = new RenderTargetBitmap((int)stack.ActualWidth, (int)stack.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(picture);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var file = File.Create(Path.Combine(output, $"lighting-{zone}-{language}-{theme}.png"));
                    encoder.Save(file);
                }
            }
            finally { window.Close(); vm.Experiments.Dispose(); UiLanguageService.Current.SetLanguage("zh-CN"); }
        });
    }

    [Theory]
    [InlineData("zh-CN", AppTheme.Light)]
    [InlineData("zh-CN", AppTheme.Dark)]
    [InlineData("en-US", AppTheme.Light)]
    [InlineData("en-US", AppTheme.Dark)]
    public void HomeQuickRow_EveryTitleFitsItsCard(string language, string theme)
    {
        RunOnSta(() =>
        {
            // Presentation only: no App startup, controller or hardware call.
            var vm = new MainWindowViewModel(HomeDashboardFactory.Create(
                new MachineIdentity("MECHREVO", "YAOSHI Series", "Offline home", "Core Ultra 9 275HX", "preview", 24, 24),
                ["Intel Graphics", "NVIDIA GeForce RTX 5090 Laptop GPU"]),
                new ApplicationSettings(UiLanguageCode: language, UiThemeCode: theme), new MemorySettings(), new NoStartup());
            UiLanguageService.Current.SetLanguage(language);
            var window = new MainWindow(vm) { AllowApplicationClose = true };
            try
            {
                var viewport = Assert.IsType<Viewbox>(window.Content);
                window.Content = null;
                var host = new Border { Child = viewport, DataContext = vm };
                host.Resources.MergedDictionaries.Add(window.Resources);
                TextElement.SetFontFamily(host, window.FontFamily); // The host has no window to inherit the font from.
                host.Measure(new Size(580, 550));
                host.Arrange(new Rect(0, 0, 580, 550));
                host.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);

                foreach (var name in new[] { "GpuOutput", "Lighting", "WaterCooling", "Keyboard", "BatteryHealth", "DisplayColor", "Other" })
                {
                    var card = Assert.IsType<Button>(window.FindName($"{name}QuickEntry"));
                    foreach (var part in new[] { "Title", "Subtitle" })
                    {
                        var text = Assert.IsType<TextBlock>(window.FindName($"{name}Quick{part}"));
                        Assert.True(text.ActualWidth > 0, $"{name} {part} has no width");
                        Assert.True(text.ActualWidth <= card.ActualWidth - 6, $"{name} {part} touches the card edge: {text.ActualWidth} in {card.ActualWidth}");
                    }
                }

                SaveHomePreview(window, host, $"home-{language}-{theme}.png");
                SaveHomePreview(window, Assert.IsType<Grid>(window.FindName("HomeQuickControlsPanel")), $"row-{language}-{theme}.png");
            }
            finally { window.Close(); vm.Experiments.Dispose(); UiLanguageService.Current.SetLanguage("zh-CN"); }
        });
    }

    [Theory]
    [InlineData("zh-CN", AppTheme.Light)]
    [InlineData("zh-CN", AppTheme.Dark)]
    [InlineData("en-US", AppTheme.Light)]
    [InlineData("en-US", AppTheme.Dark)]
    public void UpdateNotice_FitsTheHeaderAboutPageAndSettings(string language, string theme)
    {
        RunOnSta(() =>
        {
            // Presentation only: the saved tag drives the notice, and the failing source never touches the network.
            var vm = new MainWindowViewModel(HomeDashboardFactory.Create(
                new MachineIdentity("MECHREVO", "YAOSHI Series", "Offline update notice", "Core Ultra 9 275HX", "preview", 24, 24),
                ["Intel Graphics", "NVIDIA GeForce RTX 5090 Laptop GPU"]),
                new ApplicationSettings(UiLanguageCode: language, UiThemeCode: theme, LatestReleaseTag: "V99.0.10"), new MemorySettings(), new NoStartup());
            UiLanguageService.Current.SetLanguage(language);
            vm.OpenAnnouncements();
            var window = new MainWindow(vm) { AllowApplicationClose = true };
            try
            {
                typeof(MainWindow).GetMethod("OpenDrawer", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, null);
                var viewport = Assert.IsType<Viewbox>(window.Content);
                window.Content = null;
                var host = new Border { Child = viewport, DataContext = vm };
                host.Resources.MergedDictionaries.Add(window.Resources);
                TextElement.SetFontFamily(host, window.FontFamily);
                void Layout()
                {
                    host.Measure(new Size(940, 550));
                    host.Arrange(new Rect(0, 0, 940, 550));
                    host.UpdateLayout();
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
                }
                Layout();

                var badge = Assert.IsType<Button>(window.FindName("UpdateAvailableBadge"));
                var close = Assert.IsType<Button>(window.FindName("MainCloseButton"));
                Assert.Equal(Visibility.Visible, badge.Visibility);
                Assert.Equal(vm.UpdateBadgeText, Assert.IsType<TextBlock>(window.FindName("UpdateAvailableBadgeText")).Text);
                var manual = Assert.IsType<Button>(window.FindName("ManualUpdateCheckButton"));
                var badgeBounds = badge.TransformToAncestor(host).TransformBounds(new Rect(badge.RenderSize));
                var manualBounds = manual.TransformToAncestor(host).TransformBounds(new Rect(manual.RenderSize));
                var closeBounds = close.TransformToAncestor(host).TransformBounds(new Rect(close.RenderSize));
                Assert.True(badgeBounds.Right <= manualBounds.Left, "The manual check icon must follow the notice.");
                Assert.True(manualBounds.Right <= closeBounds.Left - 8, $"Header reaches the close button: {manualBounds.Right} / {closeBounds.Left}");

                // The longest status must leave the version name room on the About page.
                Assert.Equal(Visibility.Visible, Assert.IsType<ScrollViewer>(window.FindName("AnnouncementsDrawerContent")).Visibility);
                var version = Assert.IsType<TextBlock>(window.FindName("AnnouncementVersionText"));
                var status = Assert.IsType<TextBlock>(window.FindName("UpdateCheckStatus"));
                var button = Assert.IsType<Button>(window.FindName("CheckForUpdatesButton"));
                Assert.Equal(vm.UpdateCheckStatusText, status.Text);
                var inkWidth = new FormattedText(version.Text, System.Globalization.CultureInfo.CurrentUICulture, version.FlowDirection,
                    new Typeface(version.FontFamily, version.FontStyle, version.FontWeight, version.FontStretch),
                    version.FontSize, version.Foreground, VisualTreeHelper.GetDpi(version).PixelsPerDip).Width;
                Assert.True(inkWidth <= version.ActualWidth, $"Version name is cut off: {inkWidth} / {version.ActualWidth}");
                Assert.True(status.ActualWidth <= 130, $"Status is too wide: {status.ActualWidth}");
                Assert.True(button.ActualWidth > 0);
                SaveHomePreview(window, host, $"update-about-{language}-{theme}.png");

                vm.OpenSettings();
                typeof(MainWindow).GetMethod("OpenDrawer", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, null);
                Layout();
                var toggle = Assert.IsType<CheckBox>(window.FindName("AutomaticUpdateCheckToggle"));
                Assert.True(toggle.ActualWidth > 0); // Offline hosts never report IsVisible.
                Assert.True(toggle.IsChecked);
                SaveHomePreview(window, host, $"update-settings-{language}-{theme}.png");
            }
            finally { window.Close(); vm.Experiments.Dispose(); UiLanguageService.Current.SetLanguage("zh-CN"); }
        });
    }

    [Theory]
    [InlineData("zh-CN", AppTheme.Light)]
    [InlineData("en-US", AppTheme.Dark)]
    public void PossibleUpdateNotice_FitsTheHeader(string language, string theme)
    {
        RunOnSta(() =>
        {
            var vm = new MainWindowViewModel(HomeDashboardFactory.Create(
                new MachineIdentity("MECHREVO", "YAOSHI Series", "Offline possible update", "Core Ultra 9 275HX", "preview", 24, 24),
                ["Intel Graphics", "NVIDIA GeForce RTX 5090 Laptop GPU"]),
                UpdateNotificationTests.PossibleUpdateSettings() with { UiLanguageCode = language, UiThemeCode = theme },
                new MemorySettings(), new NoStartup(), utcNow: () => new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero));
            UiLanguageService.Current.SetLanguage(language);
            var window = new MainWindow(vm) { AllowApplicationClose = true };
            try
            {
                var viewport = Assert.IsType<Viewbox>(window.Content);
                window.Content = null;
                var host = new Border { Child = viewport, DataContext = vm };
                host.Resources.MergedDictionaries.Add(window.Resources);
                TextElement.SetFontFamily(host, window.FontFamily);
                host.Measure(new Size(580, 550));
                host.Arrange(new Rect(0, 0, 580, 550));
                host.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);

                Assert.True(vm.ShowPossibleUpdateBadge);
                Assert.Equal(Visibility.Collapsed, Assert.IsType<Button>(window.FindName("UpdateAvailableBadge")).Visibility);
                var badge = Assert.IsType<Button>(window.FindName("PossibleUpdateBadge"));
                var manual = Assert.IsType<Button>(window.FindName("ManualUpdateCheckButton"));
                var close = Assert.IsType<Button>(window.FindName("MainCloseButton"));
                Assert.Equal(Visibility.Visible, badge.Visibility);
                var manualBounds = manual.TransformToAncestor(host).TransformBounds(new Rect(manual.RenderSize));
                var closeBounds = close.TransformToAncestor(host).TransformBounds(new Rect(close.RenderSize));
                Assert.True(manualBounds.Right <= closeBounds.Left - 8, $"Header reaches the close button: {manualBounds.Right} / {closeBounds.Left}");
                SaveHomePreview(window, host, $"possible-update-{language}-{theme}.png");
            }
            finally { window.Close(); vm.Experiments.Dispose(); UiLanguageService.Current.SetLanguage("zh-CN"); }
        });
    }

    [Fact]
    public void BasicTurbo_LightsNeitherStrategyButton()
    {
        RunOnSta(() =>
        {
            // Presentation only: the readback is injected, nothing is sent to hardware.
            var vm = new MainWindowViewModel(HomeDashboardFactory.Create(
                new MachineIdentity("MECHREVO", "YAOSHI Series", "Offline turbo", "Core Ultra 9 275HX", "preview", 24, 24),
                ["Intel Graphics"]), new ApplicationSettings(), new MemorySettings(), new NoStartup());
            var window = new MainWindow(vm) { AllowApplicationClose = true };
            try
            {
                var quiet = Assert.IsType<RadioButton>(window.FindName("TurboQuietRadio"));
                var high = Assert.IsType<RadioButton>(window.FindName("TurboHighRadio"));
                var turbo = Assert.IsType<RadioButton>(window.FindName("TurboProfileRadio"));

                vm.ObservePlatformModeAsync(new RevoHelper.Core.Control.PlatformModeSelection(
                    RevoHelper.Core.Control.PlatformMode.TurboQuiet) { IsBasicTurbo = true }, default).GetAwaiter().GetResult();
                Assert.True(turbo.IsChecked);
                Assert.False(quiet.IsChecked);
                Assert.False(high.IsChecked);

                vm.ObservePlatformModeAsync(new RevoHelper.Core.Control.PlatformModeSelection(
                    RevoHelper.Core.Control.PlatformMode.TurboQuiet), default).GetAwaiter().GetResult();
                Assert.True(quiet.IsChecked);
                Assert.False(high.IsChecked);
            }
            finally { window.Close(); vm.Experiments.Dispose(); }
        });
    }

    private static void SaveHomePreview(MainWindow window, FrameworkElement element, string file)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RevoHelper.sln"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var output = Path.Combine(directory.FullName, "artifacts", "home-ui-preview");
        Directory.CreateDirectory(output);
        const double scale = 2;
        var area = new Rect(0, 0, element.ActualWidth, element.ActualHeight);
        var picture = new DrawingVisual();
        using (var context = picture.RenderOpen())
        {
            context.DrawRectangle((Brush)window.FindResource("SurfaceBrush"), null, area);
            context.DrawRectangle(new VisualBrush(element), null, area);
        }
        var bitmap = new RenderTargetBitmap((int)(area.Width * scale), (int)(area.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(picture);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(output, file));
        encoder.Save(stream);
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
