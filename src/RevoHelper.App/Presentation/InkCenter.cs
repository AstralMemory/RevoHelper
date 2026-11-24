using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RevoHelper.App.Presentation;

/// <summary>
/// Centers a lone glyph, such as the "?" of a help icon, by its ink instead of its line box. The
/// line box keeps room above the glyph for taller letters, so a "?" centered in its circle sat
/// about 1 px low (measured in the offline renders: 1 to 1.5 DIP in the 14 px icons).
/// <para>
/// Across, the help icons let the text fill its slot and center its line with
/// <see cref="TextAlignment.Center"/>. A line is placed to a fraction of a pixel, while a box centered in its slot lands on whole device
/// pixels under layout rounding, up to half of one off: the radar's "?" sat half a pixel left of the
/// middle of its 14 px circle at 125 %. The small shift for the ink across is a render transform for
/// the same reason. Down, the margin keeps layout rounding; WPF puts the baseline on a whole pixel
/// even without it (measured offline: the glyph still moved in whole pixels).
/// </para>
/// </summary>
public static class InkCenter
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(InkCenter), new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject element, DependencyPropertyChangedEventArgs change)
    {
        if (element is not TextBlock text) return;
        text.SizeChanged -= OnSizeChanged;
        if ((bool)change.NewValue) text.SizeChanged += OnSizeChanged;
        else
        {
            text.ClearValue(FrameworkElement.MarginProperty);
            text.ClearValue(UIElement.RenderTransformProperty);
        }
    }

    // The font is final only once the text sits in the window, so its first layout is the earliest
    // point. The margin below keeps the size, so this runs once, and again only if the size changes.
    private static void OnSizeChanged(object sender, SizeChangedEventArgs change) => Apply((TextBlock)sender);

    private static void Apply(TextBlock text)
    {
        var offset = InkOffset(text);
        // Equal and opposite margins keep the size, so a centered text moves down by exactly the offset.
        text.Margin = new Thickness(0, -offset.Y, 0, offset.Y);
        text.RenderTransform = new TranslateTransform(-offset.X, 0);
    }

    /// <summary>
    /// How far the ink center lies from the center of the text's own box, in DIPs. The line starts at
    /// the left of that box, or in its middle when centered, and the box can be larger than the line
    /// (a set size, a slot to fill, or layout rounding), so the box the text actually got counts.
    /// </summary>
    public static Vector InkOffset(TextBlock text)
    {
        var formatted = new FormattedText(text.Text, CultureInfo.InvariantCulture, text.FlowDirection,
            new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch),
            text.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(text).PixelsPerDip);
        var ink = formatted.BuildGeometry(new Point()).Bounds;
        if (ink.IsEmpty) return default;
        var line = text.TextAlignment switch
        {
            TextAlignment.Center => (text.ActualWidth - formatted.WidthIncludingTrailingWhitespace) / 2,
            TextAlignment.Right => text.ActualWidth - formatted.WidthIncludingTrailingWhitespace,
            _ => 0
        };
        return new Vector(line + ink.X + ink.Width / 2 - text.ActualWidth / 2, ink.Y + ink.Height / 2 - text.ActualHeight / 2);
    }
}
