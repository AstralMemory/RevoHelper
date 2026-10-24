using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using RevoHelper.App.Presentation;
using RevoHelper.App.Settings;
using RevoHelper.Core.Control;
using RevoHelper.Core.Dashboard;
using RevoHelper.Core.Fans;
using RevoHelper.Core.Hardware;

namespace RevoHelper.App.Tests.Presentation;

public sealed class WindowScaleVisualTests
{
    [Fact]
    public void FinalCopy_StockFanNoticeUsesPlainLanguageAndHidesProtocolDetails()
    {
        RunOnSta(() =>
        {
            var vm = CreateOfflineViewModel();
            vm.SelectProfile("office");
            var points = new[] { new FanCurvePoint(0, 40, 35, 10), new FanCurvePoint(1, 85, 80, 100) };
            vm.ApplyFanTable(new FanCurveTable("M2T1", true, points, points));
            vm.OpenFanCurve();
            var window = new MainWindow(vm) { AllowApplicationClose = true };
            try
            {
                var (viewport, size) = ArrangeOfflineDrawer(window, vm, 100);
                var notice = Assert.IsType<TextBlock>(window.FindName("FanEditingAvailabilityText"));
                var temperature = Assert.IsType<TextBlock>(window.FindName("FanCurveTemperatureHint"));
                Assert.Equal("官方的预设通常无法调整风扇曲线，所以仅作参考。", notice.Text);
                Assert.Equal("已读取官方风扇设置", vm.FanStatusText);
                Assert.DoesNotContain("M2T1", vm.FanStatusText);
                Assert.Equal(Visibility.Collapsed, temperature.Visibility);
                Assert.False(vm.CanEditFanSettings);
                SaveOfflineImage(viewport, size, "final-copy-stock-fan");
                vm.SelectProfile("custom");
                vm.ApplyFanTable(new FanCurveTable("Profile1", true, points, points));
                FlushLayout(viewport);
                Assert.Equal(Visibility.Visible, temperature.Visibility);
                Assert.Equal("CPU 曲线最高温度：85℃", temperature.Text);
                Assert.DoesNotContain("255", temperature.Text);
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData(70)]
    [InlineData(100)]
    [InlineData(130)]
    public void Prerelease_FanResponseAndModeTintedUndatedBadgeFitWithoutHardwareWrites(int percent)
    {
        RunOnSta(() =>
        {
            var vm = CreateOfflineViewModel();
            vm.SelectProfile("custom");
            vm.FanTransitionSensitivity = 5000;
            vm.OpenFanCurve();
            var window = new MainWindow(vm) { AllowApplicationClose = true };
            try
            {
                var (viewport, size) = ArrangeOfflineDrawer(window, vm, percent);
                var scroll = Assert.IsType<ScrollViewer>(window.FindName("FanDrawerContent"));
                var response = Assert.IsType<Border>(window.FindName("FanResponseControls"));
                var independent = Assert.IsType<CheckBox>(window.FindName("FanIndependentOutputToggle"));
                var slider = Assert.IsType<Slider>(window.FindName("FanTransitionSensitivitySlider"));
                var value = Assert.IsType<TextBlock>(window.FindName("FanTransitionSensitivityValue"));
                FlushLayout(viewport);
                Assert.Equal("5000 ms/0.5%", value.Text);
                Assert.Equal(5000, slider.Value);
                AssertInsideScroll(response, scroll, viewport);
                AssertInsideScroll(independent, scroll, viewport);
                Assert.Equal(5000, slider.Maximum);
                Assert.Equal(100, slider.LargeChange);
                Assert.True(slider.ActualWidth > 70, "The longest delay label must leave a usable slider.");
                var valueBounds = value.TransformToAncestor(response).TransformBounds(new Rect(value.RenderSize));
                Assert.True(valueBounds.Right <= response.ActualWidth - 4, "Delay units must stay inside their card.");
                AssertSeparateFooter(window, scroll, viewport, size);
                vm.FanTransitionSensitivity = 24;
                FlushLayout(viewport);
                Assert.Equal(24, slider.Value);
                Assert.Equal(24, vm.FanTransitionSensitivity);
                vm.FanTransitionSensitivity = 5000;
                FlushLayout(viewport);

                var badge = Assert.IsType<Border>(window.FindName("ReleaseStatusBadge"));
                var version = Assert.IsType<TextBlock>(window.FindName("ReleaseStatusText"));
                Assert.Equal("V1.0.0", version.Text);
                Assert.Equal(UiFontExtension.Grow(10), version.FontSize);
                Assert.Equal(Visibility.Collapsed, Assert.IsType<TextBlock>(window.FindName("HomeVersionFooter")).Visibility);
                SaveOfflineImage(viewport, size, $"prerelease-fan-response-{percent}");
                foreach (var profile in new[] { "office", "balanced", "turbo", "custom" })
                {
                    vm.SelectProfile(profile);
                    FlushLayout(viewport);
                    Assert.Equal((Color)ColorConverter.ConvertFromString(vm.PerformanceAccentSurfaceColor),
                        Assert.IsType<SolidColorBrush>(badge.Background).Color);
                    Assert.Equal((Color)ColorConverter.ConvertFromString(vm.PerformanceAccentColor),
                        Assert.IsType<SolidColorBrush>(version.Foreground).Color);
                    var textBounds = version.TransformToAncestor(badge).TransformBounds(new Rect(version.RenderSize));
                    Assert.True(textBounds.Left >= 0 && textBounds.Right <= badge.ActualWidth);
                }
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void September28_GpuSuperPerformanceEnablesEveryGpuControlWithoutHardwareWrites()
    {
        RunOnSta(() =>
        {
            var controller = new OfflineGpuPerformance();
            var vm = new MainWindowViewModel(HomeDashboardFactory.Create(
                new MachineIdentity("MECHREVO", "YAOSHI", "Offline", "Core Ultra 9 275HX", "test", 24, 24),
                ["Intel Graphics", "NVIDIA GeForce RTX 5090 Laptop GPU"]),
                new(), new MemorySettings(), new NoStartup(), customPerformanceController: controller,
                lightingProfileStore: new NoLightingProfiles());
            vm.SelectProfile("custom");
            vm.OpenPerformanceAsync(default).GetAwaiter().GetResult();
            var window = new MainWindow(vm) { AllowApplicationClose = true };
            try
            {
                var (viewport, size) = ArrangeOfflineDrawer(window, vm, 100);
                var core = Assert.IsType<Slider>(window.FindName("GpuCoreOffsetSlider"));
                var memory = Assert.IsType<Slider>(window.FindName("GpuMemoryOffsetSlider"));
                var tgp = Assert.IsType<Slider>(window.FindName("GpuTgpSlider"));
                Assert.True(core.IsEnabled);
                Assert.True(tgp.IsEnabled);
                Assert.Equal(-300, core.Minimum);
                Assert.Equal(-1000, memory.Minimum);
                Assert.Equal(0, controller.Writes);
                Slider.IncreaseLarge.Execute(null, core);
                Assert.Equal(120, core.Value);
                Slider.DecreaseLarge.Execute(null, core);
                Assert.Equal(100, core.Value);
                memory.SetCurrentValue(Slider.ValueProperty, 0d);
                Slider.DecreaseLarge.Execute(null, memory);
                Assert.Equal(-20, memory.Value);
                Slider.IncreaseLarge.Execute(null, memory);
                Assert.Equal(0, memory.Value);
                vm.CustomGpuTgpWatts = 130;
                Slider.IncreaseLarge.Execute(null, tgp);
                Assert.Equal(135, tgp.Value);
                Slider.DecreaseLarge.Execute(null, tgp);
                Assert.Equal(130, tgp.Value);
                var scroll = Assert.IsType<ScrollViewer>(window.FindName("PerformanceDrawerContent"));
                scroll.ScrollToEnd();
                FlushLayout(viewport);
                SaveOfflineImage(viewport, size, "beta028-gpu-controls-on");

                vm.CustomOverclockingEnabled = false;
                FlushLayout(viewport);
                Assert.False(core.IsEnabled);
                Assert.False(tgp.IsEnabled);
                SaveOfflineImage(viewport, size, "beta028-gpu-controls-off");
                Assert.Equal(0, controller.Writes);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void UnsetShutdownUsesSmallerSimplifiedChineseWhileCountdownKeepsItsSize()
    {
        RunOnSta(() =>
        {
            var vm = CreateOfflineViewModel();
            vm.OpenOtherAsync(CancellationToken.None).GetAwaiter().GetResult();
            var window = new MainWindow(vm) { AllowApplicationClose = true };
            try
            {
                var (viewport, size) = ArrangeOfflineDrawer(window, vm, 100);
                var text = Assert.IsType<TextBlock>(window.FindName("ShutdownCountdownText"));
                Assert.Equal("未设置", text.Text);
                Assert.Equal(UiFontExtension.Grow(13), text.FontSize);
                Assert.Equal("Microsoft YaHei UI", text.FontFamily.Source);
                Assert.Equal("zh-CN", text.Language.IetfLanguageTag, ignoreCase: true);
                SaveOfflineImage(viewport, size, "beta022-other-polish");
                text.DataContext = new { IsShutdownScheduled = true, ShutdownCountdownText = "00:30:00" };
                FlushLayout(viewport);
                Assert.Equal(UiFontExtension.Grow(16), text.FontSize);
                Assert.Equal("Consolas", text.FontFamily.Source);
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData(70)]
    [InlineData(100)]
    [InlineData(130)]
    public void September22_RadarAndGpuWarningsFitWithoutHardwareWrites(int percent)
    {
        RunOnSta(() =>
        {
            var controller = new OfflineGpuPerformance();
            var vm = new MainWindowViewModel(HomeDashboardFactory.Create(
                new MachineIdentity("MECHREVO", "YAOSHI", "Offline", "Core Ultra 9 275HX", "test", 24, 24),
                ["Intel Graphics", "NVIDIA GeForce RTX 5090 Laptop GPU"]),
                new(), new MemorySettings(), new NoStartup(), customPerformanceController: controller,
                lightingProfileStore: new NoLightingProfiles());
            vm.SelectProfile("custom");
            vm.OpenPerformanceAsync(default).GetAwaiter().GetResult();
            var window = new MainWindow(vm) { AllowApplicationClose = true };
            try
            {
                var (viewport, size) = ArrangeOfflineDrawer(window, vm, percent);
                var panel = Assert.IsType<Border>(window.FindName("RadarPanel"));
                foreach (var name in new[] { "RadarPerformanceLabel", "RadarTemperatureLabel" })
                {
                    var label = Assert.IsType<TextBlock>(window.FindName(name));
                    var bounds = label.TransformToAncestor(panel).TransformBounds(new Rect(label.RenderSize));
                    Assert.True(bounds.Bottom <= panel.ActualHeight - 3, "Radar labels need space above the panel border.");
                }
                var core = Assert.IsType<Slider>(window.FindName("GpuCoreOffsetSlider"));
                var memory = Assert.IsType<Slider>(window.FindName("GpuMemoryOffsetSlider"));
                Assert.True(core.IsEnabled);
                Assert.Equal(250, core.Maximum);
                Assert.Equal(1500, memory.Maximum);
                var scroll = Assert.IsType<ScrollViewer>(window.FindName("PerformanceDrawerContent"));
                Assert.IsType<Border>(window.FindName("CustomGpuAdvancedCard")).BringIntoView();
                FlushLayout(viewport);
                foreach (var name in new[] { "GpuCoreOffsetWarning", "GpuMemoryOffsetWarning" })
                {
                    var warning = Assert.IsType<TextBlock>(window.FindName(name));
                    Assert.Equal(Color.FromRgb(0xD9, 0x36, 0x3E), Assert.IsType<SolidColorBrush>(warning.Foreground).Color);
                    AssertInsideScroll(warning, scroll, viewport);
                }
                SaveOfflineImage(viewport, size, $"beta022-gpu-{percent}");
                vm.SelectProfile("balanced");
                vm.OpenMoreProfiles();
                typeof(MainWindow).GetMethod("OpenDrawer", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, null);
                var rules = Assert.IsType<TextBlock>(window.FindName("DynamicPowerRulesText"));
                var help = Assert.IsType<Border>(window.FindName("DynamicPowerHelpIcon"));
                Assert.Same(Assert.IsType<ToolTip>(window.FindName("DynamicPowerHelpTooltip")), help.ToolTip);
                FlushLayout(viewport);
                Assert.InRange(rules.Text.Length, 1, 30);
                SaveOfflineImage(viewport, size, $"beta022-more-profiles-{percent}");
                Assert.Equal(0, controller.Writes);
            }
            finally { window.Close(); }
        });
    }

    private sealed class OfflineGpuPerformance : ICustomPerformanceController
    {
        public int Writes { get; private set; }
        public ValueTask<CustomPerformanceState?> ReadAsync(int index, CancellationToken token) =>
            ValueTask.FromResult<CustomPerformanceState?>(new(true, false, 95, 145, false, 150, true, 25, 100, 500));
        public ValueTask<GpuPerformanceState?> ReadGpuAsync(CancellationToken token) =>
            ValueTask.FromResult<GpuPerformanceState?>(null);
        public ValueTask<GpuClockOffsetState?> ReadGpuClockOffsetsAsync(CancellationToken token) =>
            ValueTask.FromResult<GpuClockOffsetState?>(new(true, true, new(100, 500), new(-300, 250, -1000, 1500), new(95, 150, 5, 25)));
        public ValueTask<ControlResult> ApplyAsync(int index, CustomPerformanceState state, CancellationToken token)
        { Writes++; return ValueTask.FromResult(ControlResult.Rejected("Offline")); }
        public ValueTask<ControlResult> ApplyGpuAsync(GpuPerformanceState state, CancellationToken token)
        { Writes++; return ValueTask.FromResult(ControlResult.Rejected("Offline")); }
        public ValueTask<ControlResult> ApplyGpuClockOffsetsAsync(GpuClockOffsets offsets, CancellationToken token)
        { Writes++; return ValueTask.FromResult(ControlResult.Rejected("Offline")); }
        public ValueTask<ControlResult> ApplySettingAsync(CustomPerformanceSetting setting, int value, CancellationToken token)
        { Writes++; return ValueTask.FromResult(ControlResult.Rejected("Offline")); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConfirmedModeCapabilitiesControlCardVisibilityWithoutChangingMode(bool twoModes)
    {
        RunOnSta(() =>
        {
            var vm = CreateOfflineViewModel();
            vm.ApplyRuntimeCapabilities(new(vm.Dashboard.Identity,
                new Dictionary<HardwareFeature, HardwareFeatureCapability>(),
                twoModes ? new HashSet<PlatformMode> { PlatformMode.Office, PlatformMode.Balanced }
                    : Enum.GetValues<PlatformMode>().ToHashSet(), []));
            vm.OpenSettings();
            var window = new MainWindow(vm) { AllowApplicationClose = true };
            try
            {
                var (viewport, size) = ArrangeOfflineDrawer(window, vm, 100);
                var office = Assert.IsType<RadioButton>(window.FindName("OfficeProfileRadio"));
                var balanced = Assert.IsType<RadioButton>(window.FindName("BalancedProfileRadio"));
                var turbo = Assert.IsType<RadioButton>(window.FindName("TurboProfileRadio"));
                var custom = Assert.IsType<RadioButton>(window.FindName("CustomProfileRadio"));
                Assert.Equal(twoModes ? "平衡模式" : "办公模式", office.Content);
                Assert.Equal(twoModes ? "性能模式" : "平衡模式", balanced.Content);
                Assert.Equal(twoModes ? Visibility.Collapsed : Visibility.Visible, ((Grid)turbo.Parent).Visibility);
                Assert.Equal(twoModes ? Visibility.Collapsed : Visibility.Visible, ((Grid)custom.Parent).Visibility);
                Assert.Equal(twoModes ? 2 : 1, Grid.GetRowSpan(office));
                Assert.Equal(office.ActualHeight, balanced.ActualHeight);
                Assert.True(office.ActualWidth > 100);
                Assert.Equal("balanced", vm.SelectedProfileId);
                SaveOfflineImage(viewport, size, twoModes ? "beta020-two-modes" : "beta020-full-modes");
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void RefreshRate_RepeatedReadAndTopologyChangeKeepSelectionWithoutValidationErrors()
    {
        RunOnSta(() =>
        {
            var display = new OfflineDisplay();
            var vm = new MainWindowViewModel(HomeDashboardFactory.Create(
                new MachineIdentity("MECHREVO", "YAOSHI Series", "Offline", "CPU", "test", 24, 24),
                ["Intel Graphics"]), new(), new MemorySettings(), new NoStartup(),
                displayController: display, lightingProfileStore: new NoLightingProfiles());
            vm.OpenDisplayAsync(CancellationToken.None).GetAwaiter().GetResult();
            var window = new MainWindow(vm) { AllowApplicationClose = true };
            try
            {
                var (viewport, size) = ArrangeOfflineDrawer(window, vm, 100);
                var selector = Assert.IsType<ComboBox>(window.FindName("DisplayRefreshRateSelector"));
                Assert.Equal(240, selector.SelectedItem);
                var changes = 0;
                vm.DisplayRefreshRates.CollectionChanged += (_, _) => changes++;
                vm.OpenDisplayAsync(CancellationToken.None).GetAwaiter().GetResult();
                FlushLayout(viewport);
                Assert.False(Validation.GetHasError(selector));
                Assert.Equal(240, selector.SelectedItem);
                Assert.Equal(0, changes);
                display.Snapshot = display.Snapshot with { AvailableRefreshRatesHz = new[] { 60, 120, 240 } };
                vm.OpenDisplayAsync(CancellationToken.None).GetAwaiter().GetResult();
                FlushLayout(viewport);
                Assert.False(Validation.GetHasError(selector));
                Assert.Equal(240, selector.SelectedItem);
                selector.SetCurrentValue(ComboBox.SelectedItemProperty, 120);
                FlushLayout(viewport);
                Assert.Equal(120, vm.SelectedDisplayRefreshRateHz);
                Assert.Equal(0, display.Writes);
                SaveOfflineImage(viewport, size, "beta008-refresh-rate");
            }
            finally { window.Close(); }
        });
    }

    private sealed class OfflineDisplay : IDisplayController
    {
        public DisplaySnapshot Snapshot { get; set; } = new("DISPLAY1", "内置显示器", 2560, 1600, 240, [60, 240, 300]);
        public int Writes { get; private set; }
        public ValueTask<ControlResult> SetTemporaryRefreshRateAsync(int rate, CancellationToken cancellationToken) =>
            SetRefreshRateAsync(rate, cancellationToken);
        public ValueTask<ControlResult> RestoreTemporaryRefreshRateAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(new ControlResult(true, "Offline"));
        public ValueTask<DisplaySnapshot> ReadAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Snapshot);
        public ValueTask<ControlResult> SetRefreshRateAsync(int refreshRateHz, CancellationToken cancellationToken)
        {
            Writes++;
            return ValueTask.FromResult(new ControlResult(true, "Offline"));
        }
    }

    [Theory]
    [InlineData(70, false)]
    [InlineData(100, false)]
    [InlineData(130, false)]
    [InlineData(70, true)]
    [InlineData(100, true)]
    [InlineData(130, true)]
    public void ExperimentalDrawers_RenderRealMarkupAtAllScaleExtremes(int percent, bool water)
    {
        RunOnSta(() =>
        {
            var vm = CreateOfflineViewModel();
            if (water) vm.OpenLiquidCoolingAsync(CancellationToken.None).GetAwaiter().GetResult();
            else vm.OpenMoreProfiles();
            var window = new MainWindow(vm) { AllowApplicationClose = true };
            try
            {
                var (viewport, size) = ArrangeOfflineDrawer(window, vm, percent);
                var scroll = Assert.IsType<ScrollViewer>(window.FindName(water ? "LiquidCoolingDrawerContent" : "MoreProfilesDrawerContent"));
                Assert.Equal(Visibility.Visible, scroll.Visibility);
                var target = Assert.IsType<Button>(window.FindName(water ? "BootWaterInstallButton" : "DynamicRecoverButton"));
                target.BringIntoView(); FlushLayout(viewport);
                AssertInsideScroll(target, scroll, viewport);
                AssertSeparateFooter(window, scroll, viewport, size);
                Assert.False(vm.Experiments.IsDynamicEnabled);
                SaveOfflineImage(viewport, size, $"exp2-{(water ? "boot-water" : "more-profiles")}-{percent}");
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData(70, false)]
    [InlineData(100, false)]
    [InlineData(130, false)]
    [InlineData(70, true)]
    [InlineData(100, true)]
    [InlineData(130, true)]
    public void PerformanceDetails_AreOptionalWhileWarningsAndControlsStayReachable(int percent, bool custom)
    {
        RunOnSta(() =>
        {
            var vm = CreateOfflineViewModel();
            // Supply capability evidence only, never a real hardware controller.
            vm.ApplyRuntimeCapabilities(new(vm.Dashboard.Identity,
                Enum.GetValues<HardwareFeature>().ToDictionary(feature => feature,
                    feature => new HardwareFeatureCapability(feature, CapabilityAvailability.Available, "Offline layout evidence")),
                Enum.GetValues<PlatformMode>().ToHashSet(), []));
            vm.SelectProfile(custom ? "custom" : "balanced");
            vm.OpenPerformance();
            var window = new MainWindow(vm) { AllowApplicationClose = true };
            try
            {
                var (viewport, size) = ArrangeOfflineDrawer(window, vm, percent);
                var scroll = Assert.IsType<ScrollViewer>(window.FindName("PerformanceDrawerContent"));
                var details = Assert.IsType<Expander>(window.FindName("PerformanceReadbackDetails"));
                Assert.False(details.IsExpanded);
                Assert.True(details.ActualHeight > 0);
                Assert.Equal(Visibility.Collapsed,
                    Assert.IsType<Border>(window.FindName("PerformanceValidationNotice")).Visibility);
                var warning = Assert.IsType<TextBlock>(window.FindName("OemPresetPowerLimitWarning"));
                Assert.Equal(Visibility.Visible, warning.Visibility);
                Assert.True(warning.ActualHeight > 0, "OEM warning must have a laid-out visible slot.");
                Assert.Equal(Color.FromRgb(0xD9, 0x36, 0x3E), Assert.IsType<SolidColorBrush>(warning.Foreground).Color);
                Assert.Contains("V1.0.0", Assert.IsType<TextBlock>(window.FindName("SettingsVersionFooter")).Text);
                var label = $"beta001-performance-{(custom ? "custom" : "balanced")}-{percent}";
                SaveOfflineImage(viewport, size, label + "-top");

                var foldedHeight = details.ActualHeight;
                details.IsExpanded = true;
                FlushLayout(viewport);
                Assert.True(details.ActualHeight > foldedHeight, "Status details must be expandable.");
                details.IsExpanded = false;
                scroll.ScrollToBottom();
                FlushLayout(viewport);
                var delay = Assert.IsType<TextBlock>(window.FindName("ProcessorFrequencyDelayWarning"));
                Assert.Equal(Color.FromRgb(0xD9, 0x36, 0x3E), Assert.IsType<SolidColorBrush>(delay.Foreground).Color);
                AssertInsideScroll(delay, scroll, viewport);
                AssertInsideScroll(Assert.IsType<TextBox>(window.FindName("ProcessorFrequencyLimitEditor")), scroll, viewport);
                AssertSeparateFooter(window, scroll, viewport, size);
                SaveOfflineImage(viewport, size, label + "-bottom");
                var validation = Assert.IsType<Border>(window.FindName("PerformanceValidationNotice"));
                validation.DataContext = new { IsPerformanceDraftValid = false, PerformanceValidationText = "数值无效" };
                FlushLayout(viewport);
                Assert.Equal(Visibility.Visible, validation.Visibility);
                Assert.Equal("数值无效", Assert.IsType<TextBlock>(validation.Child).Text);
                validation.ClearValue(FrameworkElement.DataContextProperty);
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData(HomeDrawer.Lighting, "LightingDrawerContent")]
    [InlineData(HomeDrawer.LiquidCooling, "LiquidCoolingDrawerContent")]
    [InlineData(HomeDrawer.Fan, "FanDrawerContent")]
    [InlineData(HomeDrawer.DisplayColor, "DisplayColorDrawerContent")]
    [InlineData(HomeDrawer.Other, "OtherDrawerContent")]
    public void SecondaryDrawers_RenderWithoutFooterOverlap(HomeDrawer drawer, string scrollName)
    {
        RunOnSta(() =>
        {
            var vm = CreateOfflineViewModel();
            switch (drawer)
            {
                case HomeDrawer.Lighting: vm.OpenLightingAsync(CancellationToken.None).GetAwaiter().GetResult(); break;
                case HomeDrawer.LiquidCooling: vm.OpenLiquidCoolingAsync(CancellationToken.None).GetAwaiter().GetResult(); break;
                case HomeDrawer.DisplayColor: vm.OpenDisplayAsync(CancellationToken.None).GetAwaiter().GetResult(); break;
                case HomeDrawer.Fan: vm.OpenFanCurve(); break;
                case HomeDrawer.Other: vm.OpenOtherAsync(CancellationToken.None).GetAwaiter().GetResult(); break;
                default: throw new ArgumentOutOfRangeException(nameof(drawer));
            }
            var window = new MainWindow(vm) { AllowApplicationClose = true };
            try
            {
                var (viewport, size) = ArrangeOfflineDrawer(window, vm, 100);
                var scroll = Assert.IsType<ScrollViewer>(window.FindName(scrollName));
                Assert.Equal(Visibility.Visible, scroll.Visibility);
                AssertSeparateFooter(window, scroll, viewport, size);
                SaveOfflineImage(viewport, size, $"beta001-{drawer}-top");
                scroll.ScrollToBottom();
                FlushLayout(viewport);
                SaveOfflineImage(viewport, size, $"beta001-{drawer}-bottom");
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData(70)]
    [InlineData(100)]
    [InlineData(130)]
    public void Snapshot1_HomeMonitorEntryFitsBeforePerformanceAndFanWithoutClipping(int percent)
    {
        RunOnSta(() =>
        {
            var vm = CreateOfflineViewModel();
            vm.WindowScalePercent = percent;
            var window = new MainWindow(vm) { AllowApplicationClose = true };
            try
            {
                var viewport = Assert.IsType<Viewbox>(window.Content);
                window.Content = null;
                viewport.DataContext = vm;
                var size = new Size(580 * percent / 100d, 550 * percent / 100d);
                viewport.Measure(size);
                viewport.Arrange(new Rect(size));
                FlushLayout(viewport);

                var monitor = Assert.IsType<Button>(window.FindName("MonitorEntryButton"));
                var performance = Assert.IsType<Button>(window.FindName("PerformanceEntryButton"));
                var fan = Assert.IsType<Button>(window.FindName("FanEntryButton"));
                var footer = Assert.IsType<Grid>(monitor.Parent);
                var settings = Assert.Single(footer.Children.OfType<Button>(), button => Grid.GetColumn(button) == 4);
                var previousRight = 0d;
                foreach (var button in new[] { monitor, performance, fan, settings })
                {
                    var bounds = button.TransformToAncestor(viewport).TransformBounds(new Rect(button.RenderSize));
                    Assert.True(button.ActualWidth > 0 && button.ActualHeight > 0, "Footer entries must remain visible.");
                    Assert.True(bounds.Left >= previousRight - 1 && bounds.Right <= size.Width + 1,
                        "Footer entries must remain in order without overlapping or clipping.");
                    Assert.True(bounds.Top >= 0 && bounds.Bottom <= size.Height + 1,
                        "Footer entries must stay inside the home panel.");
                    previousRight = bounds.Right;
                }
                var content = Assert.IsType<StackPanel>(monitor.Content);
                foreach (var label in content.Children.OfType<TextBlock>())
                {
                    var bounds = label.TransformToAncestor(monitor).TransformBounds(new Rect(label.RenderSize));
                    Assert.True(bounds.Left >= 0 && bounds.Right <= monitor.ActualWidth,
                        "The performance monitor entry must fit both its icon and label.");
                }
                var monitorTitle = Assert.Single(content.Children.OfType<TextBlock>(), label => label.Text == "性能监控");
                var performanceTitle = Assert.IsType<TextBlock>(window.FindName("PerformanceEntryTitle"));
                var fanTitle = Assert.IsType<TextBlock>(window.FindName("FanEntryTitle"));
                double Baseline(TextBlock label) => label.TransformToAncestor(viewport)
                    .Transform(new Point(0, label.BaselineOffset)).Y;
                Assert.InRange(Math.Abs(Baseline(monitorTitle) - Baseline(performanceTitle)), 0, 0.75 * percent / 100d);
                Assert.InRange(Math.Abs(Baseline(monitorTitle) - Baseline(fanTitle)), 0, 0.75 * percent / 100d);
            }
            finally { window.Close(); }
        });
    }

    private static MainWindowViewModel CreateOfflineViewModel() => new(HomeDashboardFactory.Create(
        new MachineIdentity("MECHREVO", "YAOSHI Series", "Offline layout test", "Core Ultra 9 275HX", "test", 24, 24),
        ["Intel Graphics", "NVIDIA GeForce RTX 5090 Laptop GPU"]), new(), new MemorySettings(), new NoStartup(),
        lightingProfileStore: new NoLightingProfiles());

    private static (Viewbox Viewport, Size Size) ArrangeOfflineDrawer(MainWindow window, MainWindowViewModel vm, int percent)
    {
        typeof(MainWindow).GetMethod("OpenDrawer", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, null);
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
        vm.WindowScalePercent = percent;
        var viewport = Assert.IsType<Viewbox>(window.Content);
        window.Content = null;
        viewport.DataContext = vm;
        var size = new Size(940 * percent / 100d, 550 * percent / 100d);
        viewport.Measure(size);
        viewport.Arrange(new Rect(size));
        FlushLayout(viewport);
        return (viewport, size);
    }

    private static void FlushLayout(Viewbox viewport)
    {
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
        viewport.UpdateLayout();
    }

    private static void AssertInsideScroll(FrameworkElement control, ScrollViewer scroll, Viewbox viewport)
    {
        var visible = scroll.TransformToAncestor(viewport).TransformBounds(new Rect(scroll.RenderSize));
        var bounds = control.TransformToAncestor(viewport).TransformBounds(new Rect(control.RenderSize));
        Assert.True(bounds.Top >= visible.Top - 1 && bounds.Bottom <= visible.Bottom + 1,
            $"{control.Name} is not reachable inside the scroll viewport.");
    }

    private static void AssertSeparateFooter(MainWindow window, ScrollViewer scroll, Viewbox viewport, Size size)
    {
        var footer = Assert.IsType<TextBlock>(window.FindName("SettingsVersionFooter"));
        var visible = scroll.TransformToAncestor(viewport).TransformBounds(new Rect(scroll.RenderSize));
        if (footer.Visibility == Visibility.Collapsed)
        {
            Assert.True(visible.Top >= 0 && visible.Bottom <= size.Height + 1,
                "The drawer viewport must remain inside the window without the duplicate footer.");
            return;
        }
        var footerBounds = footer.TransformToAncestor(viewport).TransformBounds(new Rect(footer.RenderSize));
        Assert.True(visible.Bottom <= footerBounds.Top);
        Assert.True(footerBounds.Bottom <= size.Height + 1);
    }

    private static void SaveOfflineImage(Viewbox viewport, Size size, string name)
    {
        if (Environment.GetEnvironmentVariable("REVOHELPER_TEST_VISUAL_DIR") is not { Length: > 0 } output) return;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(viewport);
        Directory.CreateDirectory(output);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(output, name + ".png"));
        encoder.Save(stream);
    }

    [Theory]
    [InlineData(false, 80)]
    [InlineData(true, 100)]
    public void ScaleSelector_AppliesImmediatelyOrRestoresSelectionOnSaveFailure(bool failSave, int expected)
    {
        RunOnSta(() =>
        {
            var settings = new MemorySettings { FailSave = failSave };
            var vm = new MainWindowViewModel(HomeDashboardFactory.Create(
                new MachineIdentity("MECHREVO", "YAOSHI", "Offline", "CPU", "test"), []),
                new(), settings, new NoStartup(), lightingProfileStore: new NoLightingProfiles());
            var window = new MainWindow(vm) { AllowApplicationClose = true };
            try
            {
                vm.OpenSettings();
                typeof(MainWindow).GetMethod("OpenDrawer", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, null);
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
                var selector = Assert.IsType<ComboBox>(window.FindName("WindowScaleSelector"));
                var initialWidth = window.Width;
                selector.SetCurrentValue(ComboBox.SelectedItemProperty, 80);
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
                Assert.Equal(expected, selector.SelectedItem);
                Assert.Equal(expected, typeof(MainWindowViewModel).GetProperty("WindowScalePercent")!.GetValue(vm));
                Assert.NotNull(selector.GetBindingExpression(ComboBox.SelectedItemProperty));
                if (failSave)
                {
                    Assert.Equal(initialWidth, window.Width);
                    Assert.Null(settings.Saved);
                }
                else Assert.Equal(80, settings.Saved!.WindowScalePercent);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void RealWindow_DrawerRoundTripsKeepSizeAndAnchorAtEveryScale()
    {
        RunOnSta(() =>
        {
            var dashboard = HomeDashboardFactory.Create(new MachineIdentity(
                "MECHREVO", "YAOSHI", "Offline", "CPU", "test"), []);
            var vm = new MainWindowViewModel(dashboard, new(), new MemorySettings(), new NoStartup(),
                lightingProfileStore: new NoLightingProfiles());
            var window = new MainWindow(vm) { AllowApplicationClose = true };
            try
            {
                var property = typeof(MainWindowViewModel).GetProperty("WindowScalePercent");
                Assert.NotNull(property);
                foreach (var percent in new[] { 70, 80, 90, 100, 110, 120, 130 })
                {
                    property.SetValue(vm, percent);
                    var baseline = new Rect(window.Left, window.Top, window.Width, window.Height);
                    for (var iteration = 0; iteration < 3; iteration++)
                    {
                        vm.OpenSettings();
                        typeof(MainWindow).GetMethod("OpenDrawer", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, null);
                        typeof(MainWindow).GetMethod("CloseDrawer_Click", BindingFlags.NonPublic | BindingFlags.Instance)!
                            .Invoke(window, [window, new RoutedEventArgs()]);
                        Assert.Equal(baseline, new Rect(window.Left, window.Top, window.Width, window.Height));
                    }
                }
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void BatterySleepDescription_WrapsInsideItsControlColumn()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "RevoHelper.App", "MainWindow.xaml");
        var document = XDocument.Load(path);
        var sleepDescription = document.Descendants().Single(element => element.Name.LocalName == "TextBlock" &&
            (string?)element.Attribute("Text") == "{presentation:UiText '睡眠指定时间后进入深度睡眠'}");
        Assert.Equal("Wrap", (string?)sleepDescription.Attribute("TextWrapping"));
        Assert.DoesNotContain(document.Descendants(), element =>
            (string?)element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "BatteryChargeLimitHint");
    }

    [Theory]
    [InlineData(70, false)]
    [InlineData(100, false)]
    [InlineData(130, false)]
    [InlineData(70, true)]
    [InlineData(100, true)]
    [InlineData(130, true)]
    public void ScaledDrawers_RenderWithReachableControlsAndSeparateFooter(int percent, bool batteryPage)
    {
        RunOnSta(() =>
        {
            var dashboard = HomeDashboardFactory.Create(new MachineIdentity(
                "MECHREVO", "YAOSHI Series", "Offline layout test", "Core Ultra 9 275HX", "test", 24, 24),
                ["Intel Graphics", "NVIDIA GeForce RTX 5090 Laptop GPU"]);
            var vm = new MainWindowViewModel(dashboard, new(), new MemorySettings(), new NoStartup(),
                lightingProfileStore: new NoLightingProfiles());
            var window = new MainWindow(vm) { AllowApplicationClose = true };
            try
            {
                if (batteryPage) vm.OpenBatteryAsync(CancellationToken.None).GetAwaiter().GetResult();
                else vm.OpenSettings();
                typeof(MainWindow).GetMethod("OpenDrawer", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, null);
                var selector = Assert.IsType<ComboBox>(window.FindName("WindowScaleSelector"));
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
                Assert.Equal(7, selector.Items.Count);
                var scaleProperty = typeof(MainWindowViewModel).GetProperty("WindowScalePercent");
                Assert.NotNull(scaleProperty);
                scaleProperty.SetValue(vm, percent);

                var viewport = Assert.IsType<Viewbox>(window.Content);
                var logicalRoot = Assert.IsType<Border>(window.FindName("WindowLogicalRoot"));
                Assert.Equal(940d, logicalRoot.Width);
                Assert.Equal(550d, logicalRoot.Height);
                // Render the real XAML without showing a Window, creating App, or using hardware controllers.
                window.Content = null;
                viewport.DataContext = vm;
                var size = new Size(940 * percent / 100d, 550 * percent / 100d);
                viewport.Measure(size);
                viewport.Arrange(new Rect(size));
                viewport.UpdateLayout();
                var scroll = Assert.IsType<ScrollViewer>(window.FindName(batteryPage ? "BatteryDrawerContent" : "SettingsDrawerContent"));
                scroll.ScrollToBottom();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
                viewport.UpdateLayout();
                AssertSeparateFooter(window, scroll, viewport, size);
                var scrollBounds = scroll.TransformToAncestor(viewport).TransformBounds(new Rect(scroll.RenderSize));
                if (batteryPage)
                {
                    Assert.Null(window.FindName("BatteryChargeLimitCard"));
                    Assert.Null(window.FindName("BatteryChargeLimitSlider"));
                }
                else
                {
                    scroll.ScrollToTop();
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
                    viewport.UpdateLayout();
                    var bounds = selector.TransformToAncestor(viewport).TransformBounds(new Rect(selector.RenderSize));
                    Assert.True(bounds.Top >= scrollBounds.Top - 1 && bounds.Bottom <= scrollBounds.Bottom + 1,
                        "Window scale selector is not reachable at the top of settings.");
                }
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height),
                    96, 96, PixelFormats.Pbgra32);
                bitmap.Render(viewport);
                if (Environment.GetEnvironmentVariable("REVOHELPER_TEST_VISUAL_DIR") is { Length: > 0 } output)
                {
                    Directory.CreateDirectory(output);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = File.Create(Path.Combine(output, $"v23-{(batteryPage ? "battery" : "settings")}-{percent}.png"));
                    encoder.Save(stream);
                }
            }
            finally { window.Close(); }
        });
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Offline WPF render timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed class MemorySettings : IApplicationSettingsStore
    {
        public bool FailSave { get; init; }
        public ApplicationSettings? Saved { get; private set; }
        public ApplicationSettings Load() => new();
        public void Save(ApplicationSettings settings)
        {
            if (FailSave) throw new IOException("Test settings store is not writable.");
            Saved = settings;
        }
    }
    private sealed class NoStartup : IStartupRegistration
    {
        public bool IsEnabled() => false;
        public void SetEnabled(bool enabled) => throw new InvalidOperationException("No startup writes in visual tests.");
    }
    private sealed class NoLightingProfiles : ILightingProfileStore
    {
        public LightingProfile? Load() => null;
        public void Save(LightingProfile profile) => throw new InvalidOperationException();
        public LightingProfile Import(string path) => throw new InvalidOperationException();
        public void Export(string path, LightingProfile profile) => throw new InvalidOperationException();
    }
}
