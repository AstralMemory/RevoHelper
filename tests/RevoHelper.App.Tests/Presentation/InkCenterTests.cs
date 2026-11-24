using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RevoHelper.App.Presentation;

namespace RevoHelper.App.Tests.Presentation;

// Snapshot 9: the user saw the question marks of the help icons sit low in their circles. The text
// box keeps room above the glyph for taller letters; InkCenter centres the ink itself. The icons are
// laid out as in the window: a 0.8 border inside the circle and layout rounding at the display scale.
// V1.0.8: the radar's then sat half a pixel left at 125 %, where its box was centred in whole pixels;
// across, the line now centres in the slot to a fraction of a pixel.
[Collection("WPF alone")]
public sealed class InkCenterTests
{
    private const int Zoom = 8;

    [Theory]
    [InlineData(14, 9.3, 1.0)]  // the help icons in the drawers, UiFont 8
    [InlineData(14, 9.3, 1.25)]
    [InlineData(14, 9.3, 1.5)]
    [InlineData(14, 9.3, 2.0)]
    [InlineData(13, 9.3, 1.0)]  // the performance drawer, UiFont 8
    [InlineData(13, 9.3, 1.25)]
    [InlineData(13, 9.3, 1.5)]
    [InlineData(13, 9.3, 2.0)]
    [InlineData(11, 8.3, 1.0)]  // the radar help, UiFont 7
    [InlineData(11, 8.3, 1.25)]
    [InlineData(11, 8.3, 1.5)]
    [InlineData(11, 8.3, 2.0)]
    public void QuestionMark_InkSitsInTheMiddleOfItsCircle(double size, double fontSize, double scale)
    {
        RunOnSta(() =>
        {
            var mark = new TextBlock
            {
                Text = "?",
                FontFamily = new FontFamily("Segoe UI Variable Display, Microsoft YaHei UI, Segoe UI"),
                FontSize = fontSize,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.Black,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            InkCenter.SetIsEnabled(mark, true);
            // The ring itself is left transparent, so only the "?" counts as ink.
            var icon = new Border
            {
                Width = size, Height = size, CornerRadius = new CornerRadius(size / 2),
                BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(0.8),
                Background = Brushes.White, UseLayoutRounding = true, Child = mark
            };
            VisualTreeHelper.SetRootDpi(icon, new DpiScale(scale, scale));
            icon.Measure(new Size(size, size));
            icon.Arrange(new Rect(0, 0, size, size));
            icon.UpdateLayout();

            var (center, height) = InkCenterOf(icon);
            Assert.InRange(height, size * 0.35, size * 0.8);
            // Across, only the render at eight times the size limits the precision.
            Assert.InRange(center.X - icon.ActualWidth / 2, -0.15, 0.15);
            // Down, layout rounding places the text on whole device pixels: half of one is the best possible.
            var tolerance = 0.5 / scale + 0.1;
            Assert.InRange(center.Y - icon.ActualHeight / 2, -tolerance, tolerance);
        });
    }

    // The text draws from the top left of its box, and layout rounding makes that box a fraction of a
    // pixel larger or smaller than the line. The box counts, not the line: shown here without rounding,
    // with a box well larger than the line, where measuring by the line misses by more than 1 DIP.
    [Fact]
    public void QuestionMark_CentresByTheBoxItGot_NotByItsLine()
    {
        RunOnSta(() =>
        {
            var mark = new TextBlock
            {
                Text = "?",
                FontFamily = new FontFamily("Segoe UI Variable Display, Microsoft YaHei UI, Segoe UI"),
                FontSize = 9.3,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.Black,
                Width = 7,
                Height = 15,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            InkCenter.SetIsEnabled(mark, true);
            var icon = new Border { Width = 20, Height = 20, Background = Brushes.White, Child = mark };
            icon.Measure(new Size(20, 20));
            icon.Arrange(new Rect(0, 0, 20, 20));
            icon.UpdateLayout();

            var (center, _) = InkCenterOf(icon);
            Assert.InRange(center.X - 10, -0.15, 0.15);
            Assert.InRange(center.Y - 10, -0.15, 0.15);
        });
    }

    // The centre of the opaque dark pixels, in device-independent units, rendered at eight times the size.
    private static (Point Center, double Height) InkCenterOf(FrameworkElement visual)
    {
        var width = (int)Math.Ceiling(visual.ActualWidth * Zoom);
        var height = (int)Math.Ceiling(visual.ActualHeight * Zoom);
        var bitmap = new RenderTargetBitmap(width, height, 96 * Zoom, 96 * Zoom, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var data = new byte[width * height * 4];
        bitmap.CopyPixels(data, width * 4, 0);
        int left = width, top = height, right = -1, bottom = -1;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var pixel = (y * width + x) * 4;
            if (data[pixel + 3] < 128 || data[pixel + 1] >= 128) continue;
            left = Math.Min(left, x);
            right = Math.Max(right, x);
            top = Math.Min(top, y);
            bottom = Math.Max(bottom, y);
        }
        Assert.True(right >= 0, "no ink rendered");
        return (new Point((left + right + 1) / 2.0 / Zoom, (top + bottom + 1) / 2.0 / Zoom),
            (bottom - top + 1) / (double)Zoom);
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
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
