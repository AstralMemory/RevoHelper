using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RevoHelper.App.Presentation;

/// <summary>
/// ClearType for text in the clipped parts of the main window. The window is layered (rounded,
/// transparent corners), where WPF draws text with ClearType only when told the background is
/// opaque. The hint on the window panel stops at every clip, so the text inside each scrolling
/// drawer and the home device list fell back to grey smoothing, which reads bolder and blurrier.
/// Measured on this machine: the hint helps only on the text element itself, not on a container,
/// so this inherited switch puts it on every TextBlock below the element that turns it on,
/// including the ones a template creates for button text and list items.
/// <para>
/// A disabled control is drawn faded (Opacity below 1) in its own see-through layer, where
/// ClearType must not be used; its text goes back to grey smoothing until it is enabled again.
/// Areas faded by a binding instead turn the switch off themselves.
/// </para>
/// </summary>
public static class TextClarity
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(TextClarity),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits, OnEnabledChanged));

    public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);

    public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);

    private static void OnEnabledChanged(DependencyObject element, DependencyPropertyChangedEventArgs change)
    {
        if (element is not TextBlock text) return;
        text.IsEnabledChanged -= OnTextEnabledChanged;
        if ((bool)change.NewValue) text.IsEnabledChanged += OnTextEnabledChanged;
        Apply(text);
    }

    private static void OnTextEnabledChanged(object sender, DependencyPropertyChangedEventArgs change) => Apply((TextBlock)sender);

    private static void Apply(TextBlock text) => RenderOptions.SetClearTypeHint(text,
        GetEnabled(text) && text.IsEnabled ? ClearTypeHint.Enabled : ClearTypeHint.Auto);
}
