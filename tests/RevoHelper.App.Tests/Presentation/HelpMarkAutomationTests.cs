using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Threading;
using RevoHelper.App.Presentation;
using RevoHelper.App.Settings;
using RevoHelper.Core.Dashboard;
using RevoHelper.Core.Hardware;

namespace RevoHelper.App.Tests.Presentation;

// Snapshot 9 review: a help icon reached with Tab was a Border with no automation peer, so a screen
// reader found only the "?" inside. Each is now one item named for its row, read as "说明" / "Help",
// with the explanation of its tooltip as help text.
[Collection("Display language")]
public sealed class HelpMarkAutomationTests
{
    [Theory]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    public void EveryQuestionMark_ReadsItsRowAndItsExplanation(string language)
    {
        RunOnSta(() =>
        {
            // Presentation only: no App startup, native window, controller, settings write or hardware call.
            var vm = new MainWindowViewModel(HomeDashboardFactory.Create(
                new MachineIdentity("MECHREVO", "YAOSHI Series", "Offline help mark test", "Core Ultra 9 275HX", "test", 24, 24),
                ["Intel Graphics", "NVIDIA GeForce RTX 5090 Laptop GPU"]),
                new ApplicationSettings(UiLanguageCode: language), new MemorySettings(), new NoStartup());
            var window = new MainWindow(vm) { AllowApplicationClose = true };
            try
            {
                var viewport = Assert.IsType<Viewbox>(window.Content);
                window.Content = null;
                var host = new Border { Child = viewport, DataContext = vm };
                host.Resources.MergedDictionaries.Add(window.Resources);
                host.Measure(new Size(940, 550));
                host.Arrange(new Rect(0, 0, 940, 550));
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);

                var marks = Marks(host).ToList();
                Assert.Equal(10, marks.Count);
                foreach (var mark in marks)
                {
                    var peer = UIElementAutomationPeer.CreatePeerForElement(mark);
                    var name = peer.GetName();
                    var help = peer.GetHelpText();
                    // Named for its row: the title just before it.
                    var row = Assert.IsAssignableFrom<Panel>(LogicalTreeHelper.GetParent(mark));
                    var title = Assert.IsType<TextBlock>(row.Children[row.Children.IndexOf(mark) - 1]);
                    Assert.False(string.IsNullOrWhiteSpace(name));
                    Assert.Equal(title.Text, name);
                    Assert.False(string.IsNullOrWhiteSpace(help), $"{name} has no help text.");
                    Assert.Equal(UiTextCatalog.Translate("说明", language), peer.GetLocalizedControlType());
                    Assert.Null(peer.GetChildren());
                    Assert.True(mark.Focusable);
                    Assert.Equal(true, ToolTipService.GetShowsToolTipOnKeyboardFocus(mark));
                    if (mark.ToolTip is string tooltip) Assert.Equal(tooltip, help);
                    if (language == "en-US")
                    {
                        Assert.DoesNotMatch(Han, name);
                        Assert.DoesNotMatch(Han, help);
                    }
                }

                string Help(string name) => UIElementAutomationPeer.CreatePeerForElement(
                    Assert.IsType<HelpMark>(window.FindName(name))).GetHelpText();
                string T(string text) => UiTextCatalog.Translate(text, language);
                Assert.Equal(Assert.IsType<TextBlock>(window.FindName("RadarHelpTooltipText")).Text, Help("RadarHelp"));
                // These tooltips bind to the window's data only once they open, so the marks carry the words.
                Assert.Equal(vm.UiCopy.PowerModeFollowHint, Help("PowerModeFollowHelpIcon"));
                Assert.Equal(vm.UiCopy.AutomaticUpdateCheckDescription, Help("AutomaticUpdateCheckHelpIcon"));
                Assert.Equal($"{vm.UiCopy.ExperimentalChargeRisk}\n{vm.UiCopy.ExperimentalChargeNativeNotice}",
                    Help("ExperimentalChargeHelpIcon"));
                // Dynamic power reads every line of its tooltip that is not live status.
                Assert.Equal(string.Join("\n",
                    T("动态节能说明"), T("默认关闭 · 适用于所有性能档位"),
                    T("空闲时临时限频，忙碌或切换档位即恢复；不改原设置。"),
                    T("负载升高会恢复原限制；Windows 频率响应可能延迟 5–10 秒。")), Help("DynamicPowerHelpIcon"));
                Assert.Equal(T("只退出官方控制中心的窗口和托盘这一次；手动打开或重新登录后会恢复。后台服务和 OSD 热键保留，整机模式、风扇、灯光照常可用。"),
                    Help("OfficialConsoleExitHelpIcon"));
                Assert.Equal(T("长期退出"), UIElementAutomationPeer.CreatePeerForElement(
                    Assert.IsType<HelpMark>(window.FindName("LongTermOfficialConsoleExitHelpIcon"))).GetName());
            }
            finally
            {
                window.Close();
                vm.Experiments.Dispose();
                UiLanguageService.Current.SetLanguage("zh-CN");
            }
        });
    }

    private static readonly Regex Han = new(@"\p{IsCJKUnifiedIdeographs}");

    private static IEnumerable<HelpMark> Marks(DependencyObject root)
    {
        if (root is HelpMark mark) yield return mark;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var found in Marks(child))
                yield return found;
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
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Offline help mark check timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed class MemorySettings : IApplicationSettingsStore
    {
        public ApplicationSettings Load() => new();
        public void Save(ApplicationSettings settings) { }
    }

    private sealed class NoStartup : IStartupRegistration
    {
        public bool IsEnabled() => false;
        public void SetEnabled(bool enabled) => throw new InvalidOperationException("No startup changes in help mark tests.");
    }
}
