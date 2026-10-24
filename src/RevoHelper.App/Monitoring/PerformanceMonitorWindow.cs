using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using RevoHelper.App.Settings;
using RevoHelper.App.Presentation;

namespace RevoHelper.App.Monitoring;

public sealed class PerformanceMonitorWindow : Window
{
    private readonly Border surface;
    private readonly StackPanel row;
    private string rendered = "";
    private readonly Dictionary<string, Brush> brushes = new(StringComparer.Ordinal);
    private PerformanceMonitorSettings currentPreferences = new();
    private bool placing;
    private bool dragging;
    private long originalInputStyles;
    private const int ExtendedStyleIndex = -20;
    private const long TransparentStyle = 0x00000020;
    private const long NoActivateStyle = 0x08000000;
    public event Action? SettingsRequested;
    public event Action? HideRequested;
    public event Action<double, double>? PositionSaved;

    public PerformanceMonitorWindow()
    {
        Title = "RevoHelper 性能监控";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        surface = new Border { Padding = new Thickness(7, 4, 7, 4), Child = row };
        Content = surface;
        var menu = new ContextMenu();
        var settings = new MenuItem { Header = "监控设置…" };
        settings.Click += (_, _) => SettingsRequested?.Invoke();
        var hide = new MenuItem { Header = "关闭性能监控" };
        hide.Click += (_, _) => HideRequested?.Invoke();
        menu.Items.Add(settings);
        menu.Items.Add(hide);
        menu.Opened += (_, _) =>
        {
            var english = UiLanguageService.Current.Code == "en-US";
            settings.Header = english ? "Monitor settings…" : "监控设置…";
            hide.Header = english ? "Turn off performance monitor" : "关闭性能监控";
            if (Application.Current?.MainWindow?.DataContext is MainWindowViewModel viewModel)
                AppTheme.Apply(menu.Resources, viewModel.SelectedUiThemeCode);
            menu.SetResourceReference(BackgroundProperty, "SurfaceBrush");
            menu.SetResourceReference(ForegroundProperty, "TextBrush");
        };
        ContextMenu = menu;
        SourceInitialized += (_, _) =>
        {
            originalInputStyles = GetWindowLongPtr(new WindowInteropHelper(this).Handle, ExtendedStyleIndex).ToInt64()
                & (TransparentStyle | NoActivateStyle);
            ApplyInputMode();
        };
        SizeChanged += (_, _) =>
        {
            if (IsLoaded && !placing && !dragging && currentPreferences.PositionPreset != MonitorPositionPreset.Custom)
                PlaceOnScreen(currentPreferences);
        };
        MouseLeftButtonDown += (_, args) =>
        {
            if (currentPreferences.IsPositionLocked) return;
            if (args.ClickCount == 2) { SettingsRequested?.Invoke(); return; }
            try
            {
                dragging = true;
                DragMove();
                currentPreferences = currentPreferences with
                { Left = Left, Top = Top, PositionPreset = MonitorPositionPreset.Custom };
                PositionSaved?.Invoke(Left, Top);
            }
            catch (InvalidOperationException) { }
            finally { dragging = false; }
        };
    }

    public void ApplyPreferences(PerformanceMonitorSettings preferences)
    {
        preferences = preferences.Normalize();
        currentPreferences = preferences;
        Topmost = preferences.AlwaysOnTop;
        FontFamily = new FontFamily("Microsoft YaHei");
        FontWeight = FontWeights.Bold;
        FontSize = preferences.SizePreset switch { 0 => 10, 1 => 12, 2 => 15, 3 => 18, _ => 21 };
        surface.Background = new SolidColorBrush(Color.FromArgb((byte)Math.Round(preferences.BackgroundOpacity * 255), 0, 0, 0));
        IsHitTestVisible = !preferences.IsPositionLocked;
        Focusable = !preferences.IsPositionLocked;
        ApplyInputMode();
        if (IsLoaded) PlaceOnScreen(preferences);
    }

    public void PlaceOnScreen(PerformanceMonitorSettings preferences)
    {
        currentPreferences = preferences.Normalize();
        if (placing) return;
        placing = true;
        try
        {
            UpdateLayout();
            var bounds = SystemParameters.WorkArea;
            var width = double.IsFinite(ActualWidth) && ActualWidth > 0 ? ActualWidth : 1000;
            var height = double.IsFinite(ActualHeight) && ActualHeight > 0 ? ActualHeight : 32;
            var dpi = VisualTreeHelper.GetDpi(this);
            var area = currentPreferences.Left is double x && currentPreferences.Top is double y
                ? System.Windows.Forms.Screen.AllScreens.FirstOrDefault(screen => screen.WorkingArea.Contains(
                    (int)(x * dpi.DpiScaleX), (int)(y * dpi.DpiScaleY))) : null;
            if (area is not null)
                bounds = new Rect(area.WorkingArea.X / dpi.DpiScaleX, area.WorkingArea.Y / dpi.DpiScaleY,
                    area.WorkingArea.Width / dpi.DpiScaleX, area.WorkingArea.Height / dpi.DpiScaleY);
            var custom = currentPreferences.Left is double savedX && currentPreferences.Top is double savedY
                ? new Point(savedX, savedY) : (Point?)null;
            var point = MonitorPlacement.Calculate(bounds, new(width, height), currentPreferences.PositionPreset, custom);
            Left = point.X;
            Top = point.Y;
        }
        finally { placing = false; }
    }

    private void ApplyInputMode()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        var current = GetWindowLongPtr(handle, ExtendedStyleIndex).ToInt64();
        var desired = currentPreferences.IsPositionLocked
            ? current | TransparentStyle | NoActivateStyle
            : (current & ~(TransparentStyle | NoActivateStyle)) | originalInputStyles;
        if (current == desired) return;
        // AllowsTransparency already creates a layered HWND. WS_EX_TRANSPARENT
        // passes its input to windows below, unlike WPF hit-testing alone:
        // https://learn.microsoft.com/windows/win32/winmsg/window-features#layered-windows
        Marshal.SetLastPInvokeError(0);
        if (SetWindowLongPtr(handle, ExtendedStyleIndex, new IntPtr(desired)) == IntPtr.Zero &&
            Marshal.GetLastWin32Error() is var error && error != 0)
            throw new Win32Exception(error, "无法更新性能监控小窗的输入模式。");
        if (!SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, 0x0037))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法刷新性能监控小窗的输入模式。");
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    public void SetReadings(IReadOnlyList<MonitorTextRun> runs)
    {
        var signature = string.Concat(runs.Select(run => run.Color + run.Text));
        if (signature == rendered) return;
        rendered = signature;
        while (row.Children.Count > runs.Count) row.Children.RemoveAt(row.Children.Count - 1);
        for (var index = 0; index < runs.Count; index++)
        {
            if (index == row.Children.Count) row.Children.Add(new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 1, ShadowDepth = 1, Opacity = 0.8 }
            });
            var text = (TextBlock)row.Children[index];
            text.Text = runs[index].Text;
            if (!brushes.TryGetValue(runs[index].Color, out var brush))
            {
                brush = (Brush)new BrushConverter().ConvertFromString(runs[index].Color)!;
                if (brush.CanFreeze) brush.Freeze();
                brushes.Add(runs[index].Color, brush);
            }
            text.Foreground = brush;
        }
    }
}
