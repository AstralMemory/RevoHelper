using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using RevoHelper.App.Presentation;
using RevoHelper.App.Settings;
using RevoHelper.App.SystemControl;
using RevoHelper.Core.Control;
using RevoHelper.Core.Dashboard;
using RevoHelper.Core.Hardware;
using RevoHelper.Hardware.Windows.OemControl;

namespace RevoHelper.App.Tests.Presentation;

// Opt-in UI audit for the V1.0.5 UI work: with REVOHELPER_UI_AUDIT_DIR set, renders every drawer in
// Chinese/English and light/dark, and lists leftover Chinese text, clipped labels and low contrast.
// Without the variable it returns at once. The fake channel only reads; nothing touches hardware.
public sealed class UiAuditRenderTests
{
    [Fact]
    public void RenderEveryDrawerForAudit()
    {
        if (Environment.GetEnvironmentVariable("REVOHELPER_UI_AUDIT_DIR") is not { Length: > 0 } output) return;
        Directory.CreateDirectory(output);
        var report = new StringBuilder();
        foreach (var (language, theme) in new[] { ("zh-CN", "light"), ("en-US", "light"), ("en-US", "dark") })
            RunOnSta(() => RenderAll(language, theme, output, report));
        File.WriteAllText(Path.Combine(output, "report.txt"), report.ToString(), Encoding.UTF8);
    }

    private static void RenderAll(string language, string theme, string output, StringBuilder report)
    {
        var pages = new (string Name, string Scroll, Action<MainWindowViewModel> Open)[]
        {
            ("performance", "PerformanceDrawerContent", vm => vm.OpenPerformance()),
            ("fan", "FanDrawerContent", vm => vm.OpenFanCurve()),
            ("gpu", "GpuOutputDrawerContent", vm => Wait(vm.OpenGpuOutputAsync(default))),
            ("lighting-keyboard", "LightingDrawerContent", vm => Wait(vm.OpenLightingAsync(default))),
            ("lighting-coast", "LightingDrawerContent", vm => { Wait(vm.OpenLightingAsync(default)); vm.SelectLightingZone(LightingZone.CoastLightbar); }),
            ("lighting-water", "LightingDrawerContent", vm => { Wait(vm.OpenLightingAsync(default)); vm.SelectLightingZone(LightingZone.LiquidCooler); }),
            ("liquid", "LiquidCoolingDrawerContent", vm => Wait(vm.OpenLiquidCoolingAsync(default))),
            ("keyboard", "KeyboardDrawerContent", vm => Wait(vm.OpenKeyboardControlsAsync(default))),
            ("battery", "BatteryDrawerContent", vm => Wait(vm.OpenBatteryAsync(default))),
            ("display-color", "DisplayColorDrawerContent", vm => { Wait(vm.OpenDisplayAsync(default)); vm.SelectDisplaySettingsPage(DisplaySettingsPage.ColorCalibration); }),
            ("display-feature", "DisplayColorDrawerContent", vm => { Wait(vm.OpenDisplayAsync(default)); vm.SelectDisplaySettingsPage(DisplaySettingsPage.DisplayFeatures); }),
            ("display-refresh", "DisplayColorDrawerContent", vm => { Wait(vm.OpenDisplayAsync(default)); vm.SelectDisplaySettingsPage(DisplaySettingsPage.RefreshRate); }),
            ("other", "OtherDrawerContent", vm => Wait(vm.OpenOtherAsync(default))),
            ("settings", "SettingsDrawerContent", vm => vm.OpenSettings()),
            ("more-profiles", "MoreProfilesDrawerContent", vm => vm.OpenMoreProfiles()),
            ("monitor", "PerformanceMonitorDrawerContent", vm => vm.OpenPerformanceMonitor()),
            ("about", "AnnouncementsDrawerContent", vm => vm.OpenAnnouncements()),
        };
        foreach (var page in pages)
        {
            var vm = new MainWindowViewModel(HomeDashboardFactory.Create(
                    new MachineIdentity("MECHREVO", "YAOSHI Series", "Offline audit", "Core Ultra 9 275HX", "test", 24, 24),
                    ["Intel Graphics", "NVIDIA GeForce RTX 5090 Laptop GPU"]),
                new ApplicationSettings { UiLanguageCode = language, UiThemeCode = theme },
                new Settings(), new Startup(),
                quickControlsController: new GcuQuickControlsController(new FakeChannel(), (_, _) => Task.CompletedTask),
                verificationDelay: (_, _) => Task.CompletedTask);
            var window = new MainWindow(vm) { AllowApplicationClose = true };
            try
            {
                page.Open(vm);
                typeof(MainWindow).GetMethod("OpenDrawer", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, null);
                if (page.Name.StartsWith("lighting", StringComparison.Ordinal))
                    typeof(MainWindow).GetMethod("SynchronizeLightingZoneButtons", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, null);
                Flush();
                var viewport = Assert.IsType<Viewbox>(window.Content);
                var size = new Size(940, 550);
                viewport.Measure(size);
                viewport.Arrange(new Rect(size));
                Flush();
                viewport.UpdateLayout();
                var tag = $"{language}-{theme}-{page.Name}";
                Save(viewport, size, Path.Combine(output, tag + ".png"), null);
                var scroll = Assert.IsType<ScrollViewer>(window.FindName(page.Scroll));
                if (scroll.Content is FrameworkElement content && content.ActualHeight > 0)
                {
                    var root = Assert.IsType<Border>(window.FindName("WindowLogicalRoot"));
                    Save(content, new Size(content.ActualWidth, content.ActualHeight), Path.Combine(output, tag + "-full.png"), root.Background);
                    Inspect(content, tag, language, report);
                }
            }
            catch (Exception exception)
            {
                report.AppendLine($"[{language}-{theme}-{page.Name}] RENDER FAILED: {exception.GetType().Name}: {exception.Message}");
            }
            finally { window.Close(); }
        }
    }

    private static void Inspect(FrameworkElement content, string tag, string language, StringBuilder report)
    {
        foreach (var block in Descendants(content).OfType<TextBlock>())
        {
            if (block.ActualWidth <= 0 || block.ActualHeight <= 0 || !IsShown(block, content)) continue;
            var text = block.Text;
            if (string.IsNullOrWhiteSpace(text)) continue;
            if (language == "en-US" && text.Any(c => c is >= '一' and <= '鿿'))
                report.AppendLine($"[{tag}] CJK: {text.Replace("\n", " / ")}");
            if (block.Foreground is SolidColorBrush { Color: var fore } && Background(block) is { } back &&
                block.Opacity * fore.A / 255d > 0.6 && Contrast(fore, back) < 3)
                report.AppendLine($"[{tag}] LOW CONTRAST {Contrast(fore, back):0.0} ({fore} on {back}): {text.Replace("\n", " / ")}");
            if (block.TextWrapping == TextWrapping.NoWrap)
            {
                var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, block.FlowDirection,
                    new Typeface(block.FontFamily, block.FontStyle, block.FontWeight, block.FontStretch), block.FontSize, Brushes.Black, 1);
                if (formatted.Width > block.ActualWidth + 0.5)
                    report.AppendLine($"[{tag}] CLIPPED ({formatted.Width:0.0} > {block.ActualWidth:0.0}): {text.Replace("\n", " / ")}");
            }
            if (VisualTreeHelper.GetParent(block) is FrameworkElement parent && parent.ActualWidth > 0)
            {
                var bounds = block.TransformToAncestor(parent).TransformBounds(new Rect(block.RenderSize));
                if (bounds.Right > parent.ActualWidth + 0.5 || bounds.Left < -0.5)
                    report.AppendLine($"[{tag}] OVERFLOW parent {parent.GetType().Name} ({bounds.Right:0.0} > {parent.ActualWidth:0.0}): {text.Replace("\n", " / ")}");
            }
        }
    }

    private static Color? Background(DependencyObject element)
    {
        for (var current = VisualTreeHelper.GetParent(element); current is not null; current = VisualTreeHelper.GetParent(current))
        {
            var brush = current switch
            {
                Border border => border.Background,
                Panel panel => panel.Background,
                Control control => control.Background,
                _ => null
            };
            if (brush is SolidColorBrush { Color.A: > 200 } solid) return solid.Color;
        }
        return null;
    }

    private static double Contrast(Color a, Color b)
    {
        static double Channel(byte value) { var c = value / 255d; return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); }
        static double Luminance(Color color) => 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
        var (l1, l2) = (Luminance(a), Luminance(b));
        return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
    }

    private static bool IsShown(DependencyObject element, DependencyObject stop)
    {
        for (var current = element; current is not null && current != stop; current = VisualTreeHelper.GetParent(current))
            if (current is UIElement { Visibility: not Visibility.Visible }) return false;
        return true;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    private static void Save(Visual visual, Size size, string path, Brush? background)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(size.Width * 2), (int)Math.Ceiling(size.Height * 2), 192, 192, PixelFormats.Pbgra32);
        if (background is null) bitmap.Render(visual);
        else
        {
            var drawing = new DrawingVisual();
            using (var context = drawing.RenderOpen())
            {
                context.DrawRectangle(background, null, new Rect(size));
                context.DrawRectangle(new VisualBrush(visual) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(size));
            }
            bitmap.Render(drawing);
        }
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void Flush() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);

    private static void Wait(Task task)
    {
        var frame = new DispatcherFrame();
        task.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
        var timer = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Normal, (_, _) => frame.Continue = false, Dispatcher.CurrentDispatcher);
        timer.Start();
        if (!task.IsCompleted) Dispatcher.PushFrame(frame);
        timer.Stop();
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromMinutes(3)), "Audit render timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed class Settings : IApplicationSettingsStore
    {
        public ApplicationSettings Load() => new();
        public void Save(ApplicationSettings settings) { }
    }

    private sealed class Startup : IStartupRegistration
    {
        public bool IsEnabled() => false;
        public void SetEnabled(bool enabled) { }
    }

    private sealed class FakeChannel : IGcuQuickControlsChannel
    {
        private const string Lightbar = """{"powerStatus":"On","type":"MEZone_Lighbar4","effect":"Single","light":3,"speed":3,"direction":"0","R":0,"G":176,"B":255}""";
        public ValueTask<string> ReadSettingStatusAsync(CancellationToken token) =>
            ValueTask.FromResult("""{"WinKey":"WINKEY_UNLOCK","FnKey":"FNKEY_UNLOCK","OSD":"OSD_HIDDEN_OFF","CopilotKey":"COPILOTKEY_UNLOCK","UsbCharger":true,"AcRecoverySwitch_Support":true,"AcRecoverySwitch_Status":true,"DeepSleepSwitch":true,"DeepSleepTime":30,"CloseTimer":120}""");
        public ValueTask<string> ReadDeviceSwitchStatusAsync(CancellationToken token) =>
            ValueTask.FromResult("""{"WIFIEnable":true,"BluetoothEnable":true,"TouchPadEnable":true,"CameraEnable":true}""");
        public ValueTask<string> ReadKeyboardStatusAsync(CancellationToken token) =>
            ValueTask.FromResult("""{"powerStatus":"On","type":"PerKey","effect":"Single","light":3,"speed":2,"direction":"0","R":0,"G":180,"B":255}""");
        public ValueTask<string> ReadLightingStatusAsync(LightingZone zone, CancellationToken token) => zone switch
        {
            LightingZone.Keyboard => ReadKeyboardStatusAsync(token),
            LightingZone.LiquidCooler => ReadLiquidCoolingAsync(token),
            _ => ValueTask.FromResult(Lightbar)
        };
        public ValueTask<string> ReadBatteryProtectionAsync(CancellationToken token) =>
            ValueTask.FromResult("{\"HealthProtectionStatus\":2,\"BatteryPercent\":70}");
        public ValueTask<string> ReadLiquidCoolingAsync(CancellationToken token) =>
            ValueTask.FromResult("""{"connected":true,"LCLED_Mode":0,"LCFanLED_Mode":0,"LCLED_R":0,"LCLED_G":176,"LCLED_B":255}""");
        public ValueTask<int?> ReadBatteryChargeLimitAsync(CancellationToken token) => ValueTask.FromResult<int?>(80);
        public ValueTask WriteSettingAsync(string payload, CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask WriteKeyboardAsync(string payload, CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask WriteLightingAsync(LightingZone zone, string payload, CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask<string> WriteLightingAndReadStatusAsync(LightingZone zone, string payload, CancellationToken token) => ReadLightingStatusAsync(zone, token);
        public ValueTask WriteBatteryProtectionAsync(string payload, CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask WriteLiquidCoolingAsync(string payload, CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask WriteBatteryChargeLimitAsync(string payload, CancellationToken token) => ValueTask.CompletedTask;
    }
}
