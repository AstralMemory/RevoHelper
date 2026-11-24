using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RevoHelper.App.Presentation;

namespace RevoHelper.App.Tests.Presentation;

// Snapshot 9: the pill and the settings switches show a focus ring after Tab, never after a click.
// Runs alone, also because it replaces the process-wide KeyboardWasLast.
[Collection("WPF alone")]
public sealed class KeyboardFocusCueTests
{
    [Fact]
    public void RingShowsForKeyboardFocusOnly_AndAClickOrLeavingHidesIt()
    {
        RunOnSta(() =>
        {
            var original = KeyboardFocusCue.KeyboardWasLast;
            var keyboard = true;
            KeyboardFocusCue.KeyboardWasLast = () => keyboard;
            try
            {
                var toggle = new CheckBox();
                KeyboardFocusCue.SetIsEnabled(toggle, true);

                ChangeFocus(toggle, gained: true);
                Assert.True(KeyboardFocusCue.GetIsShown(toggle));
                toggle.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                {
                    RoutedEvent = UIElement.PreviewMouseDownEvent
                });
                Assert.False(KeyboardFocusCue.GetIsShown(toggle));

                ChangeFocus(toggle, gained: false);
                keyboard = false;
                ChangeFocus(toggle, gained: true);
                Assert.False(KeyboardFocusCue.GetIsShown(toggle));

                ChangeFocus(toggle, gained: false);
                keyboard = true;
                ChangeFocus(toggle, gained: true);
                Assert.True(KeyboardFocusCue.GetIsShown(toggle));
                ChangeFocus(toggle, gained: false);
                Assert.False(KeyboardFocusCue.GetIsShown(toggle));

                // Only the element's own focus counts, not focus moving within it.
                var inner = new Button();
                toggle.Content = inner;
                toggle.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, null, inner)
                {
                    RoutedEvent = Keyboard.GotKeyboardFocusEvent
                });
                Assert.False(KeyboardFocusCue.GetIsShown(toggle));
            }
            finally { KeyboardFocusCue.KeyboardWasLast = original; }
        });
    }

    // Snapshot 9 review: a switch clicked first and then used with Space is keyboard use too.
    [Fact]
    public void AKeyOnTheFocusedElementShowsTheRingAgain_ButNotAModifierOrAKeyMeantForAChild()
    {
        RunOnSta(() =>
        {
            var toggle = new CheckBox();
            KeyboardFocusCue.SetIsEnabled(toggle, true);
            var source = new OfflineSource();

            Press(toggle, Key.LeftShift, source);
            Assert.False(KeyboardFocusCue.GetIsShown(toggle));
            var inner = new Button();
            toggle.Content = inner;
            Press(toggle, Key.Space, source, from: inner);
            Assert.False(KeyboardFocusCue.GetIsShown(toggle));

            Press(toggle, Key.Space, source);
            Assert.True(KeyboardFocusCue.GetIsShown(toggle));
            toggle.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            {
                RoutedEvent = UIElement.PreviewMouseDownEvent
            });
            Assert.False(KeyboardFocusCue.GetIsShown(toggle));

            KeyboardFocusCue.SetIsEnabled(toggle, false);
            Press(toggle, Key.Space, source);
            Assert.False(KeyboardFocusCue.GetIsShown(toggle));
        });
    }

    // Key events start at the focused element; "from" stands for a focused element inside this one.
    private static void Press(UIElement element, Key key, PresentationSource source, UIElement? from = null)
    {
        var press = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        if (from is not null) press.Source = from;
        element.RaiseEvent(press);
    }

    // A key event needs a source; no window is opened.
    private sealed class OfflineSource : PresentationSource
    {
        public override Visual? RootVisual { get; set; }
        public override bool IsDisposed => false;
        protected override CompositionTarget? GetCompositionTargetCore() => null;
    }

    private static void ChangeFocus(UIElement element, bool gained) =>
        element.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0,
            gained ? null : element, gained ? element : null)
        {
            RoutedEvent = gained ? Keyboard.GotKeyboardFocusEvent : Keyboard.LostKeyboardFocusEvent
        });

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
