using System.Globalization;
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

// V1.0.8: at 125 % the user saw the radar's "?" sit left of the middle of its circle, where its text
// box was centred in whole device pixels, half of one off. The home page is laid out as in the window
// at the display scale and rendered at the device resolution, as on screen, and the "?" is compared
// across with the same glyph drawn with its ink exactly in the middle.
[Collection("Display language")]
public sealed class HelpMarkCentringTests
{
    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(1.75)]
    [InlineData(2.0)]
    public void RadarQuestionMark_SitsInTheMiddleAcross(double scale)
    {
        RunOnSta(() =>
        {
            // Presentation only: no App startup, native window, controller, settings write or hardware call.
            var vm = new MainWindowViewModel(HomeDashboardFactory.Create(
                new MachineIdentity("MECHREVO", "YAOSHI Series", "Offline help mark test", "Core Ultra 9 275HX", "test", 24, 24),
                ["Intel Graphics", "NVIDIA GeForce RTX 5090 Laptop GPU"]),
                new ApplicationSettings(UiLanguageCode: "zh-CN"), new MemorySettings(), new NoStartup());
            var window = new MainWindow(vm) { AllowApplicationClose = true };
            try
            {
                var viewport = Assert.IsType<Viewbox>(window.Content);
                window.Content = null;
                var host = new Border { Child = viewport, DataContext = vm, UseLayoutRounding = true };
                host.SetValue(TextElement.FontFamilyProperty, window.FontFamily);
                host.Resources.MergedDictionaries.Add(window.Resources);
                VisualTreeHelper.SetRootDpi(host, new DpiScale(scale, scale));
                host.Measure(new Size(580, 550));
                host.Arrange(new Rect(0, 0, 580, 550));
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
                host.UpdateLayout();

                var mark = Assert.IsType<HelpMark>(window.FindName("RadarHelp"));
                var text = Assert.IsType<TextBlock>(mark.Child);
                // Only the "?" counts as ink.
                mark.BorderBrush = Brushes.Transparent;
                host.UpdateLayout();
                var width = (int)Math.Round(mark.ActualWidth * scale);
                var height = (int)Math.Round(mark.ActualHeight * scale);
                var page = new RenderTargetBitmap((int)Math.Ceiling(580 * scale), (int)Math.Ceiling(550 * scale),
                    96 * scale, 96 * scale, PixelFormats.Pbgra32);
                page.Render(host);
                var origin = mark.TransformToAncestor(host).Transform(new Point());
                var drawn = new CroppedBitmap(page, new Int32Rect(
                    (int)Math.Round(origin.X * scale), (int)Math.Round(origin.Y * scale), width, height));

                // The same glyph on the same line, its ink exactly in the middle across.
                var glyph = new FormattedText(text.Text, CultureInfo.InvariantCulture, text.FlowDirection,
                    new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch),
                    text.FontSize, text.Foreground, scale);
                var ink = glyph.BuildGeometry(new Point()).Bounds;
                var line = text.TransformToAncestor(mark).Transform(new Point()).Y;
                var visual = new DrawingVisual();
                using (var context = visual.RenderOpen())
                {
                    context.DrawRectangle(mark.Background, null, new Rect(mark.RenderSize));
                    context.DrawText(glyph, new Point(mark.ActualWidth / 2 - ink.X - ink.Width / 2, line));
                }
                var expected = new RenderTargetBitmap(width, height, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                expected.Render(visual);

                // S9 was 0.5 px left at 100 % and 125 %.
                Assert.InRange(Across(drawn) - Across(expected), -0.1, 0.1);
            }
            finally
            {
                window.Close();
                vm.Experiments.Dispose();
                UiLanguageService.Current.SetLanguage("zh-CN");
            }
        });
    }

    // Where the ink lies across, in device pixels: the mean column of the dark pixels inside the
    // circle, each weighted by how dark it is. A shifted glyph shifts this by the same amount.
    private static double Across(BitmapSource image)
    {
        var width = image.PixelWidth;
        var height = image.PixelHeight;
        var data = new byte[width * height * 4];
        image.CopyPixels(data, width * 4, 0);
        var inner = width / 2.0 - 1.5;
        double weight = 0, sum = 0;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var dx = x + 0.5 - width / 2.0;
            var dy = y + 0.5 - height / 2.0;
            if (dx * dx + dy * dy > inner * inner) continue;
            var pixel = (y * width + x) * 4;
            var dark = 255 - (data[pixel + 2] * 299 + data[pixel + 1] * 587 + data[pixel] * 114) / 1000.0;
            weight += dark;
            sum += dark * (x + 0.5);
        }
        Assert.True(weight > 0, "no ink rendered");
        return sum / weight;
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
