using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using RevoHelper.App.Monitoring;
using RevoHelper.App.Settings;

namespace RevoHelper.App.Tests.Monitoring;

public sealed class PerformanceMonitorWindowTests
{
    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 12)]
    [InlineData(2, 15)]
    [InlineData(3, 18)]
    [InlineData(4, 21)]
    public void FontPresets_KeepTheBoldMeterAtFiveDefinedSizes(int preset, double expected) => OnSta(() =>
    {
        var window = new PerformanceMonitorWindow();
        try
        {
            window.ApplyPreferences(new(SizePreset: preset));
            Assert.Equal(expected, window.FontSize);
            Assert.Equal(FontWeights.Bold, window.FontWeight);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void LockedWindow_IsClickThroughAndNonActivating_AndUnlockRestoresInput() => OnSta(() =>
    {
        var window = new PerformanceMonitorWindow();
        try
        {
            var handle = new WindowInteropHelper(window).EnsureHandle();
            var original = GetWindowLongPtr(handle, -20).ToInt64();
            window.ApplyPreferences(new(IsPositionLocked: true));

            var locked = GetWindowLongPtr(handle, -20).ToInt64();
            Assert.NotEqual(0, locked & 0x00080000); // WPF layered surface.
            Assert.NotEqual(0, locked & 0x00000020); // Mouse input passes to underlying windows.
            Assert.NotEqual(0, locked & 0x08000000); // Does not activate on click.
            Assert.False(window.IsHitTestVisible);

            window.ApplyPreferences(new(IsPositionLocked: false));
            var unlocked = GetWindowLongPtr(handle, -20).ToInt64();
            Assert.Equal(original & 0x08000020, unlocked & 0x08000020);
            Assert.True(window.IsHitTestVisible);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void LockSetBeforeSourceInitialization_IsAppliedToTheCreatedHiddenHandle() => OnSta(() =>
    {
        var window = new PerformanceMonitorWindow();
        try
        {
            window.ApplyPreferences(new(IsPositionLocked: true));
            var handle = new WindowInteropHelper(window).EnsureHandle();
            Assert.NotEqual(0, GetWindowLongPtr(handle, -20).ToInt64() & 0x08000020);
            Assert.False(window.IsHitTestVisible);
        }
        finally { window.Close(); }
    });

    private static void OnSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { error = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "The hidden-window check did not complete.");
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
}
