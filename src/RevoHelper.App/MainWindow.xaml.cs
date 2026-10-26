using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using RevoHelper.App.Presentation;
using RevoHelper.App.Settings;
using RevoHelper.Core.Control;
using RevoHelper.Core.Dashboard;

namespace RevoHelper.App;

public partial class MainWindow : Window
{
    private WindowScaleLayout? windowLayout;
    private double? preferredMainPanelLeft;
    private double? preferredWindowTop;
    private bool applyingWindowGeometry;
    private HwndSource? windowSource;
    private bool synchronizingProfileButtons;
    private bool refreshingLiquidCooling;
    private readonly DispatcherTimer liquidCoolingRefreshTimer;
    private readonly DispatcherTimer shutdownCountdownTimer;
    private readonly SemaphoreSlim lightingAutoApplyLock = new(1, 1);
    private readonly Dictionary<LightingZone, PendingLightingEdit> pendingLightingEdits = [];
    private readonly SemaphoreSlim performanceAutoApplyLock = new(1, 1);
    private readonly SemaphoreSlim customPerformanceAutoApplyLock = new(1, 1);
    private readonly SemaphoreSlim fanAutoApplyLock = new(1, 1);
    private readonly SemaphoreSlim batteryAutoApplyLock = new(1, 1);
    private readonly LightingMutationVersions lightingMutationVersions = new();
    private long lightingSleepApplyVersion;
    private long performanceAutoApplyVersion;
    private long customPerformanceAutoApplyVersion;
    private long profileDrawerRefreshVersion;
    private long fanAutoApplyVersion;
    private long batteryAutoApplyVersion;
    public bool AllowApplicationClose { get; set; }

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        AppTheme.Apply(Resources, viewModel.SelectedUiThemeCode);
        SetResourceReference(ForegroundProperty, "TextBrush");
        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        liquidCoolingRefreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(1500)
        };
        liquidCoolingRefreshTimer.Tick += LiquidCoolingRefreshTimer_Tick;
        shutdownCountdownTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        shutdownCountdownTimer.Tick += ShutdownCountdownTimer_Tick;
        IsVisibleChanged += (_, _) => UpdateShutdownCountdownTimer();
        SourceInitialized += (_, _) =>
        {
            windowSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            windowSource?.AddHook(WindowMessageHook);
        };
        LocationChanged += Window_LocationChanged;
        ApplyWindowGeometry();
    }

    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext;

    private void CapabilityRecheck_Click(object sender, RoutedEventArgs e) => ViewModel.RequestCapabilityRecheck();

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyWindowGeometry();
        Topmost = ViewModel.AlwaysOnTop;
        SynchronizeProfileButtons();
        UpdateRadar();
        UpdateLiquidCoolingRefreshTimer();
    }

    private async void Window_StateChanged(object? sender, EventArgs e)
    {
        UpdateLiquidCoolingRefreshTimer();
        UpdateShutdownCountdownTimer();
        if (WindowState is WindowState.Normal && IsVisible)
        {
            await ViewModel.SynchronizeControlSessionAsync(CancellationToken.None);
        }
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState is MouseButtonState.Pressed)
        {
            DragMove();
            ApplyWindowGeometry();
        }
    }

    private async void Profile_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || synchronizingProfileButtons ||
            DataContext is not MainWindowViewModel viewModel ||
            sender is not FrameworkElement { Tag: string profileId })
        {
            return;
        }

        var refreshVersion = Interlocked.Increment(ref profileDrawerRefreshVersion);
        await viewModel.ApplyPlatformProfileAsync(profileId, CancellationToken.None);
        await RefreshLatestProfileDrawerAsync(refreshVersion);
    }

    private async void TurboStrategy_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || synchronizingProfileButtons ||
            DataContext is not MainWindowViewModel viewModel ||
            sender is not FrameworkElement { Tag: string strategyId })
        {
            return;
        }

        var strategy = strategyId == "high"
            ? TurboStrategy.HighEfficiency
            : TurboStrategy.Quiet;

        var refreshVersion = Interlocked.Increment(ref profileDrawerRefreshVersion);
        await viewModel.ApplyTurboStrategyAsync(strategy, CancellationToken.None);
        await RefreshLatestProfileDrawerAsync(refreshVersion);
    }

    private async void CustomProfile_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || synchronizingProfileButtons ||
            sender is not FrameworkElement { Tag: string profileText } ||
            !int.TryParse(profileText, out var profileIndex))
        {
            return;
        }

        var refreshVersion = Interlocked.Increment(ref profileDrawerRefreshVersion);
        await ViewModel.ApplyCustomProfileAsync(profileIndex, CancellationToken.None);
        await RefreshLatestProfileDrawerAsync(refreshVersion);
    }

    private async Task RefreshLatestProfileDrawerAsync(long refreshVersion)
    {
        if (refreshVersion != Volatile.Read(ref profileDrawerRefreshVersion)) return;
        if (ViewModel.ActiveDrawer == HomeDrawer.Performance)
            await ViewModel.OpenPerformanceAsync(CancellationToken.None);
        else if (ViewModel.ActiveDrawer == HomeDrawer.Fan)
            await ViewModel.OpenFanCurveAsync(CancellationToken.None);
        if (refreshVersion != Volatile.Read(ref profileDrawerRefreshVersion)) return;
        SynchronizeProfileButtons();
        UpdateRadar();
    }

    private async void QuickAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string action })
        {
            if (action == "显卡输出模式")
            {
                Task gpuInitialization = ViewModel.OpenGpuOutputAsync(CancellationToken.None);
                OpenDrawer();
                await gpuInitialization;
                SynchronizeGpuOutputButtons();
                return;
            }
            Task initialization = action switch
            {
                "灯光调控" => ViewModel.OpenLightingAsync(CancellationToken.None),
                "水冷" => ViewModel.OpenLiquidCoolingAsync(CancellationToken.None),
                "按键开关" => ViewModel.OpenKeyboardControlsAsync(CancellationToken.None),
                "电源" => ViewModel.OpenBatteryAsync(CancellationToken.None),
                "显示" => ViewModel.OpenDisplayAsync(CancellationToken.None),
                "其它" => ViewModel.OpenOtherAsync(CancellationToken.None),
                _ => Task.CompletedTask
            };
            OpenDrawer();
            await initialization;
            if (action == "灯光调控") SynchronizeLightingZoneButtons();
        }
    }

    private void PerformanceMonitor_Click(object sender, RoutedEventArgs e)
    {
        ShowPerformanceMonitorSettings();
    }

    public void ShowPerformanceMonitorSettings()
    {
        ViewModel.OpenPerformanceMonitor();
        OpenDrawer();
    }

    private void Announcements_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.OpenAnnouncements();
        OpenDrawer();
    }

    private void AnnouncementsBack_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.OpenSettings();
        OpenDrawer();
    }

    private void AnnouncementLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string action } || AnnouncementCopy.LinkFor(action) is not { } url) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(this, ViewModel.SelectedUiLanguageCode == "en-US"
                ? $"Could not open the browser. You can copy this address:\n{url}"
                : $"暂时无法打开浏览器，可手动访问：\n{url}", "RevoHelper", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void MonitorPosition_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string preset } &&
            Enum.TryParse<MonitorPositionPreset>(preset, out var position))
            ViewModel.Monitor.SelectPosition(position);
    }

    private void AddMonitorProgram_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = ViewModel.SelectedUiLanguageCode == "en-US" ? "Add a program to monitor" : "添加需要监控的程序",
            Filter = ViewModel.SelectedUiLanguageCode == "en-US" ? "Programs (*.exe)|*.exe" : "可执行程序 (*.exe)|*.exe",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true) ViewModel.Monitor.AddProgram(dialog.FileName);
    }

    private void RemoveMonitorProgram_Click(object sender, RoutedEventArgs e) =>
        ViewModel.Monitor.RemoveSelectedProgram();

    private void KeyboardLighting_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox checkBox) return;
        var enabled = checkBox.IsChecked == true;
        var configuration = ViewModel.CaptureKeyboardLightingPower(enabled);
        QueueCapturedLightingConfiguration(configuration, TimeSpan.Zero, powerChange: true);
    }

    private void LightingZoneTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string zoneText } &&
            Enum.TryParse<LightingZone>(zoneText, out var zone))
        {
            ViewModel.SelectLightingZone(zone);
            SynchronizeLightingZoneButtons();
        }
    }

    private async void LightingPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string profileId }) return;
        var profile = LightingProfileCatalog.BuiltIns.FirstOrDefault(candidate => candidate.Id == profileId);
        if (profile is null) return;
        foreach (var zone in profile.Zones.Select(configuration => configuration.Zone)) pendingLightingEdits.Remove(zone);
        var version = lightingMutationVersions.Reserve(LightingZone.Keyboard);
        await RunLatestLightingMutationAsync(
            LightingZone.Keyboard, version,
            () => ViewModel.ApplyLightingProfileAsync(profile, CancellationToken.None));
    }

    private async void OemDefaultLighting_Click(object sender, RoutedEventArgs e)
    {
        pendingLightingEdits.Remove(LightingZone.Keyboard);
        var version = lightingMutationVersions.Reserve(LightingZone.Keyboard);
        await RunLatestLightingMutationAsync(
            LightingZone.Keyboard, version,
            () => ViewModel.ApplyOemDefaultLightingAsync(CancellationToken.None));
    }

    private void LightingZonePower_Click(object sender, RoutedEventArgs e) =>
        QueueLightingAutoApply(powerChange: true);

    private void LightingEffect_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsUserLightingComboBoxSelection(sender) ||
            sender is not ComboBox { SelectedValue: LightingEffect effect }) return;
        ViewModel.SelectedLightingEffect = effect;
        QueueLightingAutoApply();
    }

    private void LightingDirection_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsUserLightingComboBoxSelection(sender) ||
            sender is not ComboBox { SelectedItem: LightingDirection direction }) return;
        ViewModel.SelectedLightingDirection = direction;
        QueueLightingAutoApply();
    }

    private void LightingSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (IsLoaded && !ViewModel.IsSynchronizingLightingEditor &&
            sender is Slider slider && (slider.IsMouseCaptureWithin || slider.IsKeyboardFocusWithin))
            QueueLightingAutoApply(TimeSpan.FromMilliseconds(220));
    }

    private void LightingColorHexBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (ViewModel.CanEditLightingColor && !ViewModel.IsSynchronizingLightingEditor)
            QueueLightingAutoApply(TimeSpan.FromMilliseconds(120));
    }

    private void LightingColorHexBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || !LightingColorHexBox.IsKeyboardFocusWithin) return;
        QueueLightingAutoApply();
        e.Handled = true;
    }

    private void QueueLightingAutoApply(TimeSpan? delay = null, bool powerChange = false)
    {
        if (!IsLoaded || ViewModel.IsSynchronizingLightingEditor) return;
        var configuration = ViewModel.CaptureLightingEditor();
        if (configuration is null) return;
        QueueCapturedLightingConfiguration(configuration, delay ?? TimeSpan.Zero, powerChange);
    }

    private void QueueCapturedLightingConfiguration(LightingZoneConfiguration configuration, TimeSpan delay, bool powerChange)
    {
        var version = lightingMutationVersions.Reserve(configuration.Zone);
        // Coalesce within a zone only. A tab change or a sleep-timer edit must not discard
        // a color request, and replacing a pending power request must retain its intent.
        var request = new PendingLightingEdit(version, configuration,
            powerChange || pendingLightingEdits.GetValueOrDefault(configuration.Zone)?.PowerChange == true);
        pendingLightingEdits[configuration.Zone] = request;
        _ = ApplyLatestLightingEditorAsync(request, delay);
    }

    private async Task ApplyLatestLightingEditorAsync(PendingLightingEdit request, TimeSpan delay)
    {
        if (delay > TimeSpan.Zero) await Task.Delay(delay);
        if (pendingLightingEdits.GetValueOrDefault(request.Configuration.Zone) != request) return;

        await lightingAutoApplyLock.WaitAsync();
        try
        {
            if (pendingLightingEdits.GetValueOrDefault(request.Configuration.Zone) != request) return;
            pendingLightingEdits.Remove(request.Configuration.Zone);
            if (request.PowerChange)
                await ViewModel.SetLightingZonePowerAsync(request.Configuration, CancellationToken.None);
            else
                await ViewModel.ApplyLightingConfigurationAsync(request.Configuration, CancellationToken.None);
        }
        finally
        {
            lightingAutoApplyLock.Release();
        }
    }

    private sealed record PendingLightingEdit(long Version, LightingZoneConfiguration Configuration, bool PowerChange);

    private void LightingSleepTimer_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsUserLightingComboBoxSelection(sender) ||
            sender is not ComboBox { SelectedValue: int seconds }) return;
        ViewModel.LightingSleepSeconds = seconds;
        var version = Interlocked.Increment(ref lightingSleepApplyVersion);
        _ = ApplyLatestLightingSleepTimerAsync(version);
    }

    private async Task ApplyLatestLightingSleepTimerAsync(long version)
    {
        await lightingAutoApplyLock.WaitAsync();
        try
        {
            if (version == Volatile.Read(ref lightingSleepApplyVersion) &&
                ViewModel.CanApplyLightingSleepTimer)
                await ViewModel.ApplyLightingSleepTimerAsync(CancellationToken.None);
        }
        finally
        {
            lightingAutoApplyLock.Release();
        }
    }

    private bool IsUserComboBoxSelection(object sender) =>
        IsLoaded && sender is ComboBox comboBox &&
        comboBox.IsDropDownOpen && !ViewModel.IsSynchronizingLightingEditor;

    private bool IsUserLightingComboBoxSelection(object sender) =>
        IsLoaded && sender is ComboBox comboBox &&
        (comboBox.IsDropDownOpen || comboBox.IsKeyboardFocusWithin) &&
        !ViewModel.IsSynchronizingLightingEditor;

    private async Task RunLatestLightingMutationAsync(LightingZone zone, long version, Func<Task> mutation)
    {
        await lightingAutoApplyLock.WaitAsync();
        try
        {
            if (lightingMutationVersions.IsCurrent(zone, version)) await mutation();
        }
        finally
        {
            lightingAutoApplyLock.Release();
        }
    }

    private async void ApplyImportedLighting_Click(object sender, RoutedEventArgs e)
    {
        pendingLightingEdits.Remove(LightingZone.Keyboard);
        var version = lightingMutationVersions.Reserve(LightingZone.Keyboard);
        await RunLatestLightingMutationAsync(
            LightingZone.Keyboard, version,
            () => ViewModel.ApplyImportedLightingProfileAsync(CancellationToken.None));
    }

    private void LightingColorChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string color })
        {
            ViewModel.SetLightingColor(color);
            QueueLightingAutoApply();
        }
    }

    private void SaveLightingProfile_Click(object sender, RoutedEventArgs e) => ViewModel.SaveMyLightingProfile();

    private void ImportLightingProfile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = ViewModel.SelectedUiLanguageCode == "en-US" ? "Import RevoHelper keyboard lighting" : "导入 RevoHelper 键盘灯光方案",
            Filter = ViewModel.SelectedUiLanguageCode == "en-US" ? "RevoHelper lighting (*.revolight.json)|*.revolight.json|JSON files (*.json)|*.json" : "RevoHelper 键盘灯光方案 (*.revolight.json)|*.revolight.json|JSON 文件 (*.json)|*.json",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true)
        {
            ViewModel.ImportLightingProfile(dialog.FileName);
            SynchronizeLightingZoneButtons();
        }
    }

    private void ExportLightingProfile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = ViewModel.SelectedUiLanguageCode == "en-US" ? "Export RevoHelper keyboard lighting" : "导出 RevoHelper 键盘灯光方案",
            Filter = ViewModel.SelectedUiLanguageCode == "en-US" ? "RevoHelper lighting (*.revolight.json)|*.revolight.json" : "RevoHelper 键盘灯光方案 (*.revolight.json)|*.revolight.json",
            DefaultExt = ".revolight.json",
            AddExtension = true,
            FileName = "my-lighting.revolight.json"
        };
        if (dialog.ShowDialog(this) == true) ViewModel.ExportLightingProfile(dialog.FileName);
    }

    private void SynchronizeLightingZoneButtons()
    {
        foreach (var button in new[] { LightingKeyboardTab, LightingCoastTab, LightingLogoTab, LightingWaterTab })
        {
            var selected = button.Tag is string text && Enum.TryParse<LightingZone>(text, out var zone) &&
                           zone == ViewModel.SelectedLightingZone;
            if (selected)
            {
                button.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E4F7FF"));
                button.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00A7D8"));
                button.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#007EA6"));
            }
            else
            {
                button.SetResourceReference(BackgroundProperty, "SurfaceBrush");
                button.SetResourceReference(BorderBrushProperty, "ControlBorderBrush");
                button.SetResourceReference(ForegroundProperty, "MutedBrush");
            }
        }
    }

    private async void WinKey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox)
            await ViewModel.SetWinKeyAsync(checkBox.IsChecked == true, CancellationToken.None);
    }

    private async void FnKey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox)
            await ViewModel.SetFnKeyAsync(checkBox.IsChecked == true, CancellationToken.None);
    }

    private async void Osd_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox)
            await ViewModel.SetOsdAsync(checkBox.IsChecked == true, CancellationToken.None);
    }

    private async void SystemToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { Tag: string value } checkBox &&
            Enum.TryParse<SystemToggleControl>(value, out var control))
            await ViewModel.SetSystemToggleAsync(control, checkBox.IsChecked == true, CancellationToken.None);
    }

    private async void LiquidFan_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string value } && int.TryParse(value, out var mode))
            await ViewModel.SetLiquidCoolingFanAsync((LiquidCoolingFanMode)mode, CancellationToken.None);
    }

    private async void ScanLiquidCooling_Click(object sender, RoutedEventArgs e) =>
        await ViewModel.ScanLiquidCoolingDevicesAsync(CancellationToken.None);

    private async void ConnectLiquidCooling_Click(object sender, RoutedEventArgs e) =>
        await ViewModel.ConnectLiquidCoolingAsync(CancellationToken.None);

    private async void DisconnectLiquidCooling_Click(object sender, RoutedEventArgs e) =>
        await ViewModel.DisconnectLiquidCoolingAsync(CancellationToken.None);

    private async void LiquidCoolingAutoConnect_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox checkBox) return;
        ViewModel.IsLiquidCoolingAutoConnectEnabled = checkBox.IsChecked == true;
        if (ViewModel.IsLiquidCoolingAutoConnectEnabled)
            await ViewModel.TryAutoConnectLiquidCoolingAsync(CancellationToken.None);
    }

    private async void PrimeLiquidCooling_Click(object sender, RoutedEventArgs e) =>
        await ViewModel.StartLiquidCoolingPrimingAsync(CancellationToken.None);

    private async void ResetLiquidCooling_Click(object sender, RoutedEventArgs e) =>
        await ViewModel.ResetLiquidCoolingDeviceAsync(CancellationToken.None);

    private async void BatteryMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string value } && int.TryParse(value, out var mode))
            await ViewModel.SetBatteryProtectionAsync((BatteryProtectionMode)mode, CancellationToken.None);
    }

    private async void PowerToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { Tag: string value } checkBox &&
            Enum.TryParse<PowerToggleControl>(value, out var control))
            await ViewModel.SetPowerToggleAsync(control, checkBox.IsChecked == true, CancellationToken.None);
    }

    private async void DeepSleep_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox)
            await ViewModel.SetDeepSleepAsync(checkBox.IsChecked == true, CancellationToken.None);
    }

    private async void ScheduleShutdown_Click(object sender, RoutedEventArgs e) =>
        await ViewModel.ScheduleShutdownAsync(CancellationToken.None);

    private async void CancelShutdown_Click(object sender, RoutedEventArgs e) =>
        await ViewModel.CancelShutdownAsync(CancellationToken.None);

    private async void FanBoost_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox checkBox) return;
        await ViewModel.SetFanBoostAsync(checkBox.IsChecked == true, CancellationToken.None);
        checkBox.GetBindingExpression(CheckBox.IsCheckedProperty)?.UpdateTarget();
    }

    private void ShutdownCountdownTimer_Tick(object? sender, EventArgs e) =>
        ViewModel.RefreshShutdownCountdown();

    private void UpdateShutdownCountdownTimer()
    {
        if (IsVisible && WindowState != WindowState.Minimized &&
            ViewModel.ActiveDrawer == HomeDrawer.Other && ViewModel.IsShutdownScheduled)
        {
            ViewModel.RefreshShutdownCountdown();
            shutdownCountdownTimer.Start();
        }
        else shutdownCountdownTimer.Stop();
    }

    private void BatteryChargeLimit_Commit(object sender, MouseButtonEventArgs e) =>
        QueueBatteryChargeLimitAutoApply();

    private void BatteryChargeLimit_KeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End)
            QueueBatteryChargeLimitAutoApply();
    }

    private void QueueBatteryChargeLimitAutoApply()
    {
        if (!IsLoaded) return;
        var version = Interlocked.Increment(ref batteryAutoApplyVersion);
        var percent = ViewModel.BatteryChargeLimitPercent;
        _ = ApplyLatestBatteryChargeLimitAsync(version, percent);
    }

    private async Task ApplyLatestBatteryChargeLimitAsync(long version, int percent)
    {
        await batteryAutoApplyLock.WaitAsync();
        try
        {
            if (version == Volatile.Read(ref batteryAutoApplyVersion))
                await ViewModel.SetBatteryChargeLimitAsync(percent, CancellationToken.None);
        }
        finally
        {
            batteryAutoApplyLock.Release();
        }
    }

    private async void DisplayColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string value } && int.TryParse(value, out var mode))
            await ViewModel.SetDisplayColorAsync((DisplayColorMode)mode, CancellationToken.None);
    }

    private void DisplaySettingsTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string value } && int.TryParse(value, out var page))
            ViewModel.SelectDisplaySettingsPage((DisplaySettingsPage)page);
    }

    private async void DisplayColorManagement_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox)
            await ViewModel.SetDisplayColorManagementAsync(checkBox.IsChecked == true, CancellationToken.None);
    }

    private async void MiniLedLocalDimming_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox)
            await ViewModel.SetMiniLedLocalDimmingAsync(checkBox.IsChecked == true, CancellationToken.None);
    }

    private async void AutomaticRefreshRate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox)
        {
            await ViewModel.SetAutomaticRefreshRateAsync(checkBox.IsChecked == true, CancellationToken.None);
            var source = System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus;
            if (source != System.Windows.Forms.PowerLineStatus.Unknown)
                await ViewModel.ApplyAutomaticRefreshRateForPowerSourceAsync(
                    source == System.Windows.Forms.PowerLineStatus.Online, CancellationToken.None);
        }
    }

    private void DisplayRefreshRate_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Collection resets can temporarily select null; only a real rate updates the draft.
        if (sender is ComboBox { SelectedItem: int rate })
            ViewModel.SelectedDisplayRefreshRateHz = rate;
    }

    private async void ApplyDisplayRefreshRate_Click(object sender, RoutedEventArgs e) =>
        await ViewModel.ApplyDisplayRefreshRateAsync(CancellationToken.None);

    private void GpuOutputMode_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || sender is not FrameworkElement { Tag: string mode }) return;
        ViewModel.SelectedGpuOutputMode = mode switch
        {
            "Discrete" => RevoHelper.Core.Control.GpuOutputMode.Discrete,
            "Integrated" => RevoHelper.Core.Control.GpuOutputMode.Integrated,
            _ => RevoHelper.Core.Control.GpuOutputMode.Hybrid
        };
    }

    private async void ApplyGpuOutputMode_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.ApplyGpuOutputModeAsync(CancellationToken.None);
        SynchronizeGpuOutputButtons();
    }

    private async void FanCurve_Click(object sender, RoutedEventArgs e)
    {
        var initialization = ViewModel.OpenFanCurveAsync(CancellationToken.None);
        OpenDrawer();
        await initialization;
    }

    private async void Performance_Click(object sender, RoutedEventArgs e)
    {
        var initialization = ViewModel.OpenPerformanceAsync(CancellationToken.None);
        OpenDrawer();
        await initialization;
    }

    private void PerformanceBoost_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsUserComboBoxSelection(sender)) QueuePerformanceAutoApply();
    }

    private void PerformanceToggle_Click(object sender, RoutedEventArgs e) => QueuePerformanceAutoApply();

    private void PerformanceSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (IsLoaded && sender is Slider slider && (slider.IsMouseCaptureWithin || slider.IsKeyboardFocusWithin))
            QueuePerformanceAutoApply(TimeSpan.FromMilliseconds(320));
    }

    private void PerformanceEditor_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        QueuePerformanceAutoApply(TimeSpan.FromMilliseconds(120));

    private void PerformanceEditor_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        QueuePerformanceAutoApply();
        Keyboard.ClearFocus();
        e.Handled = true;
    }

    private void QueuePerformanceAutoApply(TimeSpan? delay = null)
    {
        if (!IsLoaded) return;
        var version = Interlocked.Increment(ref performanceAutoApplyVersion);
        var profileId = ViewModel.SelectedProfileId;
        var turboStrategy = ViewModel.SelectedTurboStrategy;
        var customProfileIndex = ViewModel.SelectedCustomProfileIndex;
        _ = ApplyLatestPerformancePolicyAsync(
            version, profileId, turboStrategy, customProfileIndex, delay ?? TimeSpan.Zero);
    }

    private async Task ApplyLatestPerformancePolicyAsync(
        long version,
        string profileId,
        TurboStrategy turboStrategy,
        int customProfileIndex,
        TimeSpan delay)
    {
        if (delay > TimeSpan.Zero) await Task.Delay(delay);
        if (version != Volatile.Read(ref performanceAutoApplyVersion)) return;
        await performanceAutoApplyLock.WaitAsync();
        try
        {
            if (version == Volatile.Read(ref performanceAutoApplyVersion) &&
                profileId == ViewModel.SelectedProfileId &&
                turboStrategy == ViewModel.SelectedTurboStrategy &&
                customProfileIndex == ViewModel.SelectedCustomProfileIndex &&
                ViewModel.IsPerformanceApplyEnabled)
                await ViewModel.ApplyPerformancePolicyAsync(CancellationToken.None);
        }
        finally
        {
            performanceAutoApplyLock.Release();
        }
    }

    private void CustomPerformanceToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { Tag: string tag } toggle && Enum.TryParse<CustomPerformanceSetting>(tag, out var setting))
            QueueCustomPerformanceAutoApply(setting, toggle.IsChecked == true ? 1 : 0);
    }

    private void CustomPerformanceSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded || !ViewModel.CanEditAdvancedPerformance || sender is not Slider { Tag: string tag } slider ||
            !Enum.TryParse<CustomPerformanceSetting>(tag, out var setting)) return;
        if (slider.IsMouseCaptureWithin || Mouse.LeftButton == MouseButtonState.Pressed)
        {
            if (pendingDraggedCustomSliders.Add(slider))
                Interlocked.Increment(ref customPerformanceAutoApplyVersion);
            return;
        }
        // Keyboard edits commit on key release. A focused slider receiving a programmatic
        // readback is not a new user command.
    }

    private readonly HashSet<Slider> pendingDraggedCustomSliders = [];

    private void CustomPerformanceSlider_MouseReleased(object sender, MouseButtonEventArgs e)
    {
        if (sender is Slider slider && pendingDraggedCustomSliders.Remove(slider)) CommitCustomPerformanceSlider(slider);
    }

    private void CustomPerformanceSlider_CaptureLost(object sender, MouseEventArgs e)
    {
        if (sender is Slider slider && !slider.IsMouseCaptureWithin && pendingDraggedCustomSliders.Remove(slider))
            CommitCustomPerformanceSlider(slider);
    }

    private void CommitCustomPerformanceSlider(Slider slider)
    {
        if (slider.Tag is string tag && Enum.TryParse<CustomPerformanceSetting>(tag, out var setting))
            QueueCustomPerformanceAutoApply(setting, (int)Math.Round(slider.Value));
    }

    private void CustomPerformanceSlider_KeyReleased(object sender, KeyEventArgs e)
    {
        if (sender is Slider slider && e.Key is Key.Left or Key.Right or Key.Up or Key.Down or
            Key.Home or Key.End or Key.PageUp or Key.PageDown)
            CommitCustomPerformanceSlider(slider);
    }

    private void GpuClockOffsetSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ViewModel.CanEditGpuClockOffsets) CustomPerformanceSlider_ValueChanged(sender, e);
    }

    private readonly CustomPerformanceEditBuffer pendingCustomPerformanceEdits = new();

    private void QueueCustomPerformanceAutoApply(CustomPerformanceSetting setting, int value, TimeSpan? delay = null)
    {
        if (!IsLoaded || !ViewModel.CanEditAdvancedPerformance) return;
        pendingCustomPerformanceEdits.Set(setting, value);
        var version = Interlocked.Increment(ref customPerformanceAutoApplyVersion);
        var profileIndex = ViewModel.SelectedCustomProfileIndex;
        _ = ApplyLatestCustomPerformanceAsync(version, profileIndex, delay ?? TimeSpan.Zero);
    }

    private async Task ApplyLatestCustomPerformanceAsync(long version, int profileIndex, TimeSpan delay)
    {
        if (delay > TimeSpan.Zero) await Task.Delay(delay);
        if (version != Volatile.Read(ref customPerformanceAutoApplyVersion)) return;
        await customPerformanceAutoApplyLock.WaitAsync();
        try
        {
            if (version == Volatile.Read(ref customPerformanceAutoApplyVersion) &&
                profileIndex == ViewModel.SelectedCustomProfileIndex &&
                ViewModel.CanApplyCustomPerformance)
            {
                while (version == Volatile.Read(ref customPerformanceAutoApplyVersion) &&
                    ViewModel.CanApplyCustomPerformance && pendingCustomPerformanceEdits.TryTakeNext(out var edit))
                {
                    if (!ViewModel.IsCustomProfileSelected || profileIndex != ViewModel.SelectedCustomProfileIndex) break;
                    await ViewModel.ApplyCustomPerformanceSettingAsync(edit.Key, edit.Value, CancellationToken.None);
                }
            }
        }
        finally
        {
            customPerformanceAutoApplyLock.Release();
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.OpenSettings();
        OpenDrawer();
    }

    private async void StopSelectedConflictServices_Click(object sender, RoutedEventArgs e) =>
        await ViewModel.StopSelectedConflictServicesAsync(CancellationToken.None);

    private async void RestoreSelectedConflictServices_Click(object sender, RoutedEventArgs e) =>
        await ViewModel.RestoreSelectedConflictServicesAsync(CancellationToken.None);

    private void CpuFan_Checked(object sender, RoutedEventArgs e)
    {
        if (IsLoaded) ViewModel.SelectFanChannel(true);
    }

    private void GpuFan_Checked(object sender, RoutedEventArgs e)
    {
        if (IsLoaded) ViewModel.SelectFanChannel(false);
    }

    private async void RestoreOemFanCurve_Click(object sender, RoutedEventArgs e)
    {
        Interlocked.Increment(ref fanAutoApplyVersion);
        await fanAutoApplyLock.WaitAsync();
        try
        {
            await ViewModel.RestoreOemFanCurveAsync(CancellationToken.None);
        }
        finally
        {
            fanAutoApplyLock.Release();
        }
    }

    private void FanCurveEditor_Commit(object sender, MouseButtonEventArgs e) => QueueFanAutoApply();

    private void FanPointEditor_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        QueueFanAutoApply(TimeSpan.FromMilliseconds(120));

    private void FanPointEditor_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        QueueFanAutoApply();
        Keyboard.ClearFocus();
        e.Handled = true;
    }

    private void FanResponseToggle_Click(object sender, RoutedEventArgs e) => QueueFanAutoApply();

    private void FanResponseSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (IsLoaded && sender is Slider slider && (slider.IsMouseCaptureWithin || slider.IsKeyboardFocusWithin))
            QueueFanAutoApply(TimeSpan.FromMilliseconds(320));
    }

    private void QueueFanAutoApply(TimeSpan? delay = null)
    {
        if (!IsLoaded || !ViewModel.IsCustomProfileSelected) return;
        var version = Interlocked.Increment(ref fanAutoApplyVersion);
        var profileId = ViewModel.SelectedProfileId;
        var turboStrategy = ViewModel.SelectedTurboStrategy;
        var customProfileIndex = ViewModel.SelectedCustomProfileIndex;
        _ = ApplyLatestFanCurveAsync(
            version, profileId, turboStrategy, customProfileIndex, delay ?? TimeSpan.Zero);
    }

    private async Task ApplyLatestFanCurveAsync(
        long version,
        string profileId,
        TurboStrategy turboStrategy,
        int customProfileIndex,
        TimeSpan delay)
    {
        if (delay > TimeSpan.Zero) await Task.Delay(delay);
        if (version != Volatile.Read(ref fanAutoApplyVersion)) return;
        await fanAutoApplyLock.WaitAsync();
        try
        {
            if (version == Volatile.Read(ref fanAutoApplyVersion) &&
                profileId == ViewModel.SelectedProfileId &&
                turboStrategy == ViewModel.SelectedTurboStrategy &&
                customProfileIndex == ViewModel.SelectedCustomProfileIndex &&
                ViewModel.CanEditFanSettings)
                await ViewModel.ApplyFanCurveAsync(CancellationToken.None);
        }
        finally
        {
            fanAutoApplyLock.Release();
        }
    }

    private void ResetPerformanceDraft_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ResetPerformanceDraft();
        QueuePerformanceAutoApply();
    }

    private void OpenDrawer()
    {
        AnnouncementsDrawerContent.Visibility = ViewModel.ActiveDrawer is HomeDrawer.Announcements
            ? Visibility.Visible : Visibility.Collapsed;
        PerformanceMonitorDrawerContent.Visibility = ViewModel.ActiveDrawer is HomeDrawer.PerformanceMonitor
            ? Visibility.Visible : Visibility.Collapsed;
        MoreProfilesDrawerContent.Visibility = ViewModel.ActiveDrawer is HomeDrawer.MoreProfiles
            ? Visibility.Visible : Visibility.Collapsed;
        PerformanceDrawerContent.Visibility = ViewModel.ActiveDrawer is HomeDrawer.Performance
            ? Visibility.Visible
            : Visibility.Collapsed;
        FanDrawerContent.Visibility = ViewModel.ActiveDrawer is HomeDrawer.Fan
            ? Visibility.Visible
            : Visibility.Collapsed;
        SettingsDrawerContent.Visibility = ViewModel.ActiveDrawer is HomeDrawer.Settings
            ? Visibility.Visible
            : Visibility.Collapsed;
        GpuOutputDrawerContent.Visibility = ViewModel.ActiveDrawer is HomeDrawer.GpuOutput
            ? Visibility.Visible
            : Visibility.Collapsed;
        LightingDrawerContent.Visibility = ViewModel.ActiveDrawer is HomeDrawer.Lighting
            ? Visibility.Visible
            : Visibility.Collapsed;
        LiquidCoolingDrawerContent.Visibility = ViewModel.ActiveDrawer is HomeDrawer.LiquidCooling
            ? Visibility.Visible
            : Visibility.Collapsed;
        KeyboardDrawerContent.Visibility = ViewModel.ActiveDrawer is HomeDrawer.Keyboard
            ? Visibility.Visible
            : Visibility.Collapsed;
        BatteryDrawerContent.Visibility = ViewModel.ActiveDrawer is HomeDrawer.Battery
            ? Visibility.Visible
            : Visibility.Collapsed;
        DisplayColorDrawerContent.Visibility = ViewModel.ActiveDrawer is HomeDrawer.DisplayColor
            ? Visibility.Visible
            : Visibility.Collapsed;
        OtherDrawerContent.Visibility = ViewModel.ActiveDrawer is HomeDrawer.Other
            ? Visibility.Visible
            : Visibility.Collapsed;
        PerformanceDrawerHeaderIcon.Visibility = ViewModel.ActiveDrawer is HomeDrawer.Performance
            ? Visibility.Visible
            : Visibility.Collapsed;
        PerformanceMonitorDrawerHeaderIcon.Visibility = ViewModel.ActiveDrawer is HomeDrawer.PerformanceMonitor
            ? Visibility.Visible : Visibility.Collapsed;
        FanDrawerHeaderIcon.Visibility = ViewModel.ActiveDrawer is HomeDrawer.Fan
            ? Visibility.Visible
            : Visibility.Collapsed;
        SettingsDrawerHeaderIcon.Visibility = ViewModel.ActiveDrawer is HomeDrawer.Settings or HomeDrawer.MoreProfiles or HomeDrawer.Announcements
            ? Visibility.Visible
            : Visibility.Collapsed;
        GpuOutputDrawerHeaderIcon.Visibility = ViewModel.ActiveDrawer is HomeDrawer.GpuOutput
            ? Visibility.Visible
            : Visibility.Collapsed;
        LightingDrawerHeaderIcon.Visibility = ViewModel.ActiveDrawer is HomeDrawer.Lighting
            ? Visibility.Visible
            : Visibility.Collapsed;
        KeyboardDrawerHeaderIcon.Visibility = ViewModel.ActiveDrawer is HomeDrawer.Keyboard
            ? Visibility.Visible
            : Visibility.Collapsed;
        BatteryDrawerHeaderIcon.Visibility = ViewModel.ActiveDrawer is HomeDrawer.Battery
            ? Visibility.Visible
            : Visibility.Collapsed;
        LiquidCoolingDrawerHeaderIcon.Visibility = ViewModel.ActiveDrawer is HomeDrawer.LiquidCooling
            ? Visibility.Visible
            : Visibility.Collapsed;
        DisplayDrawerHeaderIcon.Visibility = ViewModel.ActiveDrawer is HomeDrawer.DisplayColor
            ? Visibility.Visible
            : Visibility.Collapsed;
        OtherDrawerHeaderIcon.Visibility = ViewModel.ActiveDrawer is HomeDrawer.Other
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (DrawerPanel.Visibility is Visibility.Visible)
        {
            UpdateLiquidCoolingRefreshTimer();
            return;
        }

        DrawerPanel.Visibility = Visibility.Visible;
        ApplyWindowGeometry();
        UpdateLiquidCoolingRefreshTimer();
    }

    private void SynchronizeGpuOutputButtons()
    {
        if (GpuHybridRadio is null) return;
        GpuHybridRadio.IsChecked = ViewModel.SelectedGpuOutputMode is RevoHelper.Core.Control.GpuOutputMode.Hybrid;
        GpuDiscreteRadio.IsChecked = ViewModel.SelectedGpuOutputMode is RevoHelper.Core.Control.GpuOutputMode.Discrete;
        GpuIntegratedRadio.IsChecked = ViewModel.SelectedGpuOutputMode is RevoHelper.Core.Control.GpuOutputMode.Integrated;
    }

    private void CloseDrawer_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.CloseDrawer();
        DrawerPanel.Visibility = Visibility.Collapsed;
        ApplyWindowGeometry();
        UpdateLiquidCoolingRefreshTimer();
    }

    private async void LiquidCoolingRefreshTimer_Tick(object? sender, EventArgs e)
    {
        if (refreshingLiquidCooling)
        {
            return;
        }

        liquidCoolingRefreshTimer.Stop();
        refreshingLiquidCooling = true;
        try
        {
            if (ShouldRefreshLiquidCooling())
            {
                await ViewModel.RefreshLiquidCoolingStatusAsync(CancellationToken.None);
            }
        }
        finally
        {
            refreshingLiquidCooling = false;
            UpdateLiquidCoolingRefreshTimer();
        }
    }

    private bool ShouldRefreshLiquidCooling() =>
        IsVisible &&
        WindowState is not WindowState.Minimized &&
        DrawerPanel.Visibility is Visibility.Visible &&
        ViewModel.ActiveDrawer is HomeDrawer.LiquidCooling;

    private void UpdateLiquidCoolingRefreshTimer()
    {
        if (ShouldRefreshLiquidCooling() && !refreshingLiquidCooling)
        {
            if (!liquidCoolingRefreshTimer.IsEnabled)
            {
                liquidCoolingRefreshTimer.Start();
            }
        }
        else
        {
            liquidCoolingRefreshTimer.Stop();
        }
    }

    private void WindowScale_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel ||
            sender is not ComboBox selector || selector.SelectedItem is not int percent) return;
        viewModel.WindowScalePercent = percent;
        // One-way binding plus explicit refresh also rolls the selector back after a save failure.
        selector.GetBindingExpression(ComboBox.SelectedItemProperty)?.UpdateTarget();
    }

    private void ApplyWindowGeometry()
    {
        if (applyingWindowGeometry || DataContext is not MainWindowViewModel viewModel) return;
        applyingWindowGeometry = true;
        try
        {
            var workArea = GetCurrentMonitorWorkArea();
            var requestedScale = viewModel.WindowScalePercent / 100d;
            preferredMainPanelLeft ??= viewModel.RememberWindowPosition && viewModel.SavedWindowLeft is double savedLeft && double.IsFinite(savedLeft)
                ? savedLeft : workArea.Right - WindowScaleGeometry.MainWidth * requestedScale - 18;
            preferredWindowTop ??= viewModel.RememberWindowPosition && viewModel.SavedWindowTop is double savedTop && double.IsFinite(savedTop)
                ? savedTop : workArea.Bottom - WindowScaleGeometry.LogicalHeight * requestedScale - 18;
            var drawerOpen = DrawerPanel.Visibility is Visibility.Visible;
            windowLayout = WindowScaleGeometry.Calculate(viewModel.WindowScalePercent, drawerOpen, workArea,
                preferredMainPanelLeft.Value, preferredWindowTop.Value);
            DrawerColumn.Width = new GridLength(drawerOpen ? WindowScaleGeometry.DrawerWidth : 0);
            WindowLogicalRoot.Width = WindowScaleGeometry.MainWidth + (drawerOpen ? WindowScaleGeometry.DrawerWidth : 0);
            Width = windowLayout.Width;
            Height = windowLayout.Height;
            Left = windowLayout.Left;
            Top = windowLayout.Top;
            WindowScaleHint.Text = viewModel.SelectedUiLanguageCode == "en-US"
                ? windowLayout.IsCapped
                    ? $"Saved {viewModel.WindowScalePercent}%; using {windowLayout.EffectiveScale * 100:0.#}% to fit this screen."
                    : "Applies immediately; default is 100%."
                : windowLayout.IsCapped
                    ? $"已保存 {viewModel.WindowScalePercent}%；当前屏幕空间不足，暂按 {windowLayout.EffectiveScale * 100:0.#}% 显示。"
                    : "选择后立即生效，默认 100%。";
        }
        finally { applyingWindowGeometry = false; }
    }

    private void Window_LocationChanged(object? sender, EventArgs e)
    {
        if (applyingWindowGeometry || !IsLoaded || windowLayout is null || WindowState is not WindowState.Normal) return;
        preferredMainPanelLeft = Left + windowLayout.DrawerOffset;
        preferredWindowTop = Top;
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(ApplyWindowGeometry));
    }

    private IntPtr WindowMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Work-area changes (including taskbar changes) are event-driven, never telemetry-polled.
        if (message is 0x007E or 0x001A)
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(ApplyWindowGeometry));
        return IntPtr.Zero;
    }

    private Rect GetCurrentMonitorWorkArea()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var handle = new WindowInteropHelper(this).Handle;
        var initialLeft = ViewModel.RememberWindowPosition && ViewModel.SavedWindowLeft is double savedLeft && double.IsFinite(savedLeft)
            ? savedLeft : SystemParameters.WorkArea.Left;
        var initialTop = ViewModel.RememberWindowPosition && ViewModel.SavedWindowTop is double savedTop && double.IsFinite(savedTop)
            ? savedTop : SystemParameters.WorkArea.Top;
        var monitor = handle != IntPtr.Zero
            ? MonitorFromWindow(handle, 2)
            : MonitorFromPoint(new NativePoint(
                (int)(initialLeft * dpi.DpiScaleX), (int)(initialTop * dpi.DpiScaleY)), 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return SystemParameters.WorkArea;
        return new Rect(info.Work.Left / dpi.DpiScaleX, info.Work.Top / dpi.DpiScaleY,
            (info.Work.Right - info.Work.Left) / dpi.DpiScaleX, (info.Work.Bottom - info.Work.Top) / dpi.DpiScaleY);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativePoint(int x, int y) { public readonly int X = x; public readonly int Y = y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public NativeRect Monitor; public NativeRect Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(CancelEventArgs e)
    {
        var mainPanelLeft = Left + (windowLayout?.DrawerOffset ?? 0);
        ViewModel.SaveWindowPosition(mainPanelLeft, Top);
        if (!AllowApplicationClose)
        {
            e.Cancel = true;
            liquidCoolingRefreshTimer.Stop();
            Hide();
            return;
        }
        liquidCoolingRefreshTimer.Stop();
        liquidCoolingRefreshTimer.Tick -= LiquidCoolingRefreshTimer_Tick;
        shutdownCountdownTimer.Stop();
        shutdownCountdownTimer.Tick -= ShutdownCountdownTimer_Tick;
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        Interlocked.Increment(ref customPerformanceAutoApplyVersion);
        pendingCustomPerformanceEdits.Clear();
        pendingDraggedCustomSliders.Clear();
        windowSource?.RemoveHook(WindowMessageHook);
        LocationChanged -= Window_LocationChanged;
        base.OnClosing(e);
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.SelectedUiThemeCode))
        {
            AppTheme.Apply(Resources, ViewModel.SelectedUiThemeCode);
            SynchronizeLightingZoneButtons();
        }
        if (e.PropertyName == nameof(MainWindowViewModel.SelectedUiLanguageCode))
            ApplyWindowGeometry();
        if (e.PropertyName is nameof(MainWindowViewModel.ActiveDrawer) or nameof(MainWindowViewModel.IsShutdownScheduled))
            UpdateShutdownCountdownTimer();
        if (e.PropertyName == nameof(MainWindowViewModel.AlwaysOnTop))
        {
            Topmost = ViewModel.AlwaysOnTop;
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.WindowScalePercent))
        {
            ApplyWindowGeometry();
        }
        else if (e.PropertyName is nameof(MainWindowViewModel.SelectedProfileId) or
                 nameof(MainWindowViewModel.SelectedTurboStrategy) or
                 nameof(MainWindowViewModel.SelectedCustomProfileIndex))
        {
            Interlocked.Increment(ref performanceAutoApplyVersion);
            Interlocked.Increment(ref customPerformanceAutoApplyVersion);
            pendingCustomPerformanceEdits.Clear();
            pendingDraggedCustomSliders.Clear();
            Interlocked.Increment(ref fanAutoApplyVersion);
            SynchronizeProfileButtons();
            UpdateRadar();
        }
    }

    private void SynchronizeProfileButtons()
    {
        if (!IsInitialized) return;
        synchronizingProfileButtons = true;
        try
        {
            OfficeProfileRadio.IsChecked = ViewModel.SelectedProfileId == "office";
            BalancedProfileRadio.IsChecked = ViewModel.SelectedProfileId == "balanced";
            TurboProfileRadio.IsChecked = ViewModel.SelectedProfileId == "turbo";
            CustomProfileRadio.IsChecked = ViewModel.SelectedProfileId == "custom";
            CustomProfileOneRadio.IsChecked = ViewModel.SelectedCustomProfileIndex == 1;
            CustomProfileTwoRadio.IsChecked = ViewModel.SelectedCustomProfileIndex == 2;
            CustomProfileThreeRadio.IsChecked = ViewModel.SelectedCustomProfileIndex == 3;
            TurboQuietRadio.IsChecked = ViewModel.SelectedTurboStrategy is TurboStrategy.Quiet;
            TurboHighRadio.IsChecked = ViewModel.SelectedTurboStrategy is TurboStrategy.HighEfficiency;
        }
        finally
        {
            synchronizingProfileButtons = false;
        }
    }

    private void UpdateRadar()
    {
        var visible = ViewModel.IsTradeoffVisible;
        TradeoffPolygon.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        TradeoffCenter.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

        if (!visible)
        {
            return;
        }

        var tradeoff = ViewModel.Tradeoff;
        var displayedPerformance = ViewModel.SelectedProfileId == "balanced"
            ? Math.Max(0, tradeoff.Performance - 8)
            : tradeoff.Performance;
        var center = new Point(75, 64);
        TradeoffPolygon.Points =
        [
            Scale(center, new Point(75, 10), tradeoff.Noise),
            Scale(center, new Point(20, 92), displayedPerformance),
            Scale(center, new Point(130, 92), tradeoff.Temperature)
        ];

        var (stroke, fill) = ViewModel.SelectedProfileId switch
        {
            "office" => (Color.FromRgb(31, 176, 96), Color.FromRgb(145, 231, 180)),
            "balanced" => (Color.FromRgb(33, 133, 244), Color.FromRgb(145, 197, 255)),
            "turbo" => (Color.FromRgb(147, 57, 219), Color.FromRgb(202, 142, 245)),
            _ => (Color.FromRgb(125, 141, 165), Color.FromRgb(207, 217, 231))
        };

        TradeoffPolygon.Stroke = new SolidColorBrush(stroke);
        TradeoffPolygon.Fill = new SolidColorBrush(fill);
        TradeoffCenter.Fill = new SolidColorBrush(stroke);
    }

    private static Point Scale(Point center, Point vertex, int percentage)
    {
        // A small visual floor keeps low values legible in the compact preview.
        var factor = 0.25 + (percentage / 100d * 0.75);
        return new(
            center.X + ((vertex.X - center.X) * factor),
            center.Y + ((vertex.Y - center.Y) * factor));
    }
}
