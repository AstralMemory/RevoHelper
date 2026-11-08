using System.Windows;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace RevoHelper.App.Presentation;

/// <summary>
/// Pastel surfaces stay light in both themes, so everything inside them uses the light palette:
/// dark copy, and light inputs, buttons and lines (a dark input on a pastel card was unreadable).
/// </summary>
public static class ThemeScope
{
    private static readonly object Missing = new();
    private static readonly ConditionalWeakTable<FrameworkElement, Dictionary<string, object>> Originals = new();
    public static readonly DependencyProperty AccentSurfaceProperty = DependencyProperty.RegisterAttached(
        "AccentSurface", typeof(bool), typeof(ThemeScope), new PropertyMetadata(false, Changed));

    public static void SetAccentSurface(DependencyObject element, bool value) => element.SetValue(AccentSurfaceProperty, value);
    public static bool GetAccentSurface(DependencyObject element) => (bool)element.GetValue(AccentSurfaceProperty);

    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not FrameworkElement element) return;
        if (args.NewValue is not true)
        {
            if (!Originals.TryGetValue(element, out var previous)) return;
            foreach (var pair in previous)
                if (ReferenceEquals(pair.Value, Missing)) element.Resources.Remove(pair.Key);
                else element.Resources[pair.Key] = pair.Value;
            Originals.Remove(element);
            return;
        }
        if (Originals.TryGetValue(element, out _)) return;
        var original = new Dictionary<string, object>();
        Originals.Add(element, original);
        void Set(string key, object value)
        {
            original[key] = element.Resources.Contains(key) ? element.Resources[key] : Missing;
            element.Resources[key] = value;
        }
        var primary = Brush("#17202D");
        var secondary = Brush("#66758C");
        Set("TextBrush", primary);
        Set("MutedBrush", secondary);
        Set("QuietTextBrush", secondary);
        foreach (var key in AppTheme.SurfaceKeys)
            Set(key, Brush(AppTheme.LightColorOf(key)));
        foreach (var pair in AppTheme.NeutralBrushKeys)
        {
            var color = (Color)ColorConverter.ConvertFromString(pair.Key);
            // Text-like neutrals become the dark copy; pale surfaces and borders keep their light color.
            Set(pair.Value, Math.Max(color.R, Math.Max(color.G, color.B)) < 200
                ? color.R < 80 ? primary : secondary
                : Brush(pair.Key));
        }
        // Inherited Foreground is deliberately left alone: some pastel borders sit inside control
        // templates whose text follows the control. Text on a pastel card names TextBrush instead.
    }

    private static SolidColorBrush Brush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}
