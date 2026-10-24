using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using RevoHelper.App.Presentation;
using RevoHelper.App.Settings;
using RevoHelper.Core.Dashboard;
using RevoHelper.Core.Hardware;

namespace RevoHelper.App.Tests.Presentation;

/// <summary>
/// Offline-only presentation checks: no HWND, startup registration or hardware controller.
/// </summary>
public sealed class Snapshot3ThemeVisualTests
{
    [Theory]
    [InlineData("zh-CN", AppTheme.Light)]
    [InlineData("zh-CN", AppTheme.Dark)]
    [InlineData("en-US", AppTheme.Light)]
    [InlineData("en-US", AppTheme.Dark)]
    public void Home_BothLanguagesAndThemesKeepReadableTextPastelProfilesAndOriginalSize(string language, string theme)
    {
        RunOnSta(() =>
        {
            var vm = CreateViewModel(new ApplicationSettings(UiLanguageCode: language, UiThemeCode: theme), new MemorySettings());
            var window = new MainWindow(vm) { AllowApplicationClose = true };
            try
            {
                var host = ArrangeOffline(window, vm, drawer: false);
                var root = Assert.IsType<Border>(window.FindName("WindowLogicalRoot"));
                var title = Assert.IsType<TextBlock>(window.FindName("HomeTitle"));
                Assert.Equal(580, root.Width);
                Assert.Equal(550, root.Height);
                Assert.Equal(580, host.ActualWidth);
                Assert.Equal(550, host.ActualHeight);
                Assert.Equal(LocalizedUiCopy.For(language).HomeTitle, title.Text);
                Assert.True(Contrast(ReadColor(title.Foreground), ReadColor(root.Background)) >= 7,
                    "The main title must stay readable against its neutral surface.");

                AssertPastelProfile(window, "OfficeProfileRadio", "#DDF8E8");
                AssertPastelProfile(window, "BalancedProfileRadio", "#DCEEFF");
                AssertPastelProfile(window, "TurboProfileRadio", "#EEDCFF");
                AssertPastelProfile(window, "CustomProfileRadio", "#FFF0D8");
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void ExistingWindow_LanguageAndThemeSwitchImmediatelyAndCanBeRestored()
    {
        RunOnSta(() =>
        {
            var settings = new MemorySettings();
            var vm = CreateViewModel(new(), settings);
            var window = new MainWindow(vm) { AllowApplicationClose = true };
            try
            {
                var host = ArrangeOffline(window, vm, drawer: false);
                var root = Assert.IsType<Border>(window.FindName("WindowLogicalRoot"));
                var title = Assert.IsType<TextBlock>(window.FindName("HomeTitle"));
                var lightSurface = ReadColor(root.Background);
                var lightForeground = ReadColor(title.Foreground);
                Assert.Equal(LocalizedUiCopy.Chinese.HomeTitle, title.Text);

                vm.SelectedUiLanguageCode = "en-US";
                vm.SelectedUiThemeCode = AppTheme.Dark;
                Flush(host);
                Assert.Equal(LocalizedUiCopy.English.HomeTitle, title.Text);
                Assert.NotEqual(lightSurface, ReadColor(root.Background));
                Assert.NotEqual(lightForeground, ReadColor(title.Foreground));
                Assert.True(Contrast(ReadColor(title.Foreground), ReadColor(root.Background)) >= 7);
                Assert.Equal("en-US", settings.Saved!.UiLanguageCode);
                Assert.Equal(AppTheme.Dark, settings.Saved.UiThemeCode);
                AssertPastelProfile(window, "OfficeProfileRadio", "#DDF8E8");

                vm.SelectedUiLanguageCode = "zh-CN";
                vm.SelectedUiThemeCode = AppTheme.Light;
                Flush(host);
                Assert.Equal(LocalizedUiCopy.Chinese.HomeTitle, title.Text);
                Assert.Equal(lightSurface, ReadColor(root.Background));
                Assert.Equal(lightForeground, ReadColor(title.Foreground));
                Assert.Equal(580, root.Width);
                Assert.Equal(550, root.Height);
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    public void MonitorDrawer_ThemeSwitchUpdatesRealCardAndInputTextWithoutResizing(string language)
    {
        RunOnSta(() =>
        {
            var vm = CreateViewModel(new ApplicationSettings(UiLanguageCode: language), new MemorySettings());
            vm.OpenPerformanceMonitor();
            var window = new MainWindow(vm) { AllowApplicationClose = true };
            try
            {
                var host = ArrangeOffline(window, vm, drawer: true);
                var root = Assert.IsType<Border>(window.FindName("WindowLogicalRoot"));
                var selector = Assert.IsType<ComboBox>(window.FindName("MonitorSizeSelector"));
                var card = Ancestors(selector).OfType<Border>().First(border => border.CornerRadius.TopLeft >= 10);
                var inputLightSurface = ReadColor(selector.Background);
                var cardLightSurface = ReadColor(card.Background);
                Assert.Equal(Colors.White, cardLightSurface);

                vm.SelectedUiThemeCode = AppTheme.Dark;
                Flush(host);
                Assert.NotEqual(inputLightSurface, ReadColor(selector.Background));
                Assert.NotEqual(cardLightSurface, ReadColor(card.Background));
                Assert.True(Contrast(ReadColor(selector.Foreground), ReadColor(selector.Background)) >= 7,
                    "The monitor font-size selector must not inherit dark text on a dark input.");
                Assert.Equal(940, root.Width);
                Assert.Equal(550, root.Height);
                Assert.Equal(940, host.ActualWidth);
                Assert.Equal(550, host.ActualHeight);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void ThemePreference_SaveFailureKeepsCurrentThemeAndSuccessfulSaveRestoresOnNextViewModel()
    {
        var failingStore = new MemorySettings { FailSave = true };
        var original = CreateViewModel(new(), failingStore);
        original.SelectedUiThemeCode = AppTheme.Dark;
        Assert.Equal(AppTheme.Light, original.SelectedUiThemeCode);

        var store = new MemorySettings();
        var first = CreateViewModel(new(), store);
        first.SelectedUiThemeCode = AppTheme.Dark;
        Assert.Equal(AppTheme.Dark, store.Saved!.UiThemeCode);
        var next = CreateViewModel(store.Saved, new MemorySettings());
        Assert.Equal(AppTheme.Dark, next.SelectedUiThemeCode);
    }

    private static MainWindowViewModel CreateViewModel(ApplicationSettings settings, IApplicationSettingsStore store) =>
        new(HomeDashboardFactory.Create(
            new MachineIdentity("MECHREVO", "YAOSHI Series", "Offline Snapshot 3 theme test", "Core Ultra 9 275HX", "test", 24, 24),
            ["Intel Graphics", "NVIDIA GeForce RTX 5090 Laptop GPU"]), settings, store, new NoStartup());

    private static Border ArrangeOffline(MainWindow window, MainWindowViewModel vm, bool drawer)
    {
        if (drawer)
            typeof(MainWindow).GetMethod("OpenDrawer", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, null);
        var viewport = Assert.IsType<Viewbox>(window.Content);
        window.Content = null;
        viewport.DataContext = vm;
        var host = new Border { Child = viewport, DataContext = vm };
        TextElement.SetFontFamily(host, window.FontFamily);
        TextElement.SetForeground(host, window.Foreground);
        // Keep real dynamic-resource lookup alive after detaching from the never-shown window.
        host.Resources.MergedDictionaries.Add(window.Resources);
        var size = new Size(drawer ? 940 : 580, 550);
        host.Measure(size);
        host.Arrange(new Rect(size));
        Flush(host);
        return host;
    }

    private static void AssertPastelProfile(MainWindow window, string name, string expectedColor)
    {
        var profile = Assert.IsType<RadioButton>(window.FindName(name));
        var background = ReadColor(profile.Background);
        Assert.Equal((Color)ColorConverter.ConvertFromString(expectedColor), background);
        Assert.True(Contrast(ReadColor(profile.Foreground), background) >= 7,
            $"{name} must retain dark copy on its unchanged pastel fill.");
        profile.ApplyTemplate();
        var templateTitle = Descendants(profile).OfType<TextBlock>().First(text => text.FontWeight == FontWeights.SemiBold);
        Assert.True(Contrast(ReadColor(templateTitle.Foreground), background) >= 7,
            $"{name}'s real template text must keep the same readable foreground.");
    }

    private static IEnumerable<DependencyObject> Ancestors(DependencyObject element)
    {
        for (var parent = VisualTreeHelper.GetParent(element); parent is not null; parent = VisualTreeHelper.GetParent(parent))
            yield return parent;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static Color ReadColor(Brush brush) => Assert.IsType<SolidColorBrush>(brush).Color;

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

    private static void Flush(FrameworkElement element)
    {
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
        element.UpdateLayout();
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
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Offline Snapshot 3 theme check timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed class MemorySettings : IApplicationSettingsStore
    {
        public bool FailSave { get; init; }
        public ApplicationSettings? Saved { get; private set; }
        public ApplicationSettings Load() => Saved ?? new();
        public void Save(ApplicationSettings settings)
        {
            if (FailSave) throw new IOException("Test settings store is read-only.");
            Saved = settings;
        }
    }

    private sealed class NoStartup : IStartupRegistration
    {
        public bool IsEnabled() => false;
        public void SetEnabled(bool enabled) => throw new InvalidOperationException("No startup changes in presentation tests.");
    }
}
