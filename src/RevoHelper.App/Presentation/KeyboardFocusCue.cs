using System.Windows;
using System.Windows.Input;

namespace RevoHelper.App.Presentation;

/// <summary>
/// A focus ring for keyboard users only, like :focus-visible on the web. WPF's own focus box also
/// shows after a mouse click while Windows keeps keyboard cues on, which the user saw as a dotted
/// box around a clicked button. A style that uses this turns FocusVisualStyle off and draws its own
/// ring while <see cref="IsShownProperty"/> is true: focus came from the keyboard, with no click since.
/// </summary>
public static class KeyboardFocusCue
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(KeyboardFocusCue), new PropertyMetadata(false, OnIsEnabledChanged));

    private static readonly DependencyPropertyKey IsShownPropertyKey = DependencyProperty.RegisterAttachedReadOnly(
        "IsShown", typeof(bool), typeof(KeyboardFocusCue), new PropertyMetadata(false));

    public static readonly DependencyProperty IsShownProperty = IsShownPropertyKey.DependencyProperty;

    /// <summary>Whether the latest input came from the keyboard. Tests replace it.</summary>
    public static Func<bool> KeyboardWasLast { get; set; } =
        () => InputManager.Current.MostRecentInputDevice is KeyboardDevice;

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    public static bool GetIsShown(DependencyObject element) => (bool)element.GetValue(IsShownProperty);

    private static void OnIsEnabledChanged(DependencyObject element, DependencyPropertyChangedEventArgs change)
    {
        if (element is not UIElement target) return;
        target.GotKeyboardFocus -= OnGotKeyboardFocus;
        target.LostKeyboardFocus -= OnLostKeyboardFocus;
        target.PreviewMouseDown -= OnPreviewMouseDown;
        target.PreviewKeyDown -= OnPreviewKeyDown;
        target.ClearValue(IsShownPropertyKey);
        if (!(bool)change.NewValue) return;
        target.GotKeyboardFocus += OnGotKeyboardFocus;
        target.LostKeyboardFocus += OnLostKeyboardFocus;
        target.PreviewMouseDown += OnPreviewMouseDown;
        target.PreviewKeyDown += OnPreviewKeyDown;
    }

    // Focus events bubble, so only the element's own focus counts, not a child's.
    private static void OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs change)
    {
        if (ReferenceEquals(change.NewFocus, sender))
            ((UIElement)sender).SetValue(IsShownPropertyKey, KeyboardWasLast());
    }

    private static void OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs change)
    {
        if (ReferenceEquals(change.OldFocus, sender)) ((UIElement)sender).ClearValue(IsShownPropertyKey);
    }

    private static void OnPreviewMouseDown(object sender, MouseButtonEventArgs change) =>
        ((UIElement)sender).ClearValue(IsShownPropertyKey);

    // Space on a switch that was clicked first is keyboard use too. Key events start at the focused
    // element, so only a key aimed at this element itself counts; a modifier alone does not.
    private static void OnPreviewKeyDown(object sender, KeyEventArgs change)
    {
        var key = change.Key == Key.System ? change.SystemKey : change.Key;
        if (!ReferenceEquals(change.OriginalSource, sender) || key is Key.LeftCtrl or Key.RightCtrl or
                Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
            return;
        ((UIElement)sender).SetValue(IsShownPropertyKey, true);
    }
}
