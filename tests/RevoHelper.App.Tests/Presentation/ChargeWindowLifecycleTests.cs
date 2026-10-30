using System.ComponentModel;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using RevoHelper.App.Presentation;
using RevoHelper.App.Settings;
using RevoHelper.Core.Control.ExperimentalCharge;
using RevoHelper.Core.Dashboard;
using RevoHelper.Core.Hardware;

namespace RevoHelper.App.Tests.Presentation;

[Collection("Display language")]
public sealed class ChargeWindowLifecycleTests
{
    [Theory]
    [InlineData("tray")]
    [InlineData("minimize")]
    [InlineData("already-cancelled")]
    [InlineData("closing-event-cancelled")]
    [InlineData("exit")]
    public void OnlyAnUncancelledRealExitRequestsChargeStop(string action)
    {
        RunOnSta(() =>
        {
            // No Application startup, Show, native HWND, real backend, settings file or helper process.
            var controller = new FakeChargeController();
            using var experiments = new ExperimentalControlsViewModel(chargeController: controller);
            var vm = new MainWindowViewModel(HomeDashboardFactory.Create(
                new MachineIdentity("MECHREVO", "YAOSHI Series", "Offline lifecycle", "Core Ultra 9 275HX", "preview", 24, 24), []),
                new ApplicationSettings(), new MemorySettings(), new NoStartup(), experiments: experiments);
            var window = new MainWindow(vm) { AllowApplicationClose = action != "tray" };
            CancelEventHandler? cancelClosing = action == "closing-event-cancelled" ? (_, args) => args.Cancel = true : null;
            if (cancelClosing is not null) window.Closing += cancelClosing;
            try
            {
                Assert.False(window.IsVisible);
                Assert.Null(PresentationSource.FromVisual(window));
                if (action == "minimize")
                    window.WindowState = WindowState.Minimized;
                else
                {
                    var args = new CancelEventArgs(action == "already-cancelled");
                    typeof(MainWindow).GetMethod("OnClosing", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [args]);
                    Assert.Equal(action != "exit", args.Cancel);
                }
                Assert.False(window.IsVisible);
                Assert.Null(PresentationSource.FromVisual(window));
                Assert.Equal(action == "exit" ? 1 : 0, controller.StopRequests);
                Assert.Equal(0, controller.Stops);
                Assert.Equal(0, controller.Enables);
                Assert.True(controller.Status.Enabled);
            }
            finally
            {
                if (cancelClosing is not null) window.Closing -= cancelClosing;
                window.AllowApplicationClose = true;
                window.Close();
                UiLanguageService.Current.SetLanguage("zh-CN");
            }
        });
    }

    [Fact]
    public void ChargeHelpKeepsTrayAndMinimizationSeparateFromActualExit()
    {
        Assert.Contains("最小化或隐藏到托盘继续限充", LocalizedUiCopy.Chinese.ExperimentalChargeRisk);
        Assert.Contains("真正退出", LocalizedUiCopy.Chinese.ExperimentalChargeRisk);
        Assert.DoesNotContain("关闭窗口", LocalizedUiCopy.Chinese.ExperimentalChargeRisk);
        Assert.Contains("Minimizing or hiding to the tray keeps limiting active", LocalizedUiCopy.English.ExperimentalChargeRisk);
        Assert.DoesNotContain("Closing the window", LocalizedUiCopy.English.ExperimentalChargeRisk);
        Assert.Contains("最小化或隐藏到托盘继续限充", AnnouncementCopy.For("zh-CN", "V1.0.2").Usage);
        Assert.Contains("Minimizing or hiding to the tray keeps limiting active", AnnouncementCopy.For("en-US", "V1.0.2").Usage);
        foreach (var text in new[] { LocalizedUiCopy.Chinese.ExperimentalChargeRisk, AnnouncementCopy.For("zh-CN", "V1.0.2").Usage })
        {
            Assert.Contains("接电或唤醒经安全确认后仅同一进程", text);
            Assert.Contains("手动关闭或健康异常不自动重新启用", text);
        }
        foreach (var text in new[] { LocalizedUiCopy.English.ExperimentalChargeRisk, AnnouncementCopy.For("en-US", "V1.0.2").Usage })
        {
            Assert.Contains("safety checks in this process only", text);
            Assert.Contains("Manual off or health faults block auto-resume", text);
            Assert.DoesNotContain("after restart", text);
        }
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Offline charge lifecycle check timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed class FakeChargeController : IExperimentalChargeController
    {
        public ExperimentalChargeStatus Status { get; } = new(Enabled: true, LimitPercent: 80);
        public event EventHandler? StatusChanged { add { } remove { } }
        public int StopRequests;
        public int Stops;
        public int Enables;
        public Task EnableAsync(int percent, CancellationToken token = default) { Enables++; return Task.CompletedTask; }
        public Task StopAsync(string reason, CancellationToken token = default) { Stops++; return Task.CompletedTask; }
        public Task RecoverAsync(CancellationToken token = default) => Task.CompletedTask;
        public void RequestStop(string reason) => StopRequests++;
    }

    private sealed class MemorySettings : IApplicationSettingsStore
    {
        public ApplicationSettings Load() => new();
        public void Save(ApplicationSettings settings) { }
    }

    private sealed class NoStartup : IStartupRegistration
    {
        public bool IsEnabled() => false;
        public void SetEnabled(bool enabled) => throw new InvalidOperationException("No startup changes in lifecycle tests.");
    }
}
