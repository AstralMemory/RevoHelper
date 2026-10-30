using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using RevoHelper.App.Presentation;
using RevoHelper.App.Settings;
using RevoHelper.Core.Dashboard;
using RevoHelper.Core.Hardware;

namespace RevoHelper.App.Tests.Presentation;

[Collection("Display language")]
public sealed class Snapshot4AnnouncementTests
{
    [Fact]
    public void AnnouncementNavigation_IsOptInAndDoesNotChangeSettingsOrMonitorPreferences()
    {
        var settings = new MemorySettings();
        var vm = CreateViewModel(new ApplicationSettings(), settings);
        Assert.Equal(HomeDrawer.None, vm.ActiveDrawer);
        Assert.False(vm.IsDrawerOpen);
        Assert.False(vm.Monitor.IsEnabled);

        vm.Monitor.ApplyPreferences(new PerformanceMonitorSettings(
            Enabled: true, SizePreset: 3, BackgroundOpacity: .65,
            PositionPreset: MonitorPositionPreset.BottomCenter, IsPositionLocked: true));
        var monitor = vm.Monitor.Preferences;
        var preferencesChanged = 0;
        vm.Monitor.PreferencesChanged += _ => preferencesChanged++;
        vm.OpenSettings();
        Assert.Equal(HomeDrawer.Settings, vm.ActiveDrawer);
        vm.OpenAnnouncements();
        Assert.Equal(HomeDrawer.Announcements, vm.ActiveDrawer);
        Assert.Equal(vm.Announcements.Title, vm.DrawerTitle);
        Assert.Equal(monitor, vm.Monitor.Preferences);
        Assert.Equal(0, preferencesChanged);
        Assert.Equal(0, settings.SaveCount);
        vm.OpenSettings();
        Assert.Equal(HomeDrawer.Settings, vm.ActiveDrawer);
        Assert.Equal(monitor, vm.Monitor.Preferences);
        Assert.Equal(0, settings.SaveCount);
    }

    [Theory]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    public void BundledCopy_UsesReleaseVersionWithoutClaimingPublicationAndIncludesDisclosure(string language)
    {
        var copy = AnnouncementCopy.For(language, "V1.0.2");
        Assert.Equal("V1.0.2", copy.Version);
        Assert.Equal("2026-10-30", copy.UpdatedDate);
        Assert.Contains("MIT", copy.Release);
        Assert.Contains("GitHub Releases", copy.Release);
        Assert.Contains("V1.0.2", copy.Notice);
        Assert.DoesNotContain(language == "en-US" ? "has been publicly released" : "已正式发布", copy.Release);
        Assert.Contains("60% / 80% / 100%", copy.Notice);
        Assert.Contains(language == "en-US" ? "MECHREVO YAOSHI in tests" : "机械革命耀世实测", copy.Notice);
        Assert.Contains(language == "en-US" ? "not model" : "不按品牌或型号限制", copy.Notice);
        Assert.Contains(language == "en-US" ? "original values" : "原值检查", copy.Notice);
        Assert.DoesNotContain("MRO24", copy.Notice);
        Assert.DoesNotContain("1.25", copy.Notice);
        Assert.Contains(language == "en-US" ? "off by default" : "默认关闭", copy.Notice);
        Assert.Contains(language == "en-US" ? "remain unverified" : "尚待验证", copy.Notice);
        foreach (var risk in language == "en-US" ? new[] { "platform state", "fans", "power", "keys" } : new[] { "平台状态", "风扇", "功耗", "按键" })
            Assert.Contains(risk, copy.Notice);
        Assert.Contains(language == "en-US" ? "does not unlock GPU" : "没有 GPU 功耗解锁", copy.Release);
        Assert.Contains(language == "en-US" ? "third-party" : "第三方", copy.Release);
        Assert.Contains(language == "en-US" ? "Some models" : "部分机型", copy.Compatibility);
        Assert.Contains(language == "en-US" ? "Not every feature" : "并非所有", copy.Compatibility);
        Assert.Contains("Vibe Coding", copy.Development);
        Assert.Contains("AI", copy.Development);
        Assert.Contains("UI", copy.Development);
        Assert.Contains(language == "en-US" ? "testing" : "测试", copy.Development);
        Assert.Contains(language == "en-US" ? "never fetches" : "不会后台联网", copy.OfflineNote);
    }

    [Fact]
    public void AnnouncementLinks_OnlyAllowThreeDeveloperControlledGitHubDestinations()
    {
        Assert.Equal("https://github.com/AstralMemory/RevoHelper", AnnouncementCopy.LinkFor("project"));
        Assert.Equal("https://github.com/AstralMemory/RevoHelper/releases", AnnouncementCopy.LinkFor("downloads"));
        Assert.Equal("https://github.com/AstralMemory/RevoHelper/issues", AnnouncementCopy.LinkFor("feedback"));
        foreach (var unknown in new string?[] { null, "", "other", "PROJECT", "https://example.com", "../settings", "javascript:alert(1)" })
            Assert.Null(AnnouncementCopy.LinkFor(unknown));
    }

    [Theory]
    [InlineData("zh-CN", AppTheme.Light)]
    [InlineData("zh-CN", AppTheme.Dark)]
    [InlineData("en-US", AppTheme.Light)]
    [InlineData("en-US", AppTheme.Dark)]
    public void AnnouncementPage_RealEntryCompactCopyBackNavigationAndLongMonitorTitleFitWithoutHwnd(string language, string theme)
    {
        RunOnSta(() =>
        {
            var settings = new MemorySettings();
            var vm = CreateViewModel(new ApplicationSettings(UiLanguageCode: language, UiThemeCode: theme), settings);
            var window = new MainWindow(vm) { AllowApplicationClose = true };
            try
            {
                var entry = Assert.IsType<Button>(window.FindName("AnnouncementsEntryButton"));
                Assert.NotNull(entry.Content);
                Assert.Equal(HomeDrawer.None, vm.ActiveDrawer);
                var monitorBefore = vm.Monitor.Preferences;
                entry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var host = ArrangeOffline(window, vm, 100, drawer: true);
                var scroll = Assert.IsType<ScrollViewer>(window.FindName("AnnouncementsDrawerContent"));
                var version = Assert.IsType<TextBlock>(window.FindName("AnnouncementVersionText"));
                Assert.Equal(HomeDrawer.Announcements, vm.ActiveDrawer);
                Assert.Equal(Visibility.Visible, scroll.Visibility);
                Assert.Equal(vm.ReleaseVersionText, version.Text);
                AssertTitleFits(window, host, vm.Announcements.Title);
                Assert.Equal(0, scroll.ScrollableHeight);
                foreach (var removedField in new[]
                {
                    "ReleaseTitle", "Release", "CompatibilityTitle", "Compatibility", "UsageTitle", "Usage",
                    "DevelopmentTitle", "Development", "FeedbackTitle", "Feedback"
                })
                    // In English, the retained feedback button and deleted section title both read
                    // "Feedback". Check the removed section bindings rather than matching button ink.
                    Assert.DoesNotContain(Descendants(scroll).OfType<TextBlock>(), body =>
                        BindingOperations.GetBinding(body, TextBlock.TextProperty)?.Path?.Path == $"Announcements.{removedField}");
                foreach (var link in new[] { "project", "downloads", "feedback" })
                    Assert.Contains(Descendants(scroll).OfType<Button>(), button => Equals(button.Tag, link));

                foreach (var body in Descendants(scroll).OfType<TextBlock>().Where(text => text.TextWrapping == TextWrapping.Wrap && !string.IsNullOrWhiteSpace(text.Text)))
                {
                    Assert.True(body.ActualWidth > 150);
                    var surface = Ancestors(body).OfType<Border>().FirstOrDefault(border => border.Background is SolidColorBrush brush && brush.Color.A == 255);
                    if (surface is not null)
                        Assert.True(Contrast(ReadColor(body.Foreground), ReadColor(surface.Background)) >= 3.5,
                            $"Announcement copy needs readable contrast in {language}/{theme}: {body.Text}");
                    var bounds = body.TransformToAncestor(scroll).TransformBounds(new Rect(body.RenderSize));
                    Assert.True(bounds.Left >= -1 && bounds.Right <= scroll.ActualWidth + 1,
                        "Wrapped announcement copy must not overflow horizontally.");
                }

                var back = Assert.IsType<Button>(window.FindName("AnnouncementsBackButton"));
                back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Flush(host);
                Assert.Equal(HomeDrawer.Settings, vm.ActiveDrawer);
                Assert.Equal(Visibility.Collapsed, scroll.Visibility);
                Assert.Equal(monitorBefore, vm.Monitor.Preferences);
                Assert.Equal(0, settings.SaveCount);

                var entryTitle = Assert.IsType<TextBlock>(window.FindName("AnnouncementsEntryTitle"));
                var entryDescription = Assert.IsType<TextBlock>(window.FindName("AnnouncementsEntryDescription"));
                Assert.Equal(TextWrapping.NoWrap, entryTitle.TextWrapping);
                Assert.Equal(TextWrapping.NoWrap, entryDescription.TextWrapping);
                var titleRow = entryTitle.TransformToAncestor(entry).TransformBounds(new Rect(entryTitle.RenderSize));
                var descriptionRow = entryDescription.TransformToAncestor(entry).TransformBounds(new Rect(entryDescription.RenderSize));
                Assert.InRange(Math.Abs((titleRow.Top + titleRow.Height / 2) - (descriptionRow.Top + descriptionRow.Height / 2)), 0, .6);
                Assert.True(titleRow.Right <= descriptionRow.Left && descriptionRow.Right <= entry.ActualWidth,
                    "The announcement title and hint must share one horizontal row without overlap.");
                var hintText = new FormattedText(entryDescription.Text, CultureInfo.CurrentUICulture, entryDescription.FlowDirection,
                    new Typeface(entryDescription.FontFamily, entryDescription.FontStyle, entryDescription.FontWeight, entryDescription.FontStretch),
                    entryDescription.FontSize, entryDescription.Foreground, VisualTreeHelper.GetDpi(entryDescription).PixelsPerDip);
                Assert.True(hintText.Width <= entryDescription.ActualWidth + .5,
                    "The one-line announcement hint must fit without truncation in either language.");

                window.ShowPerformanceMonitorSettings();
                Flush(host);
                AssertTitleFits(window, host, vm.UiCopy.MonitorTitle);
                Assert.Equal(monitorBefore, vm.Monitor.Preferences);
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData("zh-CN", 70, AppTheme.Light)]
    [InlineData("zh-CN", 100, AppTheme.Light)]
    [InlineData("zh-CN", 130, AppTheme.Light)]
    [InlineData("en-US", 70, AppTheme.Light)]
    [InlineData("en-US", 100, AppTheme.Light)]
    [InlineData("en-US", 130, AppTheme.Light)]
    [InlineData("zh-CN", 70, AppTheme.Dark)]
    [InlineData("zh-CN", 100, AppTheme.Dark)]
    [InlineData("zh-CN", 130, AppTheme.Dark)]
    [InlineData("en-US", 70, AppTheme.Dark)]
    [InlineData("en-US", 100, AppTheme.Dark)]
    [InlineData("en-US", 130, AppTheme.Dark)]
    public void Home_ProfileSubtitlesAreSlightlySmallerQuieterAndDoNotIntersectTurboStrategyButtons(string language, int percent, string theme)
    {
        RunOnSta(() =>
        {
            var vm = CreateViewModel(new ApplicationSettings(UiLanguageCode: language, UiThemeCode: theme, WindowScalePercent: percent), new MemorySettings());
            var window = new MainWindow(vm) { AllowApplicationClose = true };
            try
            {
                var host = ArrangeOffline(window, vm, percent, drawer: false);
                foreach (var name in new[] { "OfficeProfileRadio", "BalancedProfileRadio", "TurboProfileRadio", "CustomProfileRadio" })
                {
                    var radio = Assert.IsType<RadioButton>(window.FindName(name));
                    radio.ApplyTemplate();
                    var subtitle = Assert.IsType<TextBlock>(radio.Template.FindName("ProfileDescription", radio));
                    Assert.Equal(8.2, subtitle.FontSize, 3);
                    Assert.Equal(.94, subtitle.Opacity, 3);
                    var background = ReadColor(radio.Background);
                    var ink = ReadColor(subtitle.Foreground);
                    var visibleInk = Color.FromRgb(
                        (byte)Math.Round(ink.R * subtitle.Opacity + background.R * (1 - subtitle.Opacity)),
                        (byte)Math.Round(ink.G * subtitle.Opacity + background.G * (1 - subtitle.Opacity)),
                        (byte)Math.Round(ink.B * subtitle.Opacity + background.B * (1 - subtitle.Opacity)));
                    Assert.True(Contrast(visibleInk, background) >= 3.25,
                        $"{name}'s secondary subtitle must remain readable in {language}/{theme}/{percent}%.");
                }

                var turbo = Assert.IsType<RadioButton>(window.FindName("TurboProfileRadio"));
                var description = Assert.IsType<TextBlock>(turbo.Template.FindName("ProfileDescription", turbo));
                var subtitleInk = TextInkBounds(description, host);
                var quiet = Assert.IsType<RadioButton>(window.FindName("TurboQuietRadio"));
                var high = Assert.IsType<RadioButton>(window.FindName("TurboHighRadio"));
                var quietBounds = quiet.TransformToAncestor(host).TransformBounds(new Rect(quiet.RenderSize));
                var highBounds = high.TransformToAncestor(host).TransformBounds(new Rect(high.RenderSize));
                Assert.False(subtitleInk.IntersectsWith(quietBounds), $"Turbo subtitle overlaps Quiet at {language}/{percent}%.");
                Assert.False(subtitleInk.IntersectsWith(highBounds), $"Turbo subtitle overlaps High at {language}/{percent}%.");
                Assert.True(quietBounds.Right <= highBounds.Left + .1, "Turbo strategy buttons must not overlap each other.");
            }
            finally { window.Close(); }
        });
    }

    private static void AssertTitleFits(MainWindow window, Border host, string expected)
    {
        var title = Assert.IsType<TextBlock>(window.FindName("DrawerTitleText"));
        var drawer = Assert.IsType<Border>(window.FindName("DrawerPanel"));
        Assert.Equal(expected, title.Text);
        Assert.Equal(TextWrapping.Wrap, title.TextWrapping);
        Assert.True(title.ActualWidth <= 310.1);
        var titleBounds = title.TransformToAncestor(host).TransformBounds(new Rect(title.RenderSize));
        var drawerBounds = drawer.TransformToAncestor(host).TransformBounds(new Rect(drawer.RenderSize));
        Assert.True(titleBounds.Left >= drawerBounds.Left && titleBounds.Right <= drawerBounds.Right - 1,
            "The full drawer title must remain inside the left information panel.");
        var measured = new FormattedText(title.Text, CultureInfo.CurrentUICulture, title.FlowDirection,
            new Typeface(title.FontFamily, title.FontStyle, title.FontWeight, title.FontStretch),
            title.FontSize, title.Foreground, VisualTreeHelper.GetDpi(title).PixelsPerDip)
        { MaxTextWidth = title.ActualWidth };
        Assert.True(measured.Height <= title.ActualHeight + 1, "The wrapped title must keep enough vertical room for all lines.");
    }

    private static Rect TextInkBounds(TextBlock text, Visual ancestor)
    {
        var ink = new FormattedText(text.Text, CultureInfo.CurrentUICulture, text.FlowDirection,
            new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch),
            text.FontSize, text.Foreground, VisualTreeHelper.GetDpi(text).PixelsPerDip);
        return text.TransformToAncestor(ancestor).TransformBounds(new Rect(0, 0,
            Math.Min(text.ActualWidth, ink.WidthIncludingTrailingWhitespace), Math.Min(text.ActualHeight, ink.Height)));
    }

    private static MainWindowViewModel CreateViewModel(ApplicationSettings settings, IApplicationSettingsStore store) =>
        new(HomeDashboardFactory.Create(
            new MachineIdentity("MECHREVO", "YAOSHI Series", "Offline Snapshot 4 announcement test", "Core Ultra 9 275HX", "test", 24, 24),
            ["Intel Graphics", "NVIDIA GeForce RTX 5090 Laptop GPU"]), settings, store, new NoStartup());

    private static Border ArrangeOffline(MainWindow window, MainWindowViewModel vm, int percent, bool drawer)
    {
        var viewport = Assert.IsType<Viewbox>(window.Content);
        window.Content = null;
        viewport.DataContext = vm;
        var host = new Border { Child = viewport, DataContext = vm };
        TextElement.SetFontFamily(host, window.FontFamily);
        TextElement.SetForeground(host, window.Foreground);
        host.Resources.MergedDictionaries.Add(window.Resources);
        var size = new Size((drawer ? 940 : 580) * percent / 100d, 550 * percent / 100d);
        host.Measure(size);
        host.Arrange(new Rect(size));
        Flush(host);
        return host;
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

    private static IEnumerable<DependencyObject> Ancestors(DependencyObject element)
    {
        for (var parent = VisualTreeHelper.GetParent(element); parent is not null; parent = VisualTreeHelper.GetParent(parent)) yield return parent;
    }

    private static Color ReadColor(Brush brush) => Assert.IsType<SolidColorBrush>(brush).Color;

    private static double Contrast(Color first, Color second)
    {
        static double Luminance(Color color)
        {
            static double Linear(byte value)
            {
                var channel = value / 255d;
                return channel <= .04045 ? channel / 12.92 : Math.Pow((channel + .055) / 1.055, 2.4);
            }
            return (.2126 * Linear(color.R)) + (.7152 * Linear(color.G)) + (.0722 * Linear(color.B));
        }
        var a = Luminance(first);
        var b = Luminance(second);
        return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
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
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Offline Snapshot 4 presentation check timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed class MemorySettings : IApplicationSettingsStore
    {
        public int SaveCount { get; private set; }
        public ApplicationSettings Load() => new();
        public void Save(ApplicationSettings settings) => SaveCount++;
    }

    private sealed class NoStartup : IStartupRegistration
    {
        public bool IsEnabled() => false;
        public void SetEnabled(bool enabled) => throw new InvalidOperationException("No startup changes in announcement tests.");
    }
}
