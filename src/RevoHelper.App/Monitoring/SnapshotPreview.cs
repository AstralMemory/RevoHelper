using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using RevoHelper.App.Presentation;
using RevoHelper.App.Settings;
using RevoHelper.Core.Control;
using RevoHelper.Core.Dashboard;
using RevoHelper.Core.Hardware;
using RevoHelper.Core.Fans;
using RevoHelper.Core.Monitoring;
using RevoHelper.Core.Telemetry;

namespace RevoHelper.App.Monitoring;

/// <summary>
/// Offline visual acceptance only. No runtime composition, controllers, settings
/// files, target enumeration, ETW session, HWND, or real telemetry are created.
/// The startup caller must bypass runtime initialization and exit restoration.
/// </summary>
public static class SnapshotPreview
{
    public static void Render(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("Snapshot preview requires the WPF STA thread.");
        Directory.CreateDirectory(directory);
        if (Application.Current is { } application) application.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var now = new DateTimeOffset(2026, 10, 24, 18, 23, 57, TimeSpan.FromHours(8));
        var identity = new MachineIdentity("MECHREVO", "YAOSHI Series", "Offline preview",
            "Intel(R) Core(TM) Ultra 9 275HX", "preview", 24, 24);
        var dashboard = HomeDashboardFactory.Create(identity,
            ["Intel(R) Graphics", "NVIDIA GeForce RTX 5090 Laptop GPU"]);
        var telemetry = new HardwareTelemetrySnapshot(now,
        [
            new(DeviceKind.Cpu, "Core Ultra 9 275HX", 34, 63, 23, 3292, 3292, 1995),
            new(DeviceKind.DiscreteGpu, "GeForce RTX 5090 Laptop GPU", 24, 57, 73, 1957,
                DedicatedMemoryMegabytes: 24576),
            new(DeviceKind.IntegratedGpu, "Intel Graphics", null, null, null, null)
        ]);
        var viewModel = new MainWindowViewModel(dashboard, new(), new PreviewSettings(), new PreviewStartup(),
            lightingProfileStore: new PreviewLighting(), utcNow: () => now);
        viewModel.ApplyTelemetry(telemetry);
        var main = new MainWindow(viewModel) { AllowApplicationClose = true };
        // Run only the existing visual initialization; without Show there is no
        // presentation source, user input, native hook, or running drawer timer.
        main.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        Render(main, Path.Combine(directory, "snapshot4-main.png"));

        var preferences = new PerformanceMonitorSettings(Enabled: true);
        var monitor = new PerformanceMonitorWindow();
        monitor.ApplyPreferences(preferences);
        monitor.SetReadings(MonitorLineFormatter.Format(telemetry, 72));
        Render(monitor, Path.Combine(directory, "snapshot4-monitor.png"));

        var target = new ProcessTarget(12345, now.AddHours(-1), "javaw", "private title is not displayed");
        RenderDrawer("snapshot4-monitor-drawer-top.png", false, false);
        RenderDrawer("snapshot4-monitor-drawer-bottom.png", true, false);
        RenderDrawer("snapshot4-monitor-drawer-manual.png", true, true);

        foreach (var language in new[] { "zh-CN", "en-US" })
        foreach (var theme in new[] { "light", "dark" })
        foreach (var page in new[] { HomeDrawer.None, HomeDrawer.PerformanceMonitor,
                     HomeDrawer.MoreProfiles, HomeDrawer.Settings, HomeDrawer.Announcements, HomeDrawer.Performance })
        {
            var model = new MainWindowViewModel(dashboard, new(UiLanguageCode: language, UiThemeCode: theme),
                new PreviewSettings(new(UiLanguageCode: language, UiThemeCode: theme)), new PreviewStartup(),
                lightingProfileStore: new PreviewLighting(), utcNow: () => now,
                customPerformanceController: page == HomeDrawer.Performance ? new PreviewCustomPerformance() : null);
            model.ApplyTelemetry(telemetry);
            if (page == HomeDrawer.Performance)
            {
                model.SelectCustomProfile(1);
                model.OpenPerformanceAsync(CancellationToken.None).GetAwaiter().GetResult();
                typeof(MainWindowViewModel).GetProperty(nameof(MainWindowViewModel.GpuPowerStatusText))!.SetValue(model,
                    "Dynamic Boost 已关闭（OEM 回读确认）；TGP 设置 95 W，保存档位 25 W。驱动当前功率上限：100 W（独立读数，不代表实时功耗）。");
            }
            if (page == HomeDrawer.Fan)
            {
                FanCurvePoint[] points = [new(0, 40, 35, 0), new(1, 50, 45, 10),
                    new(2, 60, 55, 35), new(3, 70, 65, 50), new(4, 80, 75, 75), new(5, 90, 85, 100)];
                model.ApplyFanTable(new FanCurveTable("M2T1", true, points, points));
            }
            var window = new MainWindow(model) { AllowApplicationClose = true };
            if (page != HomeDrawer.None)
            {
                // Select only a presentation state, never initialize a hardware controller.
                typeof(MainWindowViewModel).GetProperty(nameof(MainWindowViewModel.ActiveDrawer))!.SetValue(model, page);
                typeof(MainWindow).GetMethod("OpenDrawer", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
            }
            window.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            Render(window, Path.Combine(directory, $"snapshot4-{language}-{theme}-{page}.png"), page switch
            {
                HomeDrawer.Settings => () => ((ScrollViewer)window.FindName("SettingsDrawerContent")).ScrollToEnd(),
                HomeDrawer.Performance => () =>
                {
                    var scroll = (ScrollViewer)window.FindName("PerformanceDrawerContent");
                    var card = (FrameworkElement)window.FindName("CustomGpuAdvancedCard");
                    scroll.ScrollToVerticalOffset(scroll.VerticalOffset + card.TranslatePoint(new Point(), scroll).Y);
                },
                _ => null
            });
            if (page == HomeDrawer.Settings && language == "zh-CN" && theme == "light")
                RenderSettingsTopPreview(directory, language, theme, dashboard, telemetry, now);
        }

        void RenderDrawer(string name, bool bottom, bool manual)
        {
            // Each detached visual gets its own model: WPF radio groups in
            // multiple off-screen trees must not mutate a shared preview model.
            var drawerModel = new MainWindowViewModel(dashboard, new(), new PreviewSettings(), new PreviewStartup(),
                lightingProfileStore: new PreviewLighting(), utcNow: () => now);
            drawerModel.ApplyTelemetry(telemetry);
            drawerModel.Monitor.ApplyPreferences(preferences);
            if (manual)
            {
                drawerModel.Monitor.ReplacePrograms([
                    new MonitorProgramEntry("preview-1", @"C:\Games\Minecraft\javaw.exe", "javaw.exe", now.AddDays(-1), now),
                    new MonitorProgramEntry("preview-2", @"C:\Games\Example\A-long-user-added-game-name.exe", "A-long-user-added-game-name.exe", now, null)
                ]);
                drawerModel.Monitor.IsAutomatic = false;
            }
            drawerModel.Monitor.ApplyFrameRateStatus(new(target, 72, now, FrameRateStatus.Available));
            var drawer = new MainWindow(drawerModel) { AllowApplicationClose = true };
            drawer.ShowPerformanceMonitorSettings();
            drawer.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            var scroll = (ScrollViewer)drawer.FindName("PerformanceMonitorDrawerContent");
            Render(drawer, Path.Combine(directory, name), bottom ? () => scroll.ScrollToEnd() : null);
        }
    }

    private static void RenderSettingsTopPreview(string directory, string language, string theme,
        HomeDashboard dashboard, HardwareTelemetrySnapshot telemetry, DateTimeOffset now)
    {
        var model = new MainWindowViewModel(dashboard, new(UiLanguageCode: language, UiThemeCode: theme),
            new PreviewSettings(new(UiLanguageCode: language, UiThemeCode: theme)), new PreviewStartup(),
            lightingProfileStore: new PreviewLighting(), utcNow: () => now);
        model.ApplyTelemetry(telemetry);
        var window = new MainWindow(model) { AllowApplicationClose = true };
        typeof(MainWindowViewModel).GetProperty(nameof(MainWindowViewModel.ActiveDrawer))!.SetValue(model, HomeDrawer.Settings);
        typeof(MainWindow).GetMethod("OpenDrawer", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
        window.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        var scroll = (ScrollViewer)window.FindName("SettingsDrawerContent");
        Render(window, Path.Combine(directory, "snapshot4-zh-CN-light-Settings-top.png"), scroll.ScrollToHome);
    }

    /// <summary>Render only window content without ever showing the window.</summary>
    public static void Render(Window window, string path, Action? afterLayout = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (window.IsVisible)
            throw new InvalidOperationException("Offline preview must not render a live window.");
        if (window.Content is not FrameworkElement content)
            throw new InvalidOperationException("The preview window has no renderable content.");
        try
        {
            var dataContext = content.DataContext ?? window.DataContext;
            window.Content = null;
            content.DataContext = dataContext;
            var surface = new Border { Background = window.Background, Child = content, FlowDirection = window.FlowDirection };
            // Dynamic theme resources must still resolve after detaching the never-shown content.
            surface.Resources.MergedDictionaries.Add(window.Resources);
            surface.SetValue(TextElement.FontFamilyProperty, window.FontFamily);
            surface.SetValue(TextElement.FontSizeProperty, window.FontSize);
            surface.SetValue(TextElement.FontWeightProperty, window.FontWeight);
            surface.SetValue(TextElement.ForegroundProperty, window.Foreground);
            var autoWidth = window.SizeToContent is SizeToContent.Width or SizeToContent.WidthAndHeight;
            var autoHeight = window.SizeToContent is SizeToContent.Height or SizeToContent.WidthAndHeight;
            var width = !autoWidth && double.IsFinite(window.Width) && window.Width > 0 ? window.Width : double.PositiveInfinity;
            var height = !autoHeight && double.IsFinite(window.Height) && window.Height > 0 ? window.Height : double.PositiveInfinity;
            surface.Measure(new Size(width, height));
            if (!double.IsFinite(width)) width = surface.DesiredSize.Width;
            if (!double.IsFinite(height)) height = surface.DesiredSize.Height;
            var size = new Size(Math.Max(1, width), Math.Max(1, height));
            surface.Arrange(new Rect(size));
            surface.UpdateLayout();
            afterLayout?.Invoke();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            surface.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height),
                96, 96, PixelFormats.Pbgra32);
            bitmap.Render(surface);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(path);
            encoder.Save(output);
        }
        finally
        {
            if (window is MainWindow main) main.AllowApplicationClose = true;
            window.Close();
        }
    }

    private sealed class PreviewCustomPerformance : ICustomPerformanceController
    {
        public ValueTask<CustomPerformanceState?> ReadAsync(int profileIndex, CancellationToken token) =>
            ValueTask.FromResult<CustomPerformanceState?>(new(true, true, 95, 145, true, 95, false, 25, 0, 0));
        public ValueTask<GpuClockOffsetState?> ReadGpuClockOffsetsAsync(CancellationToken token) =>
            ValueTask.FromResult<GpuClockOffsetState?>(new(true, true, new(0, 0), new(-300, 250, -1000, 1500), new(95, 150, 5, 25)));
        public ValueTask<GpuPerformanceState?> ReadGpuAsync(CancellationToken token) =>
            ValueTask.FromResult<GpuPerformanceState?>(new(true, 95, false, 25, 0, 0));
        public ValueTask<ControlResult> ApplyAsync(int profileIndex, CustomPerformanceState state, CancellationToken token) =>
            throw new InvalidOperationException("No hardware writes in offline preview.");
        public ValueTask<ControlResult> ApplyGpuAsync(GpuPerformanceState state, CancellationToken token) =>
            throw new InvalidOperationException("No hardware writes in offline preview.");
    }

    private sealed class PreviewSettings(ApplicationSettings? initial = null) : IApplicationSettingsStore
    {
        public ApplicationSettings Load() => initial ?? new();
        public void Save(ApplicationSettings settings) { }
    }

    private sealed class PreviewStartup : IStartupRegistration
    {
        public bool IsEnabled() => false;
        public void SetEnabled(bool enabled) => throw new InvalidOperationException("No startup changes in offline preview.");
    }

    private sealed class PreviewLighting : ILightingProfileStore
    {
        public LightingProfile? Load() => null;
        public void Save(LightingProfile profile) => throw new InvalidOperationException("No lighting changes in offline preview.");
        public LightingProfile Import(string path) => throw new InvalidOperationException("No imports in offline preview.");
        public void Export(string path, LightingProfile profile) => throw new InvalidOperationException("No exports in offline preview.");
    }
}
