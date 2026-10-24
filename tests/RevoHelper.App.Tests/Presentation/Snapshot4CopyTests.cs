using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using RevoHelper.App.Presentation;
using RevoHelper.App.Settings;
using RevoHelper.Core.Control.DynamicPower;
using RevoHelper.Core.Dashboard;
using RevoHelper.Core.Hardware;
using RevoHelper.Core.Telemetry;

namespace RevoHelper.App.Tests.Presentation;

[Collection("Display language")]
public sealed class Snapshot4CopyTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void ProfileSubtitlesAreSlightlySmallerAndQuieterWithoutChangingTitles()
    {
        var document = Load();
        var subtitle = Named(document, "ProfileDescription");
        Assert.Equal("8.2", (string?)subtitle.Attribute("FontSize"));
        Assert.Equal("0.94", (string?)subtitle.Attribute("Opacity"));
        Assert.Equal("{DynamicResource ProfileDescriptionBrush}", (string?)subtitle.Attribute("Foreground"));
        var profileStyle = document.Descendants().Single(element =>
            (string?)element.Attribute(Xaml + "Key") == "ProfileRadioStyle");
        Assert.Contains(profileStyle.Elements(), element =>
            (string?)element.Attribute("Property") == "FontSize" &&
            (string?)element.Attribute("Value") == "{presentation:UiFont 13.5}");
        Assert.Equal("双策略高性能档", LocalizedUiCopy.Chinese.TurboDescription);
    }

    [Fact]
    public void AnnouncementsKeepVersionNoticeAndLinksWithoutTheLongReleaseInformationCard()
    {
        var drawer = Named(Load(), "AnnouncementsDrawerContent");
        foreach (var binding in new[]
        {
            "Version", "UpdatedDate", "NoticeTitle", "Notice", "ProjectButton", "DownloadsButton", "FeedbackButton"
        })
            Assert.Contains(drawer.Descendants(), element =>
                element.Attributes().Any(attribute => attribute.Value == $"{{Binding Announcements.{binding}}}"));

        foreach (var removed in new[]
        {
            "ReleaseTitle", "Release", "CompatibilityTitle", "Compatibility", "UsageTitle", "Usage",
            "DevelopmentTitle", "Development", "FeedbackTitle", "Feedback"
        })
            Assert.DoesNotContain(drawer.Descendants(), element =>
                element.Attributes().Any(attribute => attribute.Value == $"{{Binding Announcements.{removed}}}"));

        Assert.Equal(2, drawer.Elements().Single().Elements().Count(element => element.Name.LocalName == "Border"));
    }

    [Fact]
    public void DynamicPowerKeepsMechanismBenefitStatusAndControlsVisibleWhileDetailsUseTooltip()
    {
        var drawer = Named(Load(), "MoreProfilesDrawerContent");
        Assert.DoesNotContain(drawer.Descendants(), element => element.Name.LocalName == "Expander");
        var mechanism = Named(drawer, "DynamicPowerMechanismText");
        Assert.Contains("最近 10 分钟至少 90% 空闲时，临时限制至 3 GHz", (string?)mechanism.Attribute("Text"));
        Assert.Contains("可同时降低 CPU 功耗和温度", (string?)Named(drawer, "DynamicPowerBenefitText").Attribute("Text"));
        foreach (var name in new[] { "DynamicPowerToggle", "DynamicPowerStatus", "DynamicRecoverButton" })
            Assert.DoesNotContain(Named(drawer, name).Ancestors(), element => element.Name.LocalName == "ToolTip");
        var tooltip = Named(drawer, "DynamicPowerHelpTooltip");
        Assert.Contains(tooltip.Descendants(), element => ((string?)element.Attribute("Text"))?.Contains("Experiments.DynamicProgress", StringComparison.Ordinal) == true);
        Assert.Contains(Named(drawer, "DynamicPowerDelayWarning").Ancestors(), element => element == tooltip);
        Assert.Contains(Named(drawer, "DynamicPowerRulesText").Ancestors(), element => element == tooltip);
    }

    [Fact]
    public void OfficialConsoleCardContainsOneCoexistenceExplanationAndRetainsBackendRequirement()
    {
        var card = Named(Load(), "OfficialConsoleConflictCard");
        Assert.DoesNotContain("OfficialConsoleInstructionText", card.ToString(), StringComparison.Ordinal);
        var explanation = Assert.Single(card.Descendants(), element =>
            ((string?)element.Attribute("Text"))?.Contains("共存", StringComparison.Ordinal) == true);
        var text = (string?)explanation.Attribute("Text");
        Assert.Contains("GCUBridge", text);
        Assert.Contains("GCUService", text);
        Assert.Contains("OSD", text);
        Assert.Contains("运行", text);
        Assert.Contains("OfficialConsoleStatusText", card.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("动态节能说明")]
    [InlineData("可同时降低 CPU 功耗和温度")]
    [InlineData("RevoHelper 可以与官方控制台共存。请保留 GCUBridge、GCUService 和 OSD 热键后台运行。")]
    public void NewHelperCopyHasEnglishTranslation(string text)
    {
        Assert.True(UiTextCatalog.HasTranslation(text));
        Assert.Equal(text, UiTextCatalog.Translate(text, "zh-CN"));
        Assert.NotEqual(text, UiTextCatalog.Translate(text, "en-US"));
    }

    [Theory]
    [InlineData("zh-CN", AppTheme.Light)]
    [InlineData("zh-CN", AppTheme.Dark)]
    [InlineData("en-US", AppTheme.Light)]
    [InlineData("en-US", AppTheme.Dark)]
    public void DynamicHelp_BothLanguagesAndThemesKeepReadableCopyAndLiveProgress(string language, string theme)
    {
        RunOnSta(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            var controller = new FakeDynamicPower();
            using var experiments = new ExperimentalControlsViewModel(controller,
                dispatch: action => dispatcher.BeginInvoke(action));
            var vm = new MainWindowViewModel(HomeDashboardFactory.Create(
                new MachineIdentity("MECHREVO", "YAOSHI Series", "Offline Snapshot 4 tooltip test", "Core Ultra 9 275HX", "test", 24, 24),
                ["Intel Graphics", "NVIDIA GeForce RTX 5090 Laptop GPU"]),
                new ApplicationSettings(UiLanguageCode: language, UiThemeCode: theme),
                new MemorySettings(), new NoStartup(), experiments: experiments);
            vm.OpenMoreProfiles();
            var window = new MainWindow(vm) { AllowApplicationClose = true };
            try
            {
                typeof(MainWindow).GetMethod("OpenDrawer", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, null);
                var viewport = Assert.IsType<Viewbox>(window.Content);
                window.Content = null;
                var host = new Border { Child = viewport, DataContext = vm };
                host.Resources.MergedDictionaries.Add(window.Resources);
                host.Measure(new Size(940, 550));
                host.Arrange(new Rect(0, 0, 940, 550));
                var help = Assert.IsType<Border>(window.FindName("DynamicPowerHelpIcon"));
                var tooltip = Assert.IsType<ToolTip>(help.ToolTip);
                // Simulate the popup owner without opening a native window or starting the app.
                tooltip.PlacementTarget = help;
                Flush();
                Assert.Same(vm, tooltip.DataContext);
                var copy = Assert.IsType<StackPanel>(tooltip.Content).Children.OfType<TextBlock>().ToArray();
                Assert.Equal(UiTextCatalog.Translate("动态节能说明", language), copy[0].Text);
                Assert.Equal(UiTextCatalog.Translate("默认关闭 · 适用于所有性能档位", language), copy[1].Text);
                Assert.Equal(UiTextCatalog.Translate("观察窗口：120 / 600 秒 · 空闲 90%", language), copy[2].Text);
                Assert.Equal(UiTextCatalog.Translate("空闲时临时限频，忙碌或切换档位即恢复；不改原设置。", language), copy[3].Text);
                Assert.Equal(UiTextCatalog.Translate("负载升高会恢复原限制；Windows 频率响应可能延迟 5–10 秒。", language), copy[4].Text);
                Assert.Equal(Color.FromRgb(0xF1, 0xF7, 0xFF), Assert.IsType<SolidColorBrush>(tooltip.Background).Color);
                Assert.Equal(Color.FromRgb(0x17, 0x20, 0x2D), Assert.IsType<SolidColorBrush>(tooltip.Foreground).Color);

                // The fake mutates only its in-memory status; no hardware or watchdog is started.
                Assert.True(experiments.PauseForManualControlAsync().GetAwaiter().GetResult());
                Flush();
                Assert.Equal(UiTextCatalog.Translate("观察窗口：180 / 600 秒 · 空闲 95%", language), copy[2].Text);
            }
            finally
            {
                window.Close();
                UiLanguageService.Current.SetLanguage("zh-CN");
            }
        });
    }

    private static void Flush() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);

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
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Offline tooltip check timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed class FakeDynamicPower : IDynamicPowerController
    {
        public DynamicPowerStatus Status { get; private set; } = new(false, "动态节能未启用。", 120, false, false, 90);
        public Task SetEnabledAsync(bool enabled, CancellationToken token) => Task.CompletedTask;
        public Task ObserveAsync(HardwareTelemetrySnapshot sample, CancellationToken token) => Task.CompletedTask;
        public Task RecoverAsync(CancellationToken token) => Task.CompletedTask;
        public Task PauseAsync(string reason, CancellationToken token) => Task.CompletedTask;
        public Task PrepareForExternalChangeAsync(string reason, CancellationToken token)
        {
            Status = Status with { IdleSeconds = 180, IdlePercent = 95 };
            return Task.CompletedTask;
        }
    }

    private sealed class MemorySettings : IApplicationSettingsStore
    {
        public ApplicationSettings Load() => new();
        public void Save(ApplicationSettings settings) { }
    }

    private sealed class NoStartup : IStartupRegistration
    {
        public bool IsEnabled() => false;
        public void SetEnabled(bool enabled) => throw new InvalidOperationException("No startup changes in tooltip tests.");
    }

    private static XElement Named(XContainer root, string name) => root.Descendants().Single(element => (string?)element.Attribute(Xaml + "Name") == name);
    private static XDocument Load()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "src", "RevoHelper.App", "MainWindow.xaml");
            if (File.Exists(path)) return XDocument.Load(path);
        }
        throw new FileNotFoundException("MainWindow.xaml");
    }
}
