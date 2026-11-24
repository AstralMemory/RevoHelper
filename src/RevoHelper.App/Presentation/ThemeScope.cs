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
    private static readonly KeyValuePair<string, SolidColorBrush>[] Palette = CreatePalette();
    private static readonly ConditionalWeakTable<FrameworkElement, Dictionary<string, object>> Originals = new();
    public static readonly DependencyProperty AccentSurfaceProperty = DependencyProperty.RegisterAttached(
        "AccentSurface", typeof(bool), typeof(ThemeScope), new PropertyMetadata(false, Changed));

    public static void SetAccentSurface(DependencyObject element, bool value) => element.SetValue(AccentSurfaceProperty, value);
    public static bool GetAccentSurface(DependencyObject element) => (bool)element.GetValue(AccentSurfaceProperty);

    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not FrameworkElement element) return;
        var resources = element.Resources;
        if (args.NewValue is not true)
        {
            Originals.TryGetValue(element, out var previous);
            foreach (var (key, _) in Palette)
            {
                if (previous is not null && previous.TryGetValue(key, out var value)) resources[key] = value;
                else resources.Remove(key);
            }
            Originals.Remove(element);
            return;
        }
        Dictionary<string, object>? original = null;
        foreach (var (key, brush) in Palette)
        {
            if (resources.Contains(key))
            {
                original ??= new(StringComparer.Ordinal);
                original.Add(key, resources[key]);
            }
            resources[key] = brush;
        }
        if (original is not null) Originals.Add(element, original);
    }

    // The brushes are frozen and can be shared across scopes and UI threads. Each element
    // keeps its own resources so existing lookups and local overrides retain their precedence.
    private static KeyValuePair<string, SolidColorBrush>[] CreatePalette()
    {
        var primary = Brush("#17202D");
        var secondary = Brush("#66758C");
        var palette = new List<KeyValuePair<string, SolidColorBrush>>
        {
            new("TextBrush", primary),
            new("MutedBrush", secondary),
            new("QuietTextBrush", secondary)
        };
        foreach (var key in AppTheme.SurfaceKeys)
            palette.Add(new(key, Brush(AppTheme.LightColorOf(key))));
        foreach (var pair in AppTheme.NeutralBrushKeys)
        {
            var color = (Color)ColorConverter.ConvertFromString(pair.Key);
            // Text-like neutrals become the dark copy; pale surfaces and borders keep their light color.
            var brush = Math.Max(color.R, Math.Max(color.G, color.B)) < 200
                ? color.R < 80 ? primary : secondary
                : Brush(pair.Key);
            palette.Add(new(pair.Value, brush));
        }
        return palette.ToArray();
    }

    private static SolidColorBrush Brush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}
