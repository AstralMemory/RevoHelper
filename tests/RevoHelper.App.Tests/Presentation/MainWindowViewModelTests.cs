using RevoHelper.App.Presentation;
using RevoHelper.App.Settings;
using RevoHelper.App.Lifecycle;
using RevoHelper.Core.Dashboard;
using RevoHelper.Core.Hardware;
using RevoHelper.Core.Telemetry;
using RevoHelper.Core.Fans;
using RevoHelper.Core.Control;
using RevoHelper.Core.Control.DynamicPower;
using RevoHelper.Hardware.Windows.OemStatus;

namespace RevoHelper.App.Tests.Presentation;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void ConfirmedTwoPresetsUseOemLabelsAndSurviveTransientEmptyEvidence()
    {
        var vm = new MainWindowViewModel(CreateDashboard());
        Assert.True(vm.ShowTurboMode);
        Assert.True(vm.ShowCustomMode);
        var two = new RuntimeCapabilitySnapshot(CreateDashboard().Identity,
            new Dictionary<HardwareFeature, HardwareFeatureCapability>(),
            new HashSet<PlatformMode> { PlatformMode.Office, PlatformMode.Balanced }, []);
        vm.ApplyRuntimeCapabilities(two);
        Assert.Equal("平衡模式", vm.OfficeModeTitle);
        Assert.Equal("性能模式", vm.BalancedModeTitle);
        Assert.Equal(2, vm.PresetRowSpan);
        Assert.False(vm.ShowTurboMode);
        Assert.False(vm.ShowCustomMode);
        vm.ApplyRuntimeCapabilities(two with { SupportedPlatformModes = new HashSet<PlatformMode>() });
        Assert.True(vm.HasTwoPresetModes);
        Assert.False(vm.CanUseOfficeMode);
        vm.ApplyRuntimeCapabilities(two with { SupportedPlatformModes = Enum.GetValues<PlatformMode>().ToHashSet() });
        Assert.True(vm.ShowTurboMode);
        Assert.True(vm.ShowCustomMode);
        Assert.False(vm.HasTwoPresetModes);
        Assert.Equal("办公模式", vm.OfficeModeTitle);
    }

    [Fact]
    public async Task PlatformSwitchSerializesHardwareAndKeepsTheLatestClickDuringDynamicRestore()
    {
        var controller = new DynamicTestController { Hold = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var experiments = new ExperimentalControlsViewModel(controller, null);
        var platform = new RecordingPlatformModeController(new(PlatformMode.Balanced));
        var vm = CreateViewModel(platformModeController: platform, experiments: experiments);
        var first = vm.ApplyPlatformProfileAsync("office", default);
        var second = vm.ApplyPlatformProfileAsync("turbo", default);
        try
        {
            Assert.Equal("turbo", vm.SelectedProfileId);
            Assert.False(second.IsCompleted);
            Assert.Null(platform.LastApplied);
        }
        finally { controller.Hold.SetResult(); await Task.WhenAll(first, second); }
        Assert.False(await first);
        Assert.True(await second);
        Assert.Equal(2, controller.Preparations);
        Assert.Equal(new PlatformModeSelection(PlatformMode.TurboQuiet), platform.LastApplied);
    }

    [Fact]
    public async Task OpeningPerformanceDrawerDoesNotTouchDynamicPowerState()
    {
        var controller = new DynamicTestController { Hold = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var experiments = new ExperimentalControlsViewModel(controller, null);
        var vm = CreateViewModel(experiments: experiments);
        var opening = vm.OpenPerformanceAsync(CancellationToken.None);
        try
        {
            await opening.WaitAsync(TimeSpan.FromMilliseconds(250));
            Assert.Equal(HomeDrawer.Performance, vm.ActiveDrawer);
            Assert.Equal(0, controller.Pauses);
        }
        finally { controller.Hold.TrySetResult(); }
    }

    [Theory]
    [InlineData("profile")]
    [InlineData("performance")]
    [InlineData("custom")]
    public async Task ManualPerformanceActionsRejectWritesWhenDynamicRecoveryIsPending(string action)
    {
        var controller = new DynamicTestController { FailRestore = true };
        using var experiments = new ExperimentalControlsViewModel(controller, null);
        var platform = new RecordingPlatformModeController(new(PlatformMode.Balanced));
        var vm = CreateViewModel(platformModeController: platform, experiments: experiments);
        switch (action)
        {
            case "profile": Assert.False(await vm.ApplyPlatformProfileAsync("office", default)); break;
            case "performance": await vm.ApplyPerformancePolicyAsync(default); break;
            case "custom": await vm.ApplyCustomPerformanceAsync(default); break;
        }
        Assert.Equal(1, controller.Preparations);
        Assert.Null(platform.LastApplied);
        Assert.Contains("恢复未完成", vm.StatusMessage);
    }

    [Theory]
    [InlineData("office")]
    [InlineData("balanced")]
    [InlineData("turbo")]
    public async Task DynamicSavingRemainsEnabledAcrossOemProfileChanges(string profile)
    {
        var controller = new DynamicTestController();
        using var experiments = new ExperimentalControlsViewModel(controller, null);
        await experiments.SetDynamicEnabledAsync(true);
        var platform = new RecordingPlatformModeController(new(PlatformMode.Balanced));
        var vm = CreateViewModel(platformModeController: platform, experiments: experiments);

        Assert.True(await vm.ApplyPlatformProfileAsync(profile, default));

        Assert.True(experiments.IsDynamicEnabled);
        Assert.Equal(1, controller.Preparations);
    }

    private sealed class DynamicTestController : IDynamicPowerController
    {
        public bool FailRestore; public int Pauses; public int Preparations; public TaskCompletionSource? Hold;
        public DynamicPowerStatus Status { get; private set; } = new(false, "off", 0, false, false);
        public Task SetEnabledAsync(bool enabled, CancellationToken token)
        { Status = Status with { Enabled = enabled }; return Task.CompletedTask; }
        public Task ObserveAsync(HardwareTelemetrySnapshot sample, CancellationToken token) => Task.CompletedTask;
        public Task RecoverAsync(CancellationToken token) => PauseAsync("recover", token);
        public async Task PrepareForExternalChangeAsync(string reason, CancellationToken token)
        {
            Preparations++; if (Hold is not null) await Hold.Task;
            Status = new(Status.Enabled, reason, 0, false, FailRestore);
        }
        public async Task PauseAsync(string reason, CancellationToken token)
        {
            Pauses++; if (Hold is not null) await Hold.Task;
            Status = new(false, reason, 0, FailRestore, FailRestore);
        }
    }

    [Fact]
    public async Task KeyboardMasterPower_UpdatesEditorAndAppliesOfflineColorOnReenable()
    {
        var controls = new RecordingQuickControlsController();
        controls.AvailableLightingZones.Add(LightingZone.Keyboard);
        var viewModel = CreateViewModel(quickControlsController: controls);
        await viewModel.OpenLightingAsync(CancellationToken.None);

        await viewModel.SetKeyboardLightingAsync(false, CancellationToken.None);
        Assert.False(viewModel.IsSelectedLightingZoneEnabled);
        viewModel.SetLightingColor("#5267FF");
        await viewModel.ApplySelectedLightingZoneAsync(CancellationToken.None);
        Assert.Empty(controls.AppliedLightingConfigurations);

        await viewModel.SetKeyboardLightingAsync(true, CancellationToken.None);

        Assert.True(viewModel.IsSelectedLightingZoneEnabled);
        Assert.True(viewModel.IsKeyboardLightingEnabled);
        Assert.Equal("#5267FF", Assert.Single(controls.AppliedLightingConfigurations).Color.Hex);
    }

    [Fact]
    public async Task LightingFeedback_TabChangeShowsThatZonesDraftNotAnotherZonesAcknowledgement()
    {
        var controls = new RecordingQuickControlsController();
        controls.AvailableLightingZones.UnionWith([LightingZone.CoastLightbar, LightingZone.Logo]);
        var viewModel = CreateViewModel(quickControlsController: controls);
        await viewModel.OpenLightingAsync(CancellationToken.None);
        viewModel.SelectLightingZone(LightingZone.CoastLightbar);
        viewModel.LightingColorHex = "#5267FF";
        Assert.Contains("#5267FF", viewModel.LightingStatusText, StringComparison.Ordinal);
        viewModel.SelectLightingZone(LightingZone.Logo);
        Assert.DoesNotContain("海岸", viewModel.LightingStatusText, StringComparison.Ordinal);
        viewModel.SelectLightingZone(LightingZone.CoastLightbar);
        Assert.Contains("#5267FF", viewModel.LightingStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LightingCapturedRequest_AfterTabSwitchWritesOriginalZoneAndColor()
    {
        var controls = new RecordingQuickControlsController();
        controls.AvailableLightingZones.UnionWith([LightingZone.CoastLightbar, LightingZone.Logo]);
        var viewModel = CreateViewModel(quickControlsController: controls);
        await viewModel.OpenLightingAsync(CancellationToken.None);
        viewModel.SelectLightingZone(LightingZone.CoastLightbar);
        viewModel.IsSelectedLightingZoneEnabled = true;
        viewModel.LightingColorHex = "#5267FF";
        var snapshot = viewModel.CaptureLightingEditor();
        Assert.Equal(LightingZone.CoastLightbar, snapshot!.Zone);
        viewModel.SelectLightingZone(LightingZone.Logo);
        viewModel.LightingColorHex = "#24D66A";

        await viewModel.ApplyLightingConfigurationAsync(snapshot!, CancellationToken.None);

        Assert.Equal(snapshot, Assert.Single(controls.AppliedLightingConfigurations));
        Assert.Equal("#24D66A", viewModel.LightingColorHex);
        Assert.DoesNotContain("海岸", viewModel.LightingStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OfficialConsoleDetection_ExplainsCoexistenceWithoutStoppingOemServices()
    {
        var viewModel = CreateViewModel(officialConsoleRunningProbe: () => true);

        await viewModel.RefreshConflictServicesAsync(CancellationToken.None);

        Assert.True(viewModel.IsOfficialConsoleRunning);
        Assert.Equal("RevoHelper可以与官方控制台共存，不会停用或禁用它们", viewModel.OfficialConsoleInstructionText);
        Assert.DoesNotContain("请关闭官方前台", viewModel.OfficialConsoleInstructionText, StringComparison.Ordinal);
    }

    [Fact]
    public void UpdateConflictServices_ReplacesPreviousDetectionSnapshot()
    {
        var viewModel = new MainWindowViewModel(CreateDashboard());

        viewModel.UpdateConflictServices(
        [
            new("XtuService", "Intel XTU Service", true, true, "正在运行")
        ]);
        viewModel.UpdateConflictServices(
        [
            new("XtuService", "Intel XTU Service", false, true, "已停止"),
            new("dptftcs", "Intel Dynamic Tuning", true, true, "正在运行")
        ]);

        Assert.Equal(2, viewModel.ConflictServices.Count);
        Assert.False(viewModel.ConflictServices[0].IsRunning);
        Assert.True(viewModel.ConflictServices[1].IsRunning);
    }

    [Fact]
    public async Task StopSelectedConflictServices_StopsOnlySelectedRunningServicesAndRefreshesStatus()
    {
        var manager = new RecordingConflictServiceManager
        {
            DetectionResult =
            [
                new("XtuService", "Intel XTU Service", false, true, "已停止"),
                new("dptftcs", "Intel Dynamic Tuning", false, true, "已停止")
            ]
        };
        var viewModel = CreateViewModel(conflictServiceManager: manager);
        viewModel.UpdateConflictServices(
        [
            new("XtuService", "Intel XTU Service", true, true, "正在运行"),
            new("dptftcs", "Intel Dynamic Tuning", false, true, "已停止")
        ]);
        viewModel.ConflictServices[0].IsSelected = true;
        viewModel.ConflictServices[1].IsSelected = true;

        Assert.True(viewModel.CanStopSelectedConflictServices);
        Assert.True(viewModel.CanRestoreSelectedConflictServices);
        await viewModel.StopSelectedConflictServicesAsync(CancellationToken.None);

        Assert.Equal(["XtuService"], manager.StoppedServices);
        Assert.All(viewModel.ConflictServices, service => Assert.False(service.IsRunning));
        Assert.Contains("停止", viewModel.ConflictServiceOperationStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RestoreSelectedConflictServices_StartsOnlySelectedStoppedServices()
    {
        var manager = new RecordingConflictServiceManager
        {
            DetectionResult =
            [
                new("ipfsvc", "Intel Innovation Platform Framework", true, true, "正在运行")
            ]
        };
        var viewModel = CreateViewModel(conflictServiceManager: manager);
        viewModel.UpdateConflictServices(
        [
            new("ipfsvc", "Intel Innovation Platform Framework", false, true, "已停止")
        ]);
        viewModel.ConflictServices[0].IsSelected = true;

        await viewModel.RestoreSelectedConflictServicesAsync(CancellationToken.None);

        Assert.Equal(["ipfsvc"], manager.RestoredServices);
        Assert.True(viewModel.ConflictServices[0].IsRunning);
        Assert.Contains("恢复", viewModel.ConflictServiceOperationStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StopSelectedConflictServices_WhenOperationFails_ShowsFailureAndReenablesAction()
    {
        var manager = new RecordingConflictServiceManager
        {
            OperationException = new InvalidOperationException("access denied")
        };
        var viewModel = CreateViewModel(conflictServiceManager: manager);
        viewModel.UpdateConflictServices(
        [
            new("XtuService", "Intel XTU Service", true, true, "正在运行")
        ]);
        viewModel.ConflictServices[0].IsSelected = true;

        await viewModel.StopSelectedConflictServicesAsync(CancellationToken.None);

        Assert.Contains("access denied", viewModel.ConflictServiceOperationStatusText, StringComparison.Ordinal);
        Assert.True(viewModel.CanStopSelectedConflictServices);
    }

    [Fact]
    public void ApplyRuntimeCapabilities_ExposesOnlyConfirmedHardwareFeatures()
    {
        var viewModel = new MainWindowViewModel(CreateDashboard());
        var identity = CreateDashboard().Identity;
        var features = Enum.GetValues<HardwareFeature>().ToDictionary(
            feature => feature,
            feature => new HardwareFeatureCapability(
                feature,
                feature is HardwareFeature.OemPlatformModes or
                    HardwareFeature.OemFanCurves or
                    HardwareFeature.WindowsProcessorPolicy
                    ? CapabilityAvailability.Available
                    : CapabilityAvailability.Unverified,
                "test"));
        var snapshot = new RuntimeCapabilitySnapshot(
            identity,
            features,
            new HashSet<PlatformMode> { PlatformMode.Office, PlatformMode.Custom },
            []);

        viewModel.ApplyRuntimeCapabilities(snapshot);

        Assert.True(viewModel.CanUseOfficeMode);
        Assert.False(viewModel.CanUseBalancedMode);
        Assert.True(viewModel.CanUseCustomMode);
        Assert.True(viewModel.CanOpenFanAdjustment);
        Assert.True(viewModel.CanOpenPerformanceAdjustment);
        Assert.False(viewModel.CanOpenGpuOutput);
        Assert.False(viewModel.CanOpenLighting);
        Assert.False(viewModel.CanOpenLiquidCooling);
        Assert.Contains("尚未", viewModel.GpuOutputAvailabilityReason, StringComparison.Ordinal);
    }

    [Fact]
    public void GpuOutputDrawer_RemainsOpenableWhenControllerIsWiredBeforeRuntimeEvidenceArrives()
    {
        var controller = new RecordingGpuOutputModeController(new(
            GpuOutputMode.Hybrid,
            new(true, true, true)));
        var viewModel = CreateViewModel(gpuOutputModeController: controller);

        Assert.True(viewModel.CanOpenGpuOutput);
    }

    [Fact]
    public void OemPowerLimitEditing_IsExposedOnlyForConfirmedCustomMode()
    {
        var viewModel = new MainWindowViewModel(CreateDashboard());
        var features = Enum.GetValues<HardwareFeature>().ToDictionary(
            feature => feature,
            feature => new HardwareFeatureCapability(
                feature,
                feature is HardwareFeature.OemPlatformModes or
                    HardwareFeature.OemCpuPowerLimits or
                    HardwareFeature.WindowsProcessorPolicy
                    ? CapabilityAvailability.Available
                    : CapabilityAvailability.Unverified,
                "test"));
        viewModel.ApplyRuntimeCapabilities(new(
            CreateDashboard().Identity,
            features,
            new HashSet<PlatformMode> { PlatformMode.Office, PlatformMode.Balanced, PlatformMode.Custom },
            []));

        Assert.False(viewModel.CanEditOemPowerLimits);
        Assert.Contains("官方预设", viewModel.OemPowerLimitAvailabilityReason, StringComparison.Ordinal);

        viewModel.SelectProfile("custom");

        Assert.True(viewModel.CanEditOemPowerLimits);
    }

    [Fact]
    public void AppVersionText_UsesUndatedSnapshot1Identity()
    {
        var viewModel = new MainWindowViewModel(CreateDashboard());

        Assert.Equal("V1.0.0", viewModel.ReleaseVersionText);
        Assert.Equal("RevoHelper V1.0.0 · 免费使用", viewModel.AppVersionText);
    }

    [Fact]
    public void UiLanguage_WhenEnglishIsSelected_PersistsAndUpdatesPrimaryCopy()
    {
        var store = new RecordingSettingsStore();
        var viewModel = CreateViewModel(store: store);

        Assert.Equal("笔记本性能模式调度", viewModel.UiCopy.HomeTitle);

        viewModel.SelectedUiLanguageCode = "en-US";
        viewModel.OpenPerformance();

        Assert.Equal("en-US", store.LastSaved?.UiLanguageCode);
        Assert.Equal("Laptop performance profiles", viewModel.UiCopy.HomeTitle);
        Assert.Equal("Performance", viewModel.DrawerTitle);
        Assert.Equal("RevoHelper V1.0.0 · Free to use", viewModel.AppVersionText);
    }

    [Fact]
    public void ApplyTelemetry_IdenticalVisibleValuesKeepRowAndChangedValuesStillUpdate()
    {
        var viewModel = new MainWindowViewModel(CreateDashboard());
        var cpu = viewModel.Devices.Single(device => device.Kind is DeviceKind.Cpu);
        var sample = new HardwareTelemetrySnapshot(DateTimeOffset.Now,
            [new DeviceTelemetry(DeviceKind.Cpu, cpu.Model, 20, 60, 30, 3000)]);
        viewModel.ApplyTelemetry(sample);
        var row = viewModel.Devices.Single(device => device.Kind is DeviceKind.Cpu);
        var changes = 0;
        viewModel.Devices.CollectionChanged += (_, _) => changes++;
        viewModel.ApplyTelemetry(sample with { CapturedAt = sample.CapturedAt.AddSeconds(1) });
        Assert.Same(row, viewModel.Devices.Single(device => device.Kind is DeviceKind.Cpu));
        Assert.Equal(0, changes);
        viewModel.ApplyTelemetry(new HardwareTelemetrySnapshot(sample.CapturedAt.AddSeconds(2),
            [new DeviceTelemetry(DeviceKind.Cpu, cpu.Model, 40, 60, 30, 3000)]));
        Assert.Equal(1, changes);
        Assert.Equal("40%", viewModel.Devices.Single(device => device.Kind is DeviceKind.Cpu).Usage);
    }

    [Fact]
    public void ApplyTelemetry_UpdatesMatchingRowsAndKeepsIdentityText()
    {
        var viewModel = new MainWindowViewModel(CreateDashboard());
        var originalCpu = viewModel.Devices.Single(device => device.Kind is DeviceKind.Cpu);

        viewModel.ApplyTelemetry(new HardwareTelemetrySnapshot(
            new DateTimeOffset(2026, 8, 9, 3, 4, 5, TimeSpan.FromHours(8)),
            [new DeviceTelemetry(DeviceKind.Cpu, originalCpu.Model, 31.5, 61.8, 37.6, 3899.6)]));

        var updatedCpu = viewModel.Devices.Single(device => device.Kind is DeviceKind.Cpu);
        Assert.Equal(originalCpu.Model, updatedCpu.Model);
        Assert.Equal(originalCpu.Details, updatedCpu.Details);
        Assert.Equal("32%", updatedCpu.Usage);
        Assert.Equal("62°C", updatedCpu.Temperature);
        Assert.Equal("38 W", updatedCpu.Power);
        Assert.Equal("3900 MHz", updatedCpu.Frequency);
        Assert.Contains("03:04:05", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyTelemetry_OnlyFallsBackToKindWhenThereIsOneCandidate()
    {
        var viewModel = new MainWindowViewModel(CreateDashboard());
        var cpu = viewModel.Devices.Single(device => device.Kind is DeviceKind.Cpu);
        var gpu = viewModel.Devices.Single(device => device.Kind is DeviceKind.DiscreteGpu);
        var secondGpu = gpu with { Model = "Second GPU" };
        viewModel.Devices.Add(secondGpu);

        viewModel.ApplyTelemetry(new HardwareTelemetrySnapshot(DateTimeOffset.Now,
            [new DeviceTelemetry(DeviceKind.Cpu, "Different CPU label", 22, 60, 30, 3000),
             new DeviceTelemetry(DeviceKind.DiscreteGpu, "Unknown GPU label", 99, 70, 100, 1500)]));

        Assert.Equal("22%", viewModel.Devices.Single(device => device.Kind is DeviceKind.Cpu).Usage);
        Assert.Same(gpu, viewModel.Devices.Single(device => device.Model == gpu.Model));
        Assert.Same(secondGpu, viewModel.Devices.Single(device => device.Model == secondGpu.Model));

        viewModel.ApplyTelemetry(new HardwareTelemetrySnapshot(DateTimeOffset.Now,
            [new DeviceTelemetry(DeviceKind.DiscreteGpu, "SECOND GPU", 33, 60, 50, 1200)]));

        Assert.Equal("33%", viewModel.Devices.Single(device => device.Model == secondGpu.Model).Usage);
        Assert.Same(gpu, viewModel.Devices.Single(device => device.Model == gpu.Model));
        Assert.Equal(cpu.Model, viewModel.Devices.Single(device => device.Kind is DeviceKind.Cpu).Model);
    }

    [Fact]
    public void ReportTelemetryUnavailable_PreservesLastGoodReadings()
    {
        var viewModel = new MainWindowViewModel(CreateDashboard());
        var cpu = viewModel.Devices.Single(device => device.Kind is DeviceKind.Cpu);
        viewModel.ApplyTelemetry(new HardwareTelemetrySnapshot(
            DateTimeOffset.Now,
            [new DeviceTelemetry(DeviceKind.Cpu, cpu.Model, 20, 60, 30, 3000)]));

        viewModel.ReportTelemetryUnavailable("sensor offline");

        var unchanged = viewModel.Devices.Single(device => device.Kind is DeviceKind.Cpu);
        Assert.Equal("20%", unchanged.Usage);
        Assert.Equal("60°C", unchanged.Temperature);
        Assert.Contains("暂不可用", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyTelemetry_ShowsHighestPerformanceAndEfficiencyClocksSeparately()
    {
        var viewModel = new MainWindowViewModel(CreateDashboard());
        var cpu = viewModel.Devices.Single(device => device.Kind is DeviceKind.Cpu);

        viewModel.ApplyTelemetry(new HardwareTelemetrySnapshot(
            DateTimeOffset.Now,
            [new DeviceTelemetry(
                DeviceKind.Cpu,
                cpu.Model,
                19,
                61,
                40,
                null,
                PerformanceCoreFrequencyMhz: 5187,
                EfficiencyCoreFrequencyMhz: 1995)]));

        var updated = viewModel.Devices.Single(device => device.Kind is DeviceKind.Cpu);
        Assert.Equal("P 5187 MHz\nE 1995 MHz", updated.Frequency);
    }

    [Fact]
    public void ApplyTelemetry_IntegratedGpuWithoutOwnTemperature_SharesCpuPackageTemperature()
    {
        var viewModel = new MainWindowViewModel(CreateDashboard());
        var cpu = viewModel.Devices.Single(device => device.Kind is DeviceKind.Cpu);
        var integratedGpu = viewModel.Devices.Single(device => device.Kind is DeviceKind.IntegratedGpu);

        viewModel.ApplyTelemetry(new HardwareTelemetrySnapshot(
            DateTimeOffset.Now,
            [
                new DeviceTelemetry(DeviceKind.IntegratedGpu, integratedGpu.Model, 46, null, 1, 1400),
                new DeviceTelemetry(DeviceKind.Cpu, cpu.Model, 18, 63, 24, null)
            ]));

        var updated = viewModel.Devices.Single(device => device.Kind is DeviceKind.IntegratedGpu);
        Assert.Equal("63°C", updated.Temperature);
        Assert.Equal(string.Empty, updated.Details);
    }

    [Fact]
    public void ApplyTelemetry_IntegratedGpuWithOwnTemperature_KeepsIndependentReading()
    {
        var viewModel = new MainWindowViewModel(CreateDashboard());
        var cpu = viewModel.Devices.Single(device => device.Kind is DeviceKind.Cpu);
        var integratedGpu = viewModel.Devices.Single(device => device.Kind is DeviceKind.IntegratedGpu);

        viewModel.ApplyTelemetry(new HardwareTelemetrySnapshot(
            DateTimeOffset.Now,
            [
                new DeviceTelemetry(DeviceKind.Cpu, cpu.Model, 18, 63, 24, null),
                new DeviceTelemetry(DeviceKind.IntegratedGpu, integratedGpu.Model, 46, 51, 1, 1400)
            ]));

        var updated = viewModel.Devices.Single(device => device.Kind is DeviceKind.IntegratedGpu);
        Assert.Equal("51°C", updated.Temperature);
        Assert.Equal(string.Empty, updated.Details);
    }

    [Fact]
    public void ApplyTelemetry_DiscreteGpuWithDedicatedMemory_ShowsPhysicalVramCapacity()
    {
        var viewModel = new MainWindowViewModel(CreateDashboard());
        var discreteGpu = viewModel.Devices.Single(device => device.Kind is DeviceKind.DiscreteGpu);

        viewModel.ApplyTelemetry(new HardwareTelemetrySnapshot(
            DateTimeOffset.Now,
            [new DeviceTelemetry(
                DeviceKind.DiscreteGpu,
                discreteGpu.Model,
                40,
                44,
                48,
                1050,
                DedicatedMemoryMegabytes: 24463)]));

        var updated = viewModel.Devices.Single(device => device.Kind is DeviceKind.DiscreteGpu);
        Assert.Equal("24 GB 显存", updated.Details);
    }

    [Fact]
    public void ApplyOemCpuStatus_FillsMissingTemperatureWithoutErasingOtherTelemetry()
    {
        var viewModel = new MainWindowViewModel(CreateDashboard());
        var cpu = viewModel.Devices.Single(device => device.Kind is DeviceKind.Cpu);
        viewModel.ApplyTelemetry(new HardwareTelemetrySnapshot(
            DateTimeOffset.Now,
            [new DeviceTelemetry(DeviceKind.Cpu, cpu.Model, 20, null, null, 3000)]));

        viewModel.ApplyOemCpuStatus(new OemCpuStatus(57, 99, 9999));

        var updated = viewModel.Devices.Single(device => device.Kind is DeviceKind.Cpu);
        Assert.Equal("20%", updated.Usage);
        Assert.Equal("57°C", updated.Temperature);
        Assert.Equal("3000 MHz", updated.Frequency);
    }

    [Fact]
    public void ApplyTelemetry_MissingCpuTemperature_DoesNotEraseLastOemTemperature()
    {
        var viewModel = new MainWindowViewModel(CreateDashboard());
        var cpu = viewModel.Devices.Single(device => device.Kind is DeviceKind.Cpu);
        viewModel.ApplyOemCpuStatus(new OemCpuStatus(57, 10, 2000));

        viewModel.ApplyTelemetry(new HardwareTelemetrySnapshot(
            DateTimeOffset.Now,
            [new DeviceTelemetry(DeviceKind.Cpu, cpu.Model, 20, null, null, null)]));

        var updated = viewModel.Devices.Single(device => device.Kind is DeviceKind.Cpu);
        Assert.Equal("57°C", updated.Temperature);
    }

    [Fact]
    public void ApplyFanTable_WithoutController_KeepsHardwareApplyDisabled()
    {
        var viewModel = new MainWindowViewModel(CreateDashboard());

        viewModel.ApplyFanTable(CreateFanTable());

        Assert.Equal(16, viewModel.FanPoints.Count);
        Assert.True(viewModel.IsFanCurveValid);
        Assert.False(viewModel.IsFanApplyEnabled);
        Assert.Contains("已读取", viewModel.FanStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyFanCurveAsync_WithLiveController_WritesEditedCurveAndResponseSettings()
    {
        var fan = new RecordingFanController(CreateFanTable(), new(true, true, 18));
        var viewModel = CreateViewModel(fanController: fan);
        viewModel.SelectProfile("custom");
        await viewModel.OpenFanCurveAsync(CancellationToken.None);
        viewModel.FanPoints[0].DutyPercent += 3;
        viewModel.IsFanIndependentOutputEnabled = false;
        viewModel.IsFanTransitionSensitivityEnabled = true;
        viewModel.FanTransitionSensitivity = 24;

        await viewModel.ApplyFanCurveAsync(CancellationToken.None);

        Assert.True(viewModel.IsFanApplyEnabled);
        Assert.NotNull(fan.LastAppliedTable);
        Assert.Equal(viewModel.FanPoints[0].DutyPercent, fan.LastAppliedTable!.Cpu[0].DutyPercent);
        Assert.Equal(new FanResponseSettings(false, true, 24), fan.LastAppliedResponse);
        Assert.Contains("回读确认", viewModel.FanStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyFanCurveAsync_InOemPresetMode_DoesNotWriteFirmwareLockedCurve()
    {
        var fan = new RecordingFanController(CreateFanTable(), new(true, true, 18));
        var viewModel = CreateViewModel(fanController: fan);
        viewModel.SelectProfile("balanced");
        await viewModel.OpenFanCurveAsync(CancellationToken.None);

        await viewModel.ApplyFanCurveAsync(CancellationToken.None);

        Assert.Null(fan.LastAppliedTable);
        Assert.Null(fan.LastAppliedResponse);
        Assert.False(viewModel.IsFanApplyEnabled);
        Assert.Contains("自定义模式", viewModel.FanStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void FanTransitionSensitivity_DisplaysOemDelayValueInMilliseconds()
    {
        var viewModel = CreateViewModel();

        viewModel.FanTransitionSensitivity = 24;

        Assert.Equal("24 ms/0.5%", viewModel.FanTransitionSensitivityText);
    }

    [Fact]
    public async Task RestoreOemFanCurveAsync_ReplacesDraftWithConfirmedFactoryReadback()
    {
        var initial = CreateFanTable();
        var restored = initial with
        {
            Cpu = initial.Cpu.Select(point => point with { DutyPercent = Math.Max(point.DutyPercent, 40) }).ToArray()
        };
        var fan = new RecordingFanController(initial, new(true, true, 18)) { RestoredTable = restored };
        var viewModel = CreateViewModel(fanController: fan);
        viewModel.SelectProfile("custom");
        await viewModel.OpenFanCurveAsync(CancellationToken.None);

        await viewModel.RestoreOemFanCurveAsync(CancellationToken.None);

        Assert.Equal(restored.Cpu[0].DutyPercent, viewModel.FanPoints[0].DutyPercent);
        Assert.Contains("原厂默认", viewModel.FanStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RestoreOemFanCurveAsync_InOemPresetMode_DoesNotSendRestoreCommand()
    {
        var fan = new RecordingFanController(CreateFanTable(), new(true, true, 18));
        var viewModel = CreateViewModel(fanController: fan);
        viewModel.SelectProfile("balanced");
        await viewModel.OpenFanCurveAsync(CancellationToken.None);

        await viewModel.RestoreOemFanCurveAsync(CancellationToken.None);

        Assert.Equal(0, fan.RestoreCallCount);
        Assert.Contains("自定义模式", viewModel.FanStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void FanCurveDrafts_RemainIndependentForEachPerformanceProfile()
    {
        var viewModel = new MainWindowViewModel(CreateDashboard());
        viewModel.ApplyFanTable(CreateFanTable());
        var originalDuty = viewModel.FanPoints[0].DutyPercent;

        viewModel.FanPoints[0].DutyPercent = originalDuty + 3;
        viewModel.SelectProfile("office");
        Assert.Equal(originalDuty, viewModel.FanPoints[0].DutyPercent);

        viewModel.FanPoints[0].DutyPercent = originalDuty + 5;
        viewModel.SelectProfile("balanced");
        Assert.Equal(originalDuty + 3, viewModel.FanPoints[0].DutyPercent);

        viewModel.SelectProfile("office");
        Assert.Equal(originalDuty + 5, viewModel.FanPoints[0].DutyPercent);
    }

    [Fact]
    public void TurboFanCurveDrafts_RemainIndependentForQuietAndHighEfficiencyStrategies()
    {
        var viewModel = new MainWindowViewModel(CreateDashboard());
        viewModel.ApplyFanTable(CreateFanTable());
        var originalDuty = viewModel.FanPoints[0].DutyPercent;

        viewModel.SelectTurboStrategy(TurboStrategy.Quiet);
        viewModel.FanPoints[0].DutyPercent = originalDuty + 3;
        viewModel.SelectTurboStrategy(TurboStrategy.HighEfficiency);
        Assert.Equal(originalDuty, viewModel.FanPoints[0].DutyPercent);

        viewModel.FanPoints[0].DutyPercent = originalDuty + 5;
        viewModel.SelectTurboStrategy(TurboStrategy.Quiet);
        Assert.Equal(originalDuty + 3, viewModel.FanPoints[0].DutyPercent);
        viewModel.SelectTurboStrategy(TurboStrategy.HighEfficiency);
        Assert.Equal(originalDuty + 5, viewModel.FanPoints[0].DutyPercent);
    }

    [Fact]
    public void FanCurveNumericInput_IsClampedBetweenAdjacentDutyPoints()
    {
        var viewModel = new MainWindowViewModel(CreateDashboard());
        viewModel.ApplyFanTable(CreateFanTable());

        var edited = viewModel.FanPoints[6];
        var higherTemperatureDuty = viewModel.FanPoints[7].DutyPercent;
        edited.DutyPercent = 100;

        Assert.Equal(higherTemperatureDuty, edited.DutyPercent);
        Assert.True(viewModel.IsFanCurveValid);
    }

    [Fact]
    public void FanCurvePoint_UsesCelsiusUnitLabel()
    {
        var point = new FanCurvePointViewModel(1, 48, 46, 30, _ => { });

        Assert.Equal("48℃", point.TemperatureLabel);
    }

    [Fact]
    public void Constructor_DoesNotAdvertiseTheCompletedAppAsAReadOnlyPreview()
    {
        var viewModel = new MainWindowViewModel(CreateDashboard());

        Assert.DoesNotContain("只读预览", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("尚未接入", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.Null(typeof(MainWindowViewModel).GetMethod("PreviewQuickAction"));
    }

    [Fact]
    public void Constructor_SelectsBalancedProfileByDefault()
    {
        var viewModel = new MainWindowViewModel(CreateDashboard());

        Assert.Equal("balanced", viewModel.SelectedProfileId);
        Assert.Equal("当前：平衡模式", viewModel.CurrentFanProfileLabel);
        Assert.Equal("#2185F4", viewModel.FanAccentColor);
        Assert.Equal("#DCEEFF", viewModel.FanAccentSurfaceColor);
    }

    [Fact]
    public void OemFanReferenceCurvesFollowProfileIntentWithoutPretendingToBeWritable()
    {
        var viewModel = new MainWindowViewModel(CreateDashboard());

        viewModel.SelectProfile("office");
        var office = viewModel.FanPoints.Sum(point => point.DutyPercent);
        viewModel.SelectProfile("balanced");
        var balanced = viewModel.FanPoints.Sum(point => point.DutyPercent);
        viewModel.SelectTurboStrategy(TurboStrategy.Quiet);
        var turboQuiet = viewModel.FanPoints.Sum(point => point.DutyPercent);
        viewModel.SelectTurboStrategy(TurboStrategy.HighEfficiency);
        var turboHigh = viewModel.FanPoints.Sum(point => point.DutyPercent);

        Assert.Equal(11, viewModel.FanPoints.Count);
        Assert.True(office < balanced);
        Assert.InRange(turboQuiet - balanced, 1, 15);
        Assert.True(turboHigh > turboQuiet);
        Assert.False(viewModel.CanEditFanSettings);
        Assert.Contains("参考", viewModel.FanEditingAvailabilityText, StringComparison.Ordinal);
    }

    [Fact]
    public void OemFanReferenceSurfaceIsClearlyMutedWhileCustomEditorRemainsOpaque()
    {
        var viewModel = new MainWindowViewModel(CreateDashboard());

        Assert.Equal(0.55d, viewModel.FanCurveSurfaceOpacity);
        viewModel.SelectProfile("office");
        Assert.Equal(0.55d, viewModel.FanCurveSurfaceOpacity);
        viewModel.SelectProfile("custom");
        Assert.Equal(1d, viewModel.FanCurveSurfaceOpacity);
    }

    [Theory]
    [InlineData("office", "当前：办公模式", "#22A967", "#DDF8E8")]
    [InlineData("balanced", "当前：平衡模式", "#2185F4", "#DCEEFF")]
    [InlineData("custom", "当前：自定义模式", "#D58A00", "#FFF0D8")]
    public void SelectProfile_UpdatesFanEditorModePresentation(
        string profileId,
        string expectedLabel,
        string expectedAccent,
        string expectedSurface)
    {
        var viewModel = new MainWindowViewModel(CreateDashboard());
        var changes = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changes.Add(args.PropertyName);

        viewModel.SelectProfile(profileId);

        Assert.Equal(expectedLabel, viewModel.CurrentFanProfileLabel);
        Assert.Equal(expectedAccent, viewModel.FanAccentColor);
        Assert.Equal(expectedSurface, viewModel.FanAccentSurfaceColor);
        if (profileId != "balanced")
        {
            Assert.Contains(nameof(MainWindowViewModel.CurrentFanProfileLabel), changes);
            Assert.Contains(nameof(MainWindowViewModel.FanAccentColor), changes);
            Assert.Contains(nameof(MainWindowViewModel.FanAccentSurfaceColor), changes);
        }
    }

    [Theory]
    [InlineData(TurboStrategy.Quiet, "当前：狂暴模式 · 静音")]
    [InlineData(TurboStrategy.HighEfficiency, "当前：狂暴模式 · 高能")]
    public void SelectTurboStrategy_UpdatesCurrentFanProfileLabel(
        TurboStrategy strategy,
        string expectedLabel)
    {
        var viewModel = new MainWindowViewModel(CreateDashboard());

        viewModel.SelectTurboStrategy(strategy);

        Assert.Equal(expectedLabel, viewModel.CurrentFanProfileLabel);
        Assert.Equal("#9339DB", viewModel.FanAccentColor);
        Assert.Equal("#EEDCFF", viewModel.FanAccentSurfaceColor);
    }

    [Fact]
    public void SelectProfile_ChangesLocalSelectionWithoutEnablingHardwareWrites()
    {
        var viewModel = new MainWindowViewModel(CreateDashboard());

        viewModel.SelectProfile("office");

        Assert.Equal("office", viewModel.SelectedProfileId);
        Assert.Equal(new PerformanceTradeoff(50, 30, 15), viewModel.Tradeoff);
        Assert.True(viewModel.IsReadOnlyPreview);
    }

    [Fact]
    public async Task ApplyPlatformProfileAsync_KeepsRequestedSelectionAfterHardwareReadbackSucceeds()
    {
        var controller = new RecordingPlatformModeController(new(PlatformMode.Office));
        var viewModel = CreateViewModel(platformModeController: controller);
        viewModel.SelectProfile("office");

        var succeeded = await viewModel.ApplyPlatformProfileAsync("balanced", CancellationToken.None);

        Assert.True(succeeded);
        Assert.Equal("balanced", viewModel.SelectedProfileId);
        Assert.Equal(new PlatformModeSelection(PlatformMode.Balanced), controller.LastApplied);
    }

    [Fact]
    public async Task ApplyPlatformProfileAsync_AppliesLinkedOemPowerAndBoostSchedule()
    {
        var platform = new RecordingPlatformModeController(new(PlatformMode.Office));
        var cpu = new RecordingCpuPolicyController(new(35, 55, null));
        var boost = new RecordingBoostController(
            new(ProcessorBoostMode.EfficientEnabled, ProcessorBoostMode.EfficientEnabled));
        var viewModel = CreateViewModel(cpuPolicyController: cpu, boostController: boost, platformModeController: platform);
        viewModel.ApplyCpuPowerLimitProfiles([
            new("office:1", "Mode0_Profile1", "Office", 35, true, 55, true, true),
            new("balanced:1", "Mode1_Profile1", "Balanced", 45, true, 65, true, true)
        ]);

        var succeeded = await viewModel.ApplyPlatformProfileAsync("balanced", CancellationToken.None);

        Assert.True(succeeded, viewModel.StatusMessage);
        Assert.Equal(new PlatformModeSelection(PlatformMode.Balanced), platform.LastApplied);
        Assert.Null(cpu.LastApplied);
        Assert.Equal(ProcessorBoostMode.Aggressive, boost.AppliedPolicies.Last().AcMode);
    }

    [Fact]
    public async Task ObservePlatformModeAsync_UpdatesPresentationWithoutWritingAnyPolicy()
    {
        var platform = new RecordingPlatformModeController(new(PlatformMode.Balanced));
        var cpu = new RecordingCpuPolicyController(new(35, 55, null));
        var boost = new RecordingBoostController(
            new(ProcessorBoostMode.EfficientEnabled, ProcessorBoostMode.EfficientEnabled));
        var viewModel = CreateViewModel(cpuPolicyController: cpu, boostController: boost, platformModeController: platform);
        viewModel.ApplyCpuPowerLimitProfiles([
            new("office:1", "Mode0_Profile1", "Office", 35, true, 55, true, true),
            new("balanced:1", "Mode1_Profile1", "Balanced", 45, true, 65, true, true)
        ]);
        await viewModel.InitializePlatformModeAsync(CancellationToken.None);
        platform.SetExternal(new(PlatformMode.Office));

        var succeeded = await viewModel.ObservePlatformModeAsync(
            new(PlatformMode.Office), CancellationToken.None);

        Assert.True(succeeded, viewModel.StatusMessage);
        Assert.Null(platform.LastApplied);
        Assert.Null(cpu.LastApplied);
        Assert.Empty(boost.AppliedPolicies);
        Assert.Equal("office", viewModel.SelectedProfileId);
    }

    [Fact]
    public async Task ObservePlatformModeAsync_WhenBalancedIsObserved_ClearsStaleOfficeFrequencyLimitOnly()
    {
        var platform = new RecordingPlatformModeController(new(PlatformMode.Balanced));
        var cpu = new RecordingCpuPolicyController(new(35, 55, null));
        var boost = new RecordingBoostController(
            new(ProcessorBoostMode.EfficientEnabled, ProcessorBoostMode.EfficientEnabled));
        var frequency = new RecordingFrequencyLimitController(
            ProcessorFrequencyLimitPolicy.Uniform(3900));
        var settings = new ApplicationSettings(
            PerformanceProfiles: new Dictionary<string, PerformanceProfileSettings>
            {
                ["office:1"] = new(35, 55, ProcessorBoostMode.EfficientEnabled, 3900, true),
                ["balanced:1"] = new(45, 65, ProcessorBoostMode.Aggressive, 0, false)
            });
        var viewModel = CreateViewModel(
            cpuPolicyController: cpu,
            boostController: boost,
            platformModeController: platform,
            frequencyLimitController: frequency,
            verificationDelay: (_, _) => Task.CompletedTask,
            settings: settings);

        var succeeded = await viewModel.ObservePlatformModeAsync(
            new(PlatformMode.Balanced), CancellationToken.None);

        Assert.True(succeeded, viewModel.StatusMessage);
        Assert.Equal("balanced", viewModel.SelectedProfileId);
        Assert.Equal(ProcessorFrequencyLimitPolicy.Uniform(0), frequency.LastApplied);
        Assert.Null(platform.LastApplied);
        Assert.Null(cpu.LastApplied);
        Assert.Empty(boost.AppliedPolicies);
    }

    [Fact]
    public async Task FrequencyTransition_RepeatedSameModeDoesNotPostponeReconciliation()
    {
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delays = 0;
        var frequency = new RecordingFrequencyLimitController(ProcessorFrequencyLimitPolicy.Uniform(3600));
        var vm = CreateViewModel(frequencyLimitController: frequency,
            verificationDelay: (_, _) => { delays++; return waiting.Task; });
        var first = vm.ObservePlatformModeAsync(new(PlatformMode.Balanced), CancellationToken.None);
        var duplicate = vm.ObservePlatformModeAsync(new(PlatformMode.Balanced), CancellationToken.None);
        Assert.Equal(1, delays);
        waiting.SetResult();
        await Task.WhenAll(first, duplicate);
        Assert.Equal(ProcessorFrequencyLimitPolicy.Uniform(0), frequency.LastApplied);
    }

    [Fact]
    public async Task FrequencyTransition_InFlightObservationFinishesBeforeNewManualSelection()
    {
        var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var frequency = new RecordingFrequencyLimitController(ProcessorFrequencyLimitPolicy.Uniform(0))
        {
            BeforeApply = policy =>
            {
                if (policy.AcMaximumMhz != 3600) return Task.CompletedTask;
                entered.TrySetResult();
                return blocked.Task;
            }
        };
        var platform = new RecordingPlatformModeController(new(PlatformMode.Office));
        var vm = CreateViewModel(platformModeController: platform,
            cpuPolicyController: new RecordingCpuPolicyController(new(45, 45, null)),
            boostController: new RecordingBoostController(new(ProcessorBoostMode.Aggressive, ProcessorBoostMode.Aggressive)),
            frequencyLimitController: frequency, verificationDelay: (_, _) => Task.CompletedTask,
            settings: new(PerformanceProfiles: new()
            {
                ["office:1"] = new(45, 45, ProcessorBoostMode.Aggressive, 3600, true),
                ["balanced:1"] = new(85, 85, ProcessorBoostMode.Aggressive, 0, true)
            }));
        var observation = vm.ObservePlatformModeAsync(new(PlatformMode.Office), CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var switching = vm.ApplyPlatformProfileAsync("balanced", CancellationToken.None);
        Assert.Null(platform.LastApplied);
        blocked.SetResult();
        await Task.WhenAll(observation, switching);
        Assert.Equal("balanced", vm.SelectedProfileId);
        Assert.Equal(ProcessorFrequencyLimitPolicy.Uniform(0), frequency.LastApplied);
    }

    [Fact]
    public async Task FrequencyTransition_RepeatedOfficeBalancedSwitchesRestoreEachProfileAnd85W()
    {
        var cpu = new RecordingCpuPolicyController(new(45, 45, null));
        var frequency = new RecordingFrequencyLimitController(ProcessorFrequencyLimitPolicy.Uniform(0));
        var vm = CreateViewModel(platformModeController: new RecordingPlatformModeController(new(PlatformMode.Office)),
            cpuPolicyController: cpu,
            boostController: new RecordingBoostController(new(ProcessorBoostMode.Aggressive, ProcessorBoostMode.Aggressive)),
            frequencyLimitController: frequency, verificationDelay: (_, _) => Task.CompletedTask,
            settings: new(PerformanceProfiles: new()
            {
                ["office:1"] = new(45, 45, ProcessorBoostMode.Aggressive, 3600, true),
                ["balanced:1"] = new(85, 85, ProcessorBoostMode.Aggressive, 0, true)
            }));
        vm.ApplyRuntimeCapabilities(new(vm.Dashboard.Identity,
            Enum.GetValues<HardwareFeature>().ToDictionary(feature => feature,
                feature => new HardwareFeatureCapability(feature, CapabilityAvailability.Available, "Offline evidence")),
            Enum.GetValues<PlatformMode>().ToHashSet(), []));
        for (var i = 0; i < 3; i++)
        {
            Assert.True(await vm.ApplyPlatformProfileAsync("office", CancellationToken.None));
            Assert.Equal(ProcessorFrequencyLimitPolicy.Uniform(3600), frequency.LastApplied);
            Assert.True(await vm.ApplyPlatformProfileAsync("balanced", CancellationToken.None));
            Assert.Equal(ProcessorFrequencyLimitPolicy.Uniform(0), frequency.LastApplied);
            Assert.False(vm.IsProcessorFrequencyLimitEnabled);
            Assert.Equal(new CpuPolicy(85, 85, null), cpu.LastApplied);
        }
    }

    [Fact]
    public async Task FrequencyTransition_OldEditorCannotApplyDuringBalancedSettlement()
    {
        var settling = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var platform = new RecordingPlatformModeController(new(PlatformMode.Office));
        var frequency = new RecordingFrequencyLimitController(ProcessorFrequencyLimitPolicy.Uniform(3600));
        var vm = CreateViewModel(platformModeController: platform,
            cpuPolicyController: new RecordingCpuPolicyController(new(45, 45, null)),
            boostController: new RecordingBoostController(new(ProcessorBoostMode.Aggressive, ProcessorBoostMode.Aggressive)),
            frequencyLimitController: frequency,
            verificationDelay: (duration, _) => duration == TimeSpan.FromSeconds(2) ? settling.Task : Task.CompletedTask,
            settings: new(PerformanceProfiles: new()
            {
                ["office:1"] = new(45, 45, ProcessorBoostMode.Aggressive, 3600, true),
                ["balanced:1"] = new(85, 85, ProcessorBoostMode.Aggressive, 0, true)
            }));
        await vm.InitializePlatformModeAsync(CancellationToken.None);
        var switching = vm.ApplyPlatformProfileAsync("balanced", CancellationToken.None);
        var oldEditor = vm.ApplyPerformancePolicyAsync(CancellationToken.None);
        Assert.Equal("balanced", vm.SelectedProfileId);
        settling.SetResult();
        await Task.WhenAll(switching, oldEditor);
        Assert.Equal(new PlatformModeSelection(PlatformMode.Balanced), await platform.ReadPlatformModeAsync(CancellationToken.None));
        Assert.Equal("balanced", vm.SelectedProfileId);
        Assert.False(vm.IsProcessorFrequencyLimitEnabled);
        Assert.All(frequency.AppliedPolicies, policy => Assert.Equal(ProcessorFrequencyLimitPolicy.Uniform(0), policy));
    }

    [Fact]
    public async Task ObservePlatformModeAsync_RapidModeChange_DiscardsOlderDelayedFrequencyWrite()
    {
        var firstDelay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondDelay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delayCall = 0;
        Task Delay(TimeSpan ignoredDelay, CancellationToken ignoredToken) => Interlocked.Increment(ref delayCall) switch
        {
            1 => firstDelay.Task,
            2 => secondDelay.Task,
            _ => Task.CompletedTask
        };
        var frequency = new RecordingFrequencyLimitController(
            ProcessorFrequencyLimitPolicy.Uniform(4200));
        var settings = new ApplicationSettings(
            PerformanceProfiles: new Dictionary<string, PerformanceProfileSettings>
            {
                ["office:1"] = new(35, 55, ProcessorBoostMode.EfficientEnabled, 3900, true),
                ["balanced:1"] = new(45, 65, ProcessorBoostMode.Aggressive, 0, false)
            });
        var viewModel = CreateViewModel(
            frequencyLimitController: frequency,
            verificationDelay: Delay,
            settings: settings);

        var officeObservation = viewModel.ObservePlatformModeAsync(
            new(PlatformMode.Office), CancellationToken.None);
        var balancedObservation = viewModel.ObservePlatformModeAsync(
            new(PlatformMode.Balanced), CancellationToken.None);

        firstDelay.SetResult();
        Assert.True(await officeObservation);
        Assert.Empty(frequency.AppliedPolicies);

        secondDelay.SetResult();
        Assert.True(await balancedObservation);
        Assert.Equal([ProcessorFrequencyLimitPolicy.Uniform(0)], frequency.AppliedPolicies);
        Assert.Equal("balanced", viewModel.SelectedProfileId);
    }

    [Fact]
    public async Task ObservePlatformModeAsync_WithoutRuntimeEvidence_ObservesButDoesNotWritePolicy()
    {
        var platform = new RecordingPlatformModeController(new(PlatformMode.Office));
        var cpu = new RecordingCpuPolicyController(new(35, 55, null));
        var boost = new RecordingBoostController(
            new(ProcessorBoostMode.EfficientEnabled, ProcessorBoostMode.EfficientEnabled));
        var viewModel = CreateViewModel(cpuPolicyController: cpu, boostController: boost, platformModeController: platform);
        var unavailable = Enum.GetValues<HardwareFeature>().ToDictionary(
            feature => feature,
            feature => new HardwareFeatureCapability(feature, CapabilityAvailability.Unverified, "not probed"));
        viewModel.ApplyRuntimeCapabilities(new(
            CreateDashboard().Identity,
            unavailable,
            new HashSet<PlatformMode>(),
            []));

        var succeeded = await viewModel.ObservePlatformModeAsync(
            new(PlatformMode.Office), CancellationToken.None);

        Assert.True(succeeded, viewModel.StatusMessage);
        Assert.Equal("office", viewModel.SelectedProfileId);
        Assert.Null(platform.LastApplied);
        Assert.Null(cpu.LastApplied);
        Assert.Empty(boost.AppliedPolicies);
    }

    [Fact]
    public async Task SynchronizeControlSessionAsync_ReadsCurrentModeWithoutWritingAnyPolicy()
    {
        var platform = new RecordingPlatformModeController(new(PlatformMode.Office));
        var cpu = new RecordingCpuPolicyController(new(35, 55, null));
        var boost = new RecordingBoostController(
            new(ProcessorBoostMode.EfficientEnabled, ProcessorBoostMode.EfficientEnabled));
        var viewModel = CreateViewModel(cpuPolicyController: cpu, boostController: boost, platformModeController: platform);
        viewModel.ApplyCpuPowerLimitProfiles([
            new("office:1", "Mode0_Profile1", "Office", 35, true, 55, true, true)
        ]);
        await viewModel.InitializePlatformModeAsync(CancellationToken.None);
        cpu.SetExternal(new(70, 80, null));

        var succeeded = await viewModel.SynchronizeControlSessionAsync(CancellationToken.None);

        Assert.True(succeeded, viewModel.StatusMessage);
        Assert.Null(cpu.LastApplied);
        Assert.Null(platform.LastApplied);
        Assert.Empty(boost.AppliedPolicies);
    }

    [Fact]
    public async Task ApplyPlatformProfileAsync_WhenHardwareRejects_KeepsPriorSelection()
    {
        var controller = new RecordingPlatformModeController(new(PlatformMode.Office)) { RejectApply = true };
        var viewModel = CreateViewModel(platformModeController: controller);
        viewModel.SelectProfile("office");

        var succeeded = await viewModel.ApplyPlatformProfileAsync("balanced", CancellationToken.None);

        Assert.False(succeeded);
        Assert.Equal("office", viewModel.SelectedProfileId);
        Assert.Contains("simulated mode failure", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(TurboStrategy.Quiet, PlatformMode.TurboQuiet)]
    [InlineData(TurboStrategy.HighEfficiency, PlatformMode.TurboHigh)]
    public async Task ApplyTurboStrategyAsync_UsesMatchingHardwareSubMode(
        TurboStrategy strategy,
        PlatformMode expectedMode)
    {
        var controller = new RecordingPlatformModeController(new(PlatformMode.Office));
        var viewModel = CreateViewModel(platformModeController: controller);

        var succeeded = await viewModel.ApplyTurboStrategyAsync(strategy, CancellationToken.None);

        Assert.True(succeeded);
        Assert.Equal(new PlatformModeSelection(expectedMode), controller.LastApplied);
        Assert.Equal("turbo", viewModel.SelectedProfileId);
        Assert.Equal(strategy, viewModel.SelectedTurboStrategy);
    }

    [Fact]
    public async Task InitializePlatformModeAsync_UsesLiveOemModeInsteadOfBalancedDefault()
    {
        var controller = new RecordingPlatformModeController(new(PlatformMode.Office));
        var viewModel = CreateViewModel(platformModeController: controller);

        var succeeded = await viewModel.InitializePlatformModeAsync(CancellationToken.None);

        Assert.True(succeeded);
        Assert.Equal("office", viewModel.SelectedProfileId);
    }

    [Fact]
    public async Task InitializePlatformModeAsync_WhenReadFails_ReturnsFalseAndKeepsSelection()
    {
        var controller = new RecordingPlatformModeController(new(PlatformMode.Office)) { ThrowOnRead = true };
        var viewModel = CreateViewModel(platformModeController: controller);

        var succeeded = await viewModel.InitializePlatformModeAsync(CancellationToken.None);

        Assert.False(succeeded);
        Assert.Equal("balanced", viewModel.SelectedProfileId);
    }

    [Fact]
    public void SelectTurboStrategy_KeepsTurboAsOneProfileAndChangesItsReferenceShape()
    {
        var viewModel = new MainWindowViewModel(CreateDashboard());

        viewModel.SelectTurboStrategy(TurboStrategy.HighEfficiency);

        Assert.Equal("turbo", viewModel.SelectedProfileId);
        Assert.Equal(TurboStrategy.HighEfficiency, viewModel.SelectedTurboStrategy);
        Assert.Equal(new PerformanceTradeoff(100, 70, 80), viewModel.Tradeoff);
    }

    [Fact]
    public void CustomProfile_HidesTheReferenceShape()
    {
        var viewModel = new MainWindowViewModel(CreateDashboard());

        viewModel.SelectProfile("custom");

        Assert.False(viewModel.IsTradeoffVisible);
    }

    [Fact]
    public void OpenFanCurve_ReportsThatItIsWaitingForReadback()
    {
        var viewModel = new MainWindowViewModel(CreateDashboard());

        viewModel.OpenFanCurve();

        Assert.Contains("读取", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.True(viewModel.IsDrawerOpen);
        Assert.Equal(HomeDrawer.Fan, viewModel.ActiveDrawer);
        Assert.Equal("风扇调整", viewModel.DrawerTitle);
    }

    [Fact]
    public void ApplyCpuPowerLimitProfiles_LoadsOemReadbackButKeepsHardwareApplyLocked()
    {
        var viewModel = new MainWindowViewModel(CreateDashboard());

        viewModel.ApplyCpuPowerLimitProfiles([
            new("balanced:1", "Mode1_Profile1", "Balanced", 75, false, 85, false, true)
        ]);

        Assert.Equal(75, viewModel.SelectedPl1Watts);
        Assert.Equal(85, viewModel.SelectedPl2Watts);
        Assert.Contains("OEM", viewModel.PerformanceReadbackText, StringComparison.Ordinal);
        Assert.Contains("未启用", viewModel.PerformanceReadbackText, StringComparison.Ordinal);
        Assert.True(viewModel.IsPerformanceDraftValid);
        Assert.False(viewModel.IsPerformanceApplyEnabled);
    }

    [Fact]
    public async Task InitializePerformancePolicy_ReadsLivePowerAndWindowsBoostMode()
    {
        var cpu = new RecordingCpuPolicyController(new CpuPolicy(35, 55, null));
        var boost = new RecordingBoostController(
            new ProcessorBoostPolicy(ProcessorBoostMode.Aggressive, ProcessorBoostMode.Aggressive));
        var viewModel = CreateViewModel(cpuPolicyController: cpu, boostController: boost);
        viewModel.SelectCustomProfile(1);

        await viewModel.InitializePerformancePolicyAsync(CancellationToken.None);

        Assert.Equal(35, viewModel.SelectedPl1Watts);
        Assert.Equal(55, viewModel.SelectedPl2Watts);
        Assert.Equal(ProcessorBoostMode.Aggressive, viewModel.SelectedProcessorBoostMode);
        Assert.True(viewModel.IsPerformanceApplyEnabled);
        Assert.Contains("GCU 配置回读", viewModel.PerformanceReadbackText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InitializePerformancePolicy_OfficeKeepsModelProfilePowerAndHighEfficiencyDefault()
    {
        var cpu = new RecordingCpuPolicyController(new CpuPolicy(70, 80, null));
        var boost = new RecordingBoostController(
            new ProcessorBoostPolicy(ProcessorBoostMode.Aggressive, ProcessorBoostMode.Aggressive));
        var viewModel = CreateViewModel(cpuPolicyController: cpu, boostController: boost);
        viewModel.ApplyCpuPowerLimitProfiles([
            new("office:1", "Mode0_Profile1", "Office", 35, true, 55, true, true)
        ]);
        viewModel.SelectProfile("office");

        await viewModel.InitializePerformancePolicyAsync(CancellationToken.None);

        Assert.Equal(35, viewModel.SelectedPl1Watts);
        Assert.Equal(55, viewModel.SelectedPl2Watts);
        Assert.Equal(ProcessorBoostMode.EfficientEnabled, viewModel.SelectedProcessorBoostMode);
    }

    [Fact]
    public async Task ApplyPerformancePolicy_AppliesBoostAndPowerThenReportsVerifiedReadback()
    {
        var cpu = new RecordingCpuPolicyController(new CpuPolicy(35, 55, null));
        var boost = new RecordingBoostController(
            new ProcessorBoostPolicy(ProcessorBoostMode.Aggressive, ProcessorBoostMode.Aggressive));
        var viewModel = CreateViewModel(cpuPolicyController: cpu, boostController: boost);
        viewModel.SelectCustomProfile(1);
        await viewModel.InitializePerformancePolicyAsync(CancellationToken.None);
        viewModel.SelectedPl1Watts = 45;
        viewModel.SelectedPl2Watts = 65;
        viewModel.SelectedProcessorBoostMode = ProcessorBoostMode.Disabled;

        await viewModel.ApplyPerformancePolicyAsync(CancellationToken.None);

        Assert.Equal(new CpuPolicy(45, 65, null), cpu.LastApplied);
        Assert.Equal(
            new ProcessorBoostPolicy(ProcessorBoostMode.Disabled, ProcessorBoostMode.Disabled),
            boost.AppliedPolicies.Single());
        Assert.Contains("已应用并回读", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyPerformancePolicy_InFirmwarePreset_SkipsMisleadingOemPowerLimitWrite()
    {
        var platform = new RecordingPlatformModeController(new(PlatformMode.Balanced));
        var cpu = new RecordingCpuPolicyController(new CpuPolicy(75, 85, null));
        var boost = new RecordingBoostController(
            new ProcessorBoostPolicy(ProcessorBoostMode.Aggressive, ProcessorBoostMode.Aggressive));
        var viewModel = CreateViewModel(
            cpuPolicyController: cpu,
            boostController: boost,
            platformModeController: platform,
            frequencyLimitController: new RecordingFrequencyLimitController(
                ProcessorFrequencyLimitPolicy.Uniform(0)),
            verificationDelay: (_, _) => Task.CompletedTask);
        viewModel.ApplyCpuPowerLimitProfiles([
            new("balanced:1", "Mode1_Profile1", "Balanced", 75, true, 85, true, true)
        ]);
        viewModel.SelectedProcessorBoostMode = ProcessorBoostMode.EfficientEnabled;

        Assert.True(viewModel.IsPerformanceApplyEnabled);

        await viewModel.ApplyPerformancePolicyAsync(CancellationToken.None);

        Assert.Null(platform.LastApplied);
        Assert.Null(cpu.LastApplied);
        Assert.Contains("固件预设", viewModel.PerformanceReadbackText, StringComparison.Ordinal);
        Assert.Contains("非实际硬件 PL", viewModel.PerformanceReadbackText, StringComparison.Ordinal);
        Assert.Contains("已应用", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyPerformancePolicy_InPresetWithVerifiedIntelRapl_WritesDirectPowerLimits()
    {
        var platform = new RecordingPlatformModeController(new(PlatformMode.Balanced));
        var cpu = new RecordingCpuPolicyController(new CpuPolicy(75, 85, null));
        var boost = new RecordingBoostController(
            new ProcessorBoostPolicy(ProcessorBoostMode.Aggressive, ProcessorBoostMode.Aggressive));
        var viewModel = CreateViewModel(
            cpuPolicyController: cpu,
            boostController: boost,
            platformModeController: platform,
            frequencyLimitController: new RecordingFrequencyLimitController(
                ProcessorFrequencyLimitPolicy.Uniform(0)),
            verificationDelay: (_, _) => Task.CompletedTask);
        var features = Enum.GetValues<HardwareFeature>().ToDictionary(
            feature => feature,
            feature => new HardwareFeatureCapability(
                feature,
                feature is HardwareFeature.OemPlatformModes or
                    HardwareFeature.OemCpuPowerLimits or
                    HardwareFeature.WindowsProcessorPolicy or
                    HardwareFeature.IntelRapl
                    ? CapabilityAvailability.Available
                    : CapabilityAvailability.Unavailable,
                "test"));
        viewModel.ApplyRuntimeCapabilities(new(
            CreateDashboard().Identity,
            features,
            new HashSet<PlatformMode> { PlatformMode.Balanced, PlatformMode.Custom },
            [new(CpuControlBackend.IntelRapl, CapabilityAvailability.Available, "verified")]));
        viewModel.ApplyCpuPowerLimitProfiles([
            new("balanced:1", "Mode1_Profile1", "Balanced", 75, true, 85, true, true)
        ]);
        viewModel.SelectedPl1Watts = 55;
        viewModel.SelectedPl2Watts = 65;

        Assert.True(viewModel.CanEditDirectPowerLimits);
        Assert.True(viewModel.CanEditPowerLimits);
        Assert.True(viewModel.IsPerformanceApplyEnabled);

        await viewModel.ApplyPerformancePolicyAsync(CancellationToken.None);

        Assert.Equal(new CpuPolicy(55, 65, null), cpu.LastApplied);
        Assert.Contains("PL1 55 W", viewModel.PerformanceReadbackText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyPerformancePolicy_WithUnifiedScheduler_UpdatesCurrentPolicyWithoutReloadingOemMode()
    {
        var store = new RecordingSettingsStore();
        var platform = new RecordingPlatformModeController(new(PlatformMode.Office));
        var cpu = new RecordingCpuPolicyController(new CpuPolicy(75, 85, null));
        var boost = new RecordingBoostController(
            new ProcessorBoostPolicy(ProcessorBoostMode.EfficientEnabled, ProcessorBoostMode.EfficientEnabled));
        var frequency = new RecordingFrequencyLimitController(ProcessorFrequencyLimitPolicy.Uniform(0));
        var viewModel = CreateViewModel(store, cpuPolicyController: cpu, boostController: boost,
            platformModeController: platform, frequencyLimitController: frequency,
            verificationDelay: (_, _) => Task.CompletedTask);
        await viewModel.InitializePlatformModeAsync(CancellationToken.None);
        await viewModel.InitializePerformancePolicyAsync(CancellationToken.None);
        viewModel.SelectCustomProfile(1);
        viewModel.SelectedPl1Watts = 35;
        viewModel.SelectedPl2Watts = 35;
        viewModel.SelectedProcessorBoostMode = ProcessorBoostMode.Disabled;
        viewModel.IsProcessorFrequencyLimitEnabled = true;
        viewModel.SelectedMaximumProcessorFrequencyMhz = 3100;

        await viewModel.ApplyPerformancePolicyAsync(CancellationToken.None);

        Assert.Equal(new PlatformModeSelection(PlatformMode.Custom, 1), platform.LastApplied);
        Assert.Equal(new CpuPolicy(35, 35, null), cpu.LastApplied);
        Assert.Equal(
            new ProcessorBoostPolicy(ProcessorBoostMode.Disabled, ProcessorBoostMode.Disabled),
            boost.AppliedPolicies.Last());
        Assert.Equal(ProcessorFrequencyLimitPolicy.Uniform(3100), frequency.LastApplied);
        Assert.Contains("PL1 35 W", viewModel.PerformanceReadbackText, StringComparison.Ordinal);
        Assert.Contains("PL2 35 W", viewModel.PerformanceReadbackText, StringComparison.Ordinal);
        Assert.Contains("Disabled", viewModel.PerformanceReadbackText, StringComparison.Ordinal);
        var saved = Assert.IsType<ApplicationSettings>(store.LastSaved);
        Assert.Equal(new PerformanceProfileSettings(35, 35, ProcessorBoostMode.Disabled, 3100,
                MaximumProcessorFrequencyConfigured: true),
            saved.PerformanceProfiles!["custom:1"]);
        Assert.Contains("1 秒复查", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyPerformancePolicy_WhenFrequencyIsOverwrittenAfterInitialReadback_ReportsInterference()
    {
        var platform = new RecordingPlatformModeController(new(PlatformMode.Office));
        var frequency = new RecordingFrequencyLimitController(ProcessorFrequencyLimitPolicy.Uniform(0))
        {
            OverrideOnSecondReadAfterApply = ProcessorFrequencyLimitPolicy.Uniform(0)
        };
        var viewModel = CreateViewModel(
            cpuPolicyController: new RecordingCpuPolicyController(new(45, 55, null)),
            boostController: new RecordingBoostController(
                new(ProcessorBoostMode.EfficientEnabled, ProcessorBoostMode.EfficientEnabled)),
            platformModeController: platform,
            frequencyLimitController: frequency,
            verificationDelay: (_, _) => Task.CompletedTask);
        await viewModel.InitializePlatformModeAsync(CancellationToken.None);
        await viewModel.InitializePerformancePolicyAsync(CancellationToken.None);
        viewModel.IsProcessorFrequencyLimitEnabled = true;
        viewModel.SelectedMaximumProcessorFrequencyMhz = 3100;

        await viewModel.ApplyPerformancePolicyAsync(CancellationToken.None);

        Assert.Contains("写入后被覆盖", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("C0 0", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyPerformancePolicy_WhenPowerLimitsAreOverwrittenAfterInitialReadback_ReportsInterference()
    {
        var platform = new RecordingPlatformModeController(new(PlatformMode.Office));
        var cpu = new RecordingCpuPolicyController(new(75, 85, null))
        {
            OverrideOnSecondReadAfterApply = new(75, 85, null)
        };
        var viewModel = CreateViewModel(
            cpuPolicyController: cpu,
            boostController: new RecordingBoostController(
                new(ProcessorBoostMode.EfficientEnabled, ProcessorBoostMode.EfficientEnabled)),
            platformModeController: platform,
            frequencyLimitController: new RecordingFrequencyLimitController(
                ProcessorFrequencyLimitPolicy.Uniform(0)),
            verificationDelay: (_, _) => Task.CompletedTask);
        await viewModel.InitializePlatformModeAsync(CancellationToken.None);
        await viewModel.InitializePerformancePolicyAsync(CancellationToken.None);
        viewModel.SelectCustomProfile(1);
        viewModel.SelectedPl1Watts = 35;
        viewModel.SelectedPl2Watts = 45;

        await viewModel.ApplyPerformancePolicyAsync(CancellationToken.None);

        Assert.Contains("写入后被覆盖", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("PL1 75 W", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("PL2 85 W", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ProcessorBoostMode.Disabled, "Disabled")]
    [InlineData(ProcessorBoostMode.Enabled, "Enabled")]
    [InlineData(ProcessorBoostMode.Aggressive, "Aggressive")]
    [InlineData(ProcessorBoostMode.EfficientEnabled, "Efficient Enabled")]
    [InlineData(ProcessorBoostMode.EfficientAggressive, "Efficient Aggressive")]
    [InlineData(ProcessorBoostMode.AggressiveAtGuaranteed, "Aggressive At Guaranteed")]
    [InlineData(ProcessorBoostMode.EfficientAggressiveAtGuaranteed, "Efficient Aggressive At Guaranteed")]
    public void ProcessorBoostModes_UseWindowsEnglishNames(ProcessorBoostMode mode, string label)
    {
        var viewModel = new MainWindowViewModel(CreateDashboard());

        Assert.Contains(viewModel.ProcessorBoostModes,
            option => option.Mode == mode && option.Label == label);
    }

    [Fact]
    public void UpdateDashboard_ReplacesStartupPlaceholderAfterWindowIsAlreadyAvailable()
    {
        var placeholder = HomeDashboardFactory.Create(
            new MachineIdentity("MECHREVO", "正在识别设备", string.Empty, "正在识别处理器", string.Empty),
            []);
        var viewModel = new MainWindowViewModel(placeholder);
        var detected = CreateDashboard();

        viewModel.UpdateDashboard(detected);

        Assert.Equal("YAOSHI Series", viewModel.MachineName);
        Assert.Equal(detected.Devices, viewModel.Devices);
    }

    [Fact]
    public async Task ApplyPerformancePolicy_WhenPowerFails_RestoresPreviousBoostPolicy()
    {
        var original = new ProcessorBoostPolicy(ProcessorBoostMode.Aggressive, ProcessorBoostMode.Aggressive);
        var cpu = new RecordingCpuPolicyController(new CpuPolicy(35, 55, null)) { RejectApply = true };
        var boost = new RecordingBoostController(original);
        var viewModel = CreateViewModel(cpuPolicyController: cpu, boostController: boost);
        viewModel.SelectCustomProfile(1);
        await viewModel.InitializePerformancePolicyAsync(CancellationToken.None);
        viewModel.SelectedProcessorBoostMode = ProcessorBoostMode.Disabled;

        await viewModel.ApplyPerformancePolicyAsync(CancellationToken.None);

        Assert.Equal(2, boost.AppliedPolicies.Count);
        Assert.Equal(original, boost.AppliedPolicies[1]);
        Assert.Contains("已恢复睿频策略", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ProcessorBoostDrafts_AreIndependentAcrossPerformanceProfiles()
    {
        var viewModel = new MainWindowViewModel(CreateDashboard());
        viewModel.SelectedProcessorBoostMode = ProcessorBoostMode.EfficientEnabled;

        viewModel.SelectProfile("office");
        viewModel.SelectedProcessorBoostMode = ProcessorBoostMode.Disabled;
        viewModel.SelectTurboStrategy(TurboStrategy.HighEfficiency);
        viewModel.SelectedProcessorBoostMode = ProcessorBoostMode.AggressiveAtGuaranteed;

        viewModel.SelectProfile("balanced");
        Assert.Equal(ProcessorBoostMode.EfficientEnabled, viewModel.SelectedProcessorBoostMode);
        viewModel.SelectProfile("office");
        Assert.Equal(ProcessorBoostMode.Disabled, viewModel.SelectedProcessorBoostMode);
        viewModel.SelectTurboStrategy(TurboStrategy.HighEfficiency);
        Assert.Equal(ProcessorBoostMode.AggressiveAtGuaranteed, viewModel.SelectedProcessorBoostMode);
    }

    [Fact]
    public void PowerLimitDrafts_AreIndependentAcrossProfilesStrategiesAndCustomSlots()
    {
        var viewModel = new MainWindowViewModel(CreateDashboard());
        viewModel.ApplyCpuPowerLimitProfiles([
            new("balanced:1", "Mode1_Profile1", "Balanced", 75, false, 85, false, true),
            new("office:1", "Mode2_Profile1", "Office", 45, false, 45, false, true),
            new("turbo:1", "Mode3_Profile1", "Turbo", 100, false, 120, false, true),
            new("custom:1", "Mode4_Profile1", "Custom 1", 70, true, 80, true, true),
            new("custom:2", "Mode4_Profile2", "Custom 2", 65, true, 90, true, true)
        ]);

        viewModel.SelectedPl1Watts = 72;
        viewModel.SelectProfile("office");
        viewModel.SelectedPl1Watts = 35;
        viewModel.SelectTurboStrategy(TurboStrategy.Quiet);
        viewModel.SelectedPl1Watts = 82;
        viewModel.SelectTurboStrategy(TurboStrategy.HighEfficiency);
        viewModel.SelectedPl1Watts = 96;
        viewModel.SelectProfile("custom");
        viewModel.SelectCustomProfile(1);
        viewModel.SelectedPl1Watts = 61;
        viewModel.SelectCustomProfile(2);
        viewModel.SelectedPl1Watts = 64;

        viewModel.SelectProfile("balanced");
        Assert.Equal(72, viewModel.SelectedPl1Watts);
        viewModel.SelectProfile("office");
        Assert.Equal(35, viewModel.SelectedPl1Watts);
        viewModel.SelectTurboStrategy(TurboStrategy.Quiet);
        Assert.Equal(82, viewModel.SelectedPl1Watts);
        viewModel.SelectTurboStrategy(TurboStrategy.HighEfficiency);
        Assert.Equal(96, viewModel.SelectedPl1Watts);
        viewModel.SelectProfile("custom");
        viewModel.SelectCustomProfile(1);
        Assert.Equal(61, viewModel.SelectedPl1Watts);
        viewModel.SelectCustomProfile(2);
        Assert.Equal(64, viewModel.SelectedPl1Watts);
    }

    [Fact]
    public void PowerLimitDraft_ClampsRangeAndKeepsPl1AtOrBelowPl2()
    {
        var viewModel = new MainWindowViewModel(CreateDashboard());
        viewModel.ApplyCpuPowerLimitProfiles([
            new("balanced:1", "Mode1_Profile1", "Balanced", 75, true, 85, true, true)
        ]);

        viewModel.SelectedPl1Watts = 400;
        Assert.Equal(85, viewModel.SelectedPl1Watts);
        viewModel.SelectedPl2Watts = 3;
        Assert.Equal(85, viewModel.SelectedPl2Watts);
        Assert.True(viewModel.IsPerformanceDraftValid);
        Assert.False(viewModel.IsPerformanceApplyEnabled);
    }

    [Fact]
    public void OpenPerformance_UsesPerformanceDrawerAndCurrentAccent()
    {
        var viewModel = new MainWindowViewModel(CreateDashboard());
        viewModel.SelectProfile("office");

        viewModel.OpenPerformance();

        Assert.Equal(HomeDrawer.Performance, viewModel.ActiveDrawer);
        Assert.Equal("性能调整", viewModel.DrawerTitle);
        Assert.Equal(viewModel.FanAccentColor, viewModel.PerformanceAccentColor);
    }

    [Fact]
    public void SelectCustomProfile_WhenSlotIsAlreadySelected_StillEntersCustomMode()
    {
        var viewModel = new MainWindowViewModel(CreateDashboard());
        Assert.Equal("balanced", viewModel.SelectedProfileId);
        Assert.Equal(1, viewModel.SelectedCustomProfileIndex);

        viewModel.SelectCustomProfile(1);

        Assert.Equal("custom", viewModel.SelectedProfileId);
    }

    [Fact]
    public void OpenSettings_ThenCloseDrawer_UpdatesDrawerState()
    {
        var viewModel = new MainWindowViewModel(CreateDashboard());

        viewModel.OpenSettings();

        Assert.True(viewModel.IsDrawerOpen);
        Assert.Equal(HomeDrawer.Settings, viewModel.ActiveDrawer);
        Assert.Equal("设置", viewModel.DrawerTitle);

        viewModel.CloseDrawer();

        Assert.False(viewModel.IsDrawerOpen);
        Assert.Equal(HomeDrawer.None, viewModel.ActiveDrawer);
    }

    [Fact]
    public async Task OpenGpuOutput_InitializesReadbackAndUsesDedicatedDrawer()
    {
        var gpu = new RecordingGpuOutputModeController(new(
            GpuOutputMode.Hybrid,
            new(true, true, true)));
        var viewModel = CreateViewModel(gpuOutputModeController: gpu);

        await viewModel.OpenGpuOutputAsync(CancellationToken.None);

        Assert.Equal(HomeDrawer.GpuOutput, viewModel.ActiveDrawer);
        Assert.Equal(GpuOutputMode.Hybrid, viewModel.SelectedGpuOutputMode);
        Assert.True(viewModel.SupportsIntegratedGpuMode);
        Assert.Contains("混合输出", viewModel.GpuOutputStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyGpuOutputMode_UsesExplicitSelectionAndReportsRestartRequirement()
    {
        var gpu = new RecordingGpuOutputModeController(new(
            GpuOutputMode.Hybrid,
            new(true, true, true)));
        var viewModel = CreateViewModel(gpuOutputModeController: gpu);
        await viewModel.OpenGpuOutputAsync(CancellationToken.None);
        viewModel.SelectedGpuOutputMode = GpuOutputMode.Integrated;

        await viewModel.ApplyGpuOutputModeAsync(CancellationToken.None);

        Assert.Equal(GpuOutputMode.Integrated, gpu.LastApplied);
        Assert.Contains("重启", viewModel.GpuOutputStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void AlwaysOnTop_PersistsTheChangedPreference()
    {
        var store = new RecordingSettingsStore();
        var viewModel = CreateViewModel(store: store);

        viewModel.AlwaysOnTop = true;

        Assert.True(viewModel.AlwaysOnTop);
        Assert.True(Assert.IsType<ApplicationSettings>(store.LastSaved).AlwaysOnTop);
    }

    [Fact]
    public void StartWithWindows_UpdatesRegistrationAndPersists()
    {
        var store = new RecordingSettingsStore();
        var startup = new RecordingStartupRegistration();
        var viewModel = CreateViewModel(store, startup);

        viewModel.StartWithWindows = true;

        Assert.True(startup.LastEnabled);
        Assert.True(Assert.IsType<ApplicationSettings>(store.LastSaved).StartWithWindows);
    }

    [Fact]
    public void StartWithWindows_WhenPersistenceFails_RollsBackRegistrationAndKeepsState()
    {
        var store = new RecordingSettingsStore { ThrowOnSave = true };
        var startup = new RecordingStartupRegistration();
        var viewModel = CreateViewModel(store, startup);

        viewModel.StartWithWindows = true;

        Assert.False(viewModel.StartWithWindows);
        Assert.Equal([true, false], startup.SetRequests);
        Assert.Contains("未修改", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void StartWithWindows_WhenWindowsDoesNotConfirmRegistration_DoesNotPersistOrChangeToggle()
    {
        var store = new RecordingSettingsStore();
        var startup = new RecordingStartupRegistration { ForcedReadState = false };
        var viewModel = CreateViewModel(store, startup);

        viewModel.StartWithWindows = true;

        Assert.False(viewModel.StartWithWindows);
        Assert.Null(store.LastSaved);
        Assert.Equal([true], startup.SetRequests);
        Assert.Contains("未确认", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void AlwaysOnTop_WhenPersistenceFails_DoesNotThrowOrChangeState()
    {
        var store = new RecordingSettingsStore { ThrowOnSave = true };
        var viewModel = CreateViewModel(store: store);

        var exception = Record.Exception(() => viewModel.AlwaysOnTop = true);

        Assert.Null(exception);
        Assert.False(viewModel.AlwaysOnTop);
        Assert.Contains("保存失败", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void SaveWindowPosition_WhenPersistenceFails_DoesNotThrowOrReplaceCoordinates()
    {
        var store = new RecordingSettingsStore { ThrowOnSave = true };
        var viewModel = new MainWindowViewModel(
            CreateDashboard(),
            new ApplicationSettings(RememberWindowPosition: true, WindowLeft: 10, WindowTop: 20),
            store,
            new RecordingStartupRegistration());

        var exception = Record.Exception(() => viewModel.SaveWindowPosition(300, 400));

        Assert.Null(exception);
        Assert.Equal(10, viewModel.SavedWindowLeft);
        Assert.Equal(20, viewModel.SavedWindowTop);
    }

    [Fact]
    public void SaveWindowPosition_WhenEnabled_PersistsCoordinates()
    {
        var store = new RecordingSettingsStore();
        var viewModel = CreateViewModel(store: store);
        viewModel.RememberWindowPosition = true;

        viewModel.SaveWindowPosition(120.5, 240.25);

        var saved = Assert.IsType<ApplicationSettings>(store.LastSaved);
        Assert.Equal(120.5, saved.WindowLeft);
        Assert.Equal(240.25, saved.WindowTop);
    }

    [Fact]
    public void SaveWindowPosition_WhenDisabled_DoesNotReplaceStoredCoordinates()
    {
        var store = new RecordingSettingsStore();
        var viewModel = new MainWindowViewModel(
            CreateDashboard(),
            new ApplicationSettings(WindowLeft: 10, WindowTop: 20),
            store,
            new RecordingStartupRegistration());

        viewModel.SaveWindowPosition(300, 400);

        Assert.Null(store.LastSaved);
        Assert.Equal(10, viewModel.SavedWindowLeft);
        Assert.Equal(20, viewModel.SavedWindowTop);
    }

    [Fact]
    public async Task InitializePerformancePolicyAsync_DoesNotAdoptLiveFrequencyLimitAsUserIntent()
    {
        var frequency = new RecordingFrequencyLimitController(
            new(3100, 2200, 3100, 2200, 3100, 2200));
        var viewModel = CreateViewModel(
            cpuPolicyController: new RecordingCpuPolicyController(new(35, 55, null)),
            boostController: new RecordingBoostController(
                new(ProcessorBoostMode.EfficientEnabled, ProcessorBoostMode.EfficientEnabled)),
            frequencyLimitController: frequency);

        await viewModel.InitializePerformancePolicyAsync(CancellationToken.None);

        Assert.False(viewModel.IsProcessorFrequencyLimitEnabled);
        Assert.Contains("C0 3100", viewModel.ProcessorFrequencyLimitStatusText, StringComparison.Ordinal);
        Assert.Contains("C1 3100", viewModel.ProcessorFrequencyLimitStatusText, StringComparison.Ordinal);
        Assert.Contains("C2 3100", viewModel.ProcessorFrequencyLimitStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyPlatformProfileAsync_ReassertsFrequencyAfterDelayedOemOverwrite()
    {
        var platform = new RecordingPlatformModeController(new(PlatformMode.Office));
        var frequency = new RecordingFrequencyLimitController(ProcessorFrequencyLimitPolicy.Uniform(4300))
        {
            OverrideOnSecondReadAfterApply = ProcessorFrequencyLimitPolicy.Uniform(4300)
        };
        var viewModel = CreateViewModel(
            cpuPolicyController: new RecordingCpuPolicyController(new(45, 65, null)),
            boostController: new RecordingBoostController(
                new(ProcessorBoostMode.Aggressive, ProcessorBoostMode.Aggressive)),
            platformModeController: platform,
            frequencyLimitController: frequency,
            verificationDelay: (_, _) => Task.CompletedTask);

        var succeeded = await viewModel.ApplyPlatformProfileAsync("balanced", CancellationToken.None);

        Assert.True(succeeded, viewModel.StatusMessage);
        Assert.Equal(2, frequency.AppliedPolicies.Count);
        Assert.All(frequency.AppliedPolicies, policy =>
            Assert.Equal(ProcessorFrequencyLimitPolicy.Uniform(0), policy));
        Assert.Contains("延迟覆盖", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(999, 1000)]
    [InlineData(1000, 1000)]
    [InlineData(6000, 6000)]
    [InlineData(6001, 6000)]
    public void SelectedMaximumProcessorFrequencyMhz_ClampsToSupportedRange(int requested, int expected)
    {
        var viewModel = new MainWindowViewModel(CreateDashboard());

        viewModel.SelectedMaximumProcessorFrequencyMhz = requested;

        Assert.Equal(expected, viewModel.SelectedMaximumProcessorFrequencyMhz);
    }

    [Fact]
    public async Task InitializePerformancePolicyAsync_PreservesSavedFrequencyDraftInsteadOfReplacingItWithLiveValue()
    {
        var settings = new ApplicationSettings(PerformanceProfiles:
            new Dictionary<string, PerformanceProfileSettings>
            {
                ["balanced:1"] = new(45, 65, ProcessorBoostMode.Aggressive, 3100,
                    MaximumProcessorFrequencyConfigured: true)
            });
        var frequency = new RecordingFrequencyLimitController(ProcessorFrequencyLimitPolicy.Uniform(4200));
        var viewModel = new MainWindowViewModel(
            CreateDashboard(), settings, new RecordingSettingsStore(), new RecordingStartupRegistration(),
            new RecordingCpuPolicyController(new(45, 65, null)),
            new RecordingBoostController(new(ProcessorBoostMode.Aggressive, ProcessorBoostMode.Aggressive)),
            frequencyLimitController: frequency);

        await viewModel.InitializePerformancePolicyAsync(CancellationToken.None);

        Assert.True(viewModel.IsProcessorFrequencyLimitEnabled);
        Assert.Equal(3100, viewModel.SelectedMaximumProcessorFrequencyMhz);
        Assert.Contains("C0 4200", viewModel.ProcessorFrequencyLimitStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacySavedFrequencyWithoutExplicitIntent_IsMigratedToUnlimited()
    {
        var settings = new ApplicationSettings(PerformanceProfiles:
            new Dictionary<string, PerformanceProfileSettings>
            {
                ["balanced:1"] = new(60, 60, ProcessorBoostMode.Aggressive, 5400)
            });

        var viewModel = CreateViewModel(settings: settings);

        Assert.False(viewModel.IsProcessorFrequencyLimitEnabled);
    }

    [Fact]
    public async Task QuickControlDrawers_OpenAndReadTheirTypedStates()
    {
        var controls = new RecordingQuickControlsController();
        var viewModel = CreateViewModel(quickControlsController: controls);

        await viewModel.OpenKeyboardControlsAsync(CancellationToken.None);
        Assert.Equal(HomeDrawer.Keyboard, viewModel.ActiveDrawer);
        Assert.True(viewModel.IsWinKeyEnabled);
        Assert.True(viewModel.IsWifiEnabled);
        Assert.False(viewModel.IsBluetoothEnabled);
        Assert.True(viewModel.IsTouchpadEnabled);
        Assert.True(viewModel.IsCopilotKeyEnabled);
        Assert.True(viewModel.IsKeyboardLightingEnabled);

        await viewModel.OpenLiquidCoolingAsync(CancellationToken.None);
        Assert.Equal(HomeDrawer.LiquidCooling, viewModel.ActiveDrawer);
        Assert.True(viewModel.IsLiquidCoolingConnected);
        Assert.Equal("60%", viewModel.LiquidCoolingFanStatusText);
        Assert.Equal("运行中", viewModel.LiquidCoolingPumpStatusText);
        Assert.Equal("已连接", viewModel.LiquidCoolingConnectionStatusText);
        Assert.Equal("CoolingSystem LCT22002 (59:05)", viewModel.LiquidCoolingDeviceDisplayName);
        Assert.True(viewModel.IsLiquidCoolingBalancedStrategy);

        await viewModel.OpenBatteryAsync(CancellationToken.None);
        Assert.Equal(HomeDrawer.Battery, viewModel.ActiveDrawer);
        Assert.Equal(BatteryProtectionMode.Healthy, viewModel.SelectedBatteryProtectionMode);
        Assert.True(viewModel.IsUsbChargingWhileOffEnabled);
        Assert.False(viewModel.IsAcAutoBootEnabled);
        Assert.True(viewModel.IsHighPerformancePowerModeEnabled);
        Assert.True(viewModel.IsDeepSleepEnabled);
        Assert.Equal(30, viewModel.SelectedDeepSleepMinutes);
        Assert.Equal(80, viewModel.BatteryChargeLimitPercent);

        await viewModel.OpenDisplayColorAsync(CancellationToken.None);
        Assert.Equal(HomeDrawer.DisplayColor, viewModel.ActiveDrawer);
    }

    [Fact]
    public async Task ConfirmedBluetoothEnableIsNotRolledBackByAnImmediateStaleStatusRead()
    {
        var controls = new RecordingQuickControlsController { KeepStaleSystemReadbackAfterWrite = true };
        var viewModel = CreateViewModel(quickControlsController: controls);
        await viewModel.OpenKeyboardControlsAsync(CancellationToken.None);
        Assert.False(viewModel.IsBluetoothEnabled);

        await viewModel.SetSystemToggleAsync(SystemToggleControl.Bluetooth, true, CancellationToken.None);

        Assert.True(viewModel.IsBluetoothEnabled);
        Assert.Contains("回读", viewModel.KeyboardControlsStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SetKeyboardLighting_UsesQuickControlsControllerAndPublishesResult()
    {
        var controls = new RecordingQuickControlsController();
        var viewModel = CreateViewModel(quickControlsController: controls);

        await viewModel.SetKeyboardLightingAsync(false, CancellationToken.None);

        Assert.False(viewModel.IsKeyboardLightingEnabled);
        Assert.False(controls.KeyboardLightingEnabled);
        Assert.Contains("键盘", viewModel.KeyboardControlsStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SetKeyboardLighting_DelayedOldStatus_DoesNotRollbackFreshPowerChoice()
    {
        var now = new DateTimeOffset(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);
        var controls = new RecordingQuickControlsController();
        var viewModel = CreateViewModel(quickControlsController: controls, utcNow: () => now);

        await viewModel.SetKeyboardLightingAsync(false, CancellationToken.None);
        viewModel.ApplyLightingZoneState(LightingZone.Keyboard, new LightingZoneState(
            true,
            "MEZone_3p1nd_101",
            new(LightingZone.Keyboard, true, LightingEffect.Static, new(0, 176, 255), 3, 2,
                LightingDirection.None, 120),
            [LightingEffect.OemDefault, LightingEffect.Static],
            true));

        Assert.False(viewModel.IsKeyboardLightingEnabled);
    }

    [Fact]
    public async Task ApplyBuiltInLightingProfile_AppliesOnlyKeyboardAndNeverWritesSharedTimer()
    {
        var controls = new RecordingQuickControlsController();
        controls.AvailableLightingZones.UnionWith([LightingZone.Keyboard, LightingZone.Logo]);
        var viewModel = CreateViewModel(quickControlsController: controls);
        await viewModel.OpenLightingAsync(CancellationToken.None);

        await viewModel.ApplyLightingProfileAsync(LightingProfileCatalog.BuiltIns[0], CancellationToken.None);

        Assert.Equal([LightingZone.Keyboard], controls.AppliedLightingZones);
        Assert.Empty(controls.LightingTimerRequests);
        Assert.Contains("键盘", viewModel.LightingStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyLightingProfile_WhenControllerThrows_DoesNotPinFailedTargetAsHardwareState()
    {
        var controls = new RecordingQuickControlsController
        {
            LightingApplyException = new InvalidOperationException("simulated lighting failure")
        };
        controls.AvailableLightingZones.Add(LightingZone.Keyboard);
        var viewModel = CreateViewModel(quickControlsController: controls);
        await viewModel.OpenLightingAsync(CancellationToken.None);

        await viewModel.ApplyLightingProfileAsync(
            LightingProfileCatalog.BuiltIns.First(profile => profile.Id == "quiet-night"),
            CancellationToken.None);
        Assert.Contains("失败", viewModel.LightingStatusText, StringComparison.Ordinal);
        viewModel.ApplyLightingZoneState(LightingZone.Keyboard, new LightingZoneState(
            true,
            "PerKey",
            new(LightingZone.Keyboard, true, LightingEffect.Static, new(36, 214, 106), 3, 2,
                LightingDirection.None, 120),
            [LightingEffect.Static, LightingEffect.Breathing, LightingEffect.Rainbow]));

        Assert.Equal(LightingEffect.Static, viewModel.SelectedLightingEffect);
        Assert.Equal("#24D66A", viewModel.LightingColorHex);
    }

    [Fact]
    public async Task ApplySelectedLightingZone_UsesEditableColorAndEffect()
    {
        var controls = new RecordingQuickControlsController();
        controls.AvailableLightingZones.Add(LightingZone.Keyboard);
        var viewModel = CreateViewModel(quickControlsController: controls);
        await viewModel.OpenLightingAsync(CancellationToken.None);
        viewModel.SelectedLightingEffect = LightingEffect.Breathing;
        viewModel.LightingColorHex = "#00B0FF";

        await viewModel.ApplySelectedLightingZoneAsync(CancellationToken.None);

        var applied = Assert.Single(controls.AppliedLightingConfigurations);
        Assert.Equal(LightingEffect.Breathing, applied.Effect);
        Assert.Equal("#00B0FF", applied.Color.Hex);
        Assert.Empty(controls.LightingTimerRequests);
    }

    [Fact]
    public async Task LightingZoneSelection_ExposesKeyboardOnlyProfileSection()
    {
        var controls = new RecordingQuickControlsController();
        controls.AvailableLightingZones.UnionWith([LightingZone.Keyboard, LightingZone.LiquidCooler]);
        var viewModel = CreateViewModel(quickControlsController: controls);
        await viewModel.OpenLightingAsync(CancellationToken.None);

        Assert.True(viewModel.IsKeyboardLightingZoneSelected);

        viewModel.SelectLightingZone(LightingZone.LiquidCooler);

        Assert.False(viewModel.IsKeyboardLightingZoneSelected);
    }

    [Fact]
    public async Task WaterDynamicEffect_DisablesRgbEditorBecauseFirmwareOwnsItsColor()
    {
        var controls = new RecordingQuickControlsController();
        controls.AvailableLightingZones.Add(LightingZone.LiquidCooler);
        var viewModel = CreateViewModel(quickControlsController: controls);
        await viewModel.OpenLightingAsync(CancellationToken.None);
        viewModel.SelectLightingZone(LightingZone.LiquidCooler);

        viewModel.SelectedLightingEffect = LightingEffect.Breathing;

        Assert.False(viewModel.CanEditLightingColor);
        Assert.True(viewModel.IsWaterDynamicLightingEffectSelected);

        viewModel.SelectedLightingEffect = LightingEffect.Static;

        Assert.True(viewModel.CanEditLightingColor);
        Assert.False(viewModel.IsWaterDynamicLightingEffectSelected);
    }

    [Fact]
    public async Task KeyboardRainbow_DisablesRgbEditorBecauseFirmwareOwnsItsPalette()
    {
        var controls = new RecordingQuickControlsController();
        controls.AvailableLightingZones.Add(LightingZone.Keyboard);
        var viewModel = CreateViewModel(quickControlsController: controls);
        await viewModel.OpenLightingAsync(CancellationToken.None);

        viewModel.SelectedLightingEffect = LightingEffect.Rainbow;

        Assert.False(viewModel.CanEditLightingColor);
        Assert.True(viewModel.IsFirmwarePaletteLightingEffectSelected);

        viewModel.SelectedLightingEffect = LightingEffect.Static;

        Assert.True(viewModel.CanEditLightingColor);
        Assert.False(viewModel.IsFirmwarePaletteLightingEffectSelected);
    }

    [Fact]
    public async Task KeyboardOemDefault_HidesEditorsBecauseOnlyTheProvenPowerCommandIsSent()
    {
        var controls = new RecordingQuickControlsController();
        controls.AvailableLightingZones.Add(LightingZone.Keyboard);
        var viewModel = CreateViewModel(quickControlsController: controls);
        await viewModel.OpenLightingAsync(CancellationToken.None);

        viewModel.SelectedLightingEffect = LightingEffect.OemDefault;

        Assert.True(viewModel.IsOemDefaultLightingEffectSelected);
        Assert.False(viewModel.CanEditLightingColor);
        Assert.False(viewModel.IsFirmwarePaletteLightingEffectSelected);
    }

    [Fact]
    public void ApplyLightingZoneState_RefreshesSelectedEditorFromExternalStatusEvent()
    {
        var viewModel = CreateViewModel(quickControlsController: new RecordingQuickControlsController());
        var state = new LightingZoneState(
            true,
            "MEZone_3p1nd_101",
            new(LightingZone.Keyboard, true, LightingEffect.Breathing, new(0, 176, 255), 3, 2,
                LightingDirection.None, 120),
            [LightingEffect.OemDefault, LightingEffect.Static, LightingEffect.Breathing]);

        viewModel.ApplyLightingZoneState(LightingZone.Keyboard, state);

        Assert.True(viewModel.IsKeyboardLightingZoneAvailable);
        Assert.True(viewModel.IsKeyboardLightingEnabled);
        Assert.Equal(LightingEffect.Breathing, viewModel.SelectedLightingEffect);
        Assert.Equal("#00B0FF", viewModel.LightingColorHex);
        Assert.Contains("状态事件", viewModel.LightingStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyLightingZoneState_SparseLogoEvent_UpdatesPowerWithoutErasingEditorParameters()
    {
        var viewModel = CreateViewModel(quickControlsController: new RecordingQuickControlsController());
        viewModel.ApplyLightingZoneState(LightingZone.Logo, new LightingZoneState(
            true,
            "MEZone_Lighbar4",
            new(LightingZone.Logo, true, LightingEffect.Breathing, new(0, 176, 255), 4, 3,
                LightingDirection.None, 120),
            [LightingEffect.Static, LightingEffect.Breathing],
            true));
        viewModel.SelectLightingZone(LightingZone.Logo);

        viewModel.ApplyLightingZoneState(LightingZone.Logo, new LightingZoneState(
            true,
            "MEZone_Lighbar4",
            new(LightingZone.Logo, false, LightingEffect.Static, new(0, 0, 0), 3, 1,
                LightingDirection.None, 120),
            [LightingEffect.Static, LightingEffect.Breathing],
            false));

        Assert.False(viewModel.IsSelectedLightingZoneEnabled);
        Assert.Equal(LightingEffect.Breathing, viewModel.SelectedLightingEffect);
        Assert.Equal("#00B0FF", viewModel.LightingColorHex);
        Assert.Equal(4, viewModel.LightingBrightness);
        Assert.Equal(3, viewModel.LightingSpeed);
    }

    [Fact]
    public void ApplyLightingZoneState_FirstSparseLogoEvent_UsesVisibleFallbackInsteadOfBlackDefaults()
    {
        var viewModel = CreateViewModel(quickControlsController: new RecordingQuickControlsController());

        viewModel.ApplyLightingZoneState(LightingZone.Logo, new LightingZoneState(
            true,
            "MEZone_Lighbar4",
            new(LightingZone.Logo, true, LightingEffect.Static, new(0, 0, 0), 3, 1,
                LightingDirection.None, 120),
            [LightingEffect.Static, LightingEffect.Breathing],
            false));
        viewModel.SelectLightingZone(LightingZone.Logo);

        Assert.True(viewModel.IsSelectedLightingZoneEnabled);
        Assert.Equal(LightingEffect.Static, viewModel.SelectedLightingEffect);
        Assert.Equal("#00B0FF", viewModel.LightingColorHex);
        Assert.Equal(3, viewModel.LightingBrightness);
        Assert.Equal(2, viewModel.LightingSpeed);
    }

    [Fact]
    public void ApplyLightingZoneState_DetailedDisabledReport_PreservesLastEditableParameters()
    {
        var viewModel = CreateViewModel(quickControlsController: new RecordingQuickControlsController());
        viewModel.ApplyLightingZoneState(LightingZone.CoastLightbar, new LightingZoneState(
            true,
            "MEZone_Lighbar3",
            new(LightingZone.CoastLightbar, true, LightingEffect.Breathing, new(255, 51, 77), 4, 3,
                LightingDirection.None, 120),
            [LightingEffect.Static, LightingEffect.Breathing],
            true));
        viewModel.SelectLightingZone(LightingZone.CoastLightbar);

        viewModel.ApplyLightingZoneState(LightingZone.CoastLightbar, new LightingZoneState(
            true,
            "MEZone_Lighbar3",
            new(LightingZone.CoastLightbar, false, LightingEffect.Static, new(0, 0, 0), 0, 1,
                LightingDirection.None, 120),
            [LightingEffect.Static, LightingEffect.Breathing],
            true));

        Assert.False(viewModel.IsSelectedLightingZoneEnabled);
        Assert.Equal(LightingEffect.Breathing, viewModel.SelectedLightingEffect);
        Assert.Equal("#FF334D", viewModel.LightingColorHex);
        Assert.Equal(4, viewModel.LightingBrightness);
        Assert.Equal(3, viewModel.LightingSpeed);
    }

    [Fact]
    public void ApplyLightingZoneState_FirstDetailedDisabledReport_UsesVisibleEditorFallback()
    {
        var viewModel = CreateViewModel(quickControlsController: new RecordingQuickControlsController());

        viewModel.ApplyLightingZoneState(LightingZone.CoastLightbar, new LightingZoneState(
            true,
            "MEZone_Lighbar3",
            new(LightingZone.CoastLightbar, false, LightingEffect.Static, new(0, 0, 0), 0, 1,
                LightingDirection.None, 120),
            [LightingEffect.Static, LightingEffect.Breathing],
            true));
        viewModel.SelectLightingZone(LightingZone.CoastLightbar);

        Assert.False(viewModel.IsSelectedLightingZoneEnabled);
        Assert.Equal(LightingEffect.Static, viewModel.SelectedLightingEffect);
        Assert.Equal("#00B0FF", viewModel.LightingColorHex);
        Assert.Equal(3, viewModel.LightingBrightness);
        Assert.Equal(2, viewModel.LightingSpeed);
    }

    [Fact]
    public void ApplyLightingZoneState_SameEffectAfterOptionsReset_RaisesSelectionNotification()
    {
        var viewModel = CreateViewModel(quickControlsController: new RecordingQuickControlsController());
        var state = new LightingZoneState(
            true,
            "MEZone_Lighbar4",
            new(LightingZone.Logo, true, LightingEffect.Static, new(0, 176, 255), 3, 2,
                LightingDirection.None, 120),
            [LightingEffect.Static, LightingEffect.Breathing]);
        viewModel.ApplyLightingZoneState(LightingZone.Logo, state);
        viewModel.SelectLightingZone(LightingZone.Logo);
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        viewModel.ApplyLightingZoneState(LightingZone.Logo, state with
        {
            SupportedEffects = [LightingEffect.Static, LightingEffect.Breathing, LightingEffect.Wave]
        });

        Assert.Contains(nameof(MainWindowViewModel.SelectedLightingEffect), changed);
    }

    [Fact]
    public void ApplyLightingZoneState_UnchangedCapabilities_DoesNotRebuildEffectOptions()
    {
        var viewModel = CreateViewModel(quickControlsController: new RecordingQuickControlsController());
        var state = new LightingZoneState(
            true,
            "MEZone_Lighbar4",
            new(LightingZone.Logo, true, LightingEffect.Static, new(0, 176, 255), 3, 2,
                LightingDirection.None, 120),
            [LightingEffect.Static, LightingEffect.Breathing]);
        viewModel.ApplyLightingZoneState(LightingZone.Logo, state);
        viewModel.SelectLightingZone(LightingZone.Logo);
        var collectionChanges = 0;
        viewModel.LightingEffectOptions.CollectionChanged += (_, _) => collectionChanges++;

        viewModel.ApplyLightingZoneState(LightingZone.Logo, state);

        Assert.Equal(0, collectionChanges);
        Assert.Equal(LightingEffect.Static, viewModel.SelectedLightingEffect);
    }

    [Fact]
    public async Task SetSelectedLightingZonePower_UsesDedicatedPowerOperation()
    {
        var controls = new RecordingQuickControlsController();
        controls.AvailableLightingZones.Add(LightingZone.CoastLightbar);
        var viewModel = CreateViewModel(quickControlsController: controls);
        await viewModel.OpenLightingAsync(CancellationToken.None);
        viewModel.SelectLightingZone(LightingZone.CoastLightbar);
        viewModel.IsSelectedLightingZoneEnabled = false;

        await viewModel.SetSelectedLightingZonePowerAsync(CancellationToken.None);

        Assert.Equal([(LightingZone.CoastLightbar, false)], controls.LightingPowerRequests);
    }

    [Fact]
    public async Task SetSelectedLightingZonePower_WhenTurningOn_AppliesCompleteVisibleConfiguration()
    {
        var controls = new RecordingQuickControlsController();
        controls.AvailableLightingZones.Add(LightingZone.CoastLightbar);
        var viewModel = CreateViewModel(quickControlsController: controls);
        await viewModel.OpenLightingAsync(CancellationToken.None);
        viewModel.SelectLightingZone(LightingZone.CoastLightbar);
        viewModel.SelectedLightingEffect = LightingEffect.Breathing;
        viewModel.SetLightingColor("#000000");
        viewModel.LightingBrightness = 0;
        viewModel.IsSelectedLightingZoneEnabled = true;

        await viewModel.SetSelectedLightingZonePowerAsync(CancellationToken.None);

        var applied = Assert.Single(controls.AppliedLightingConfigurations);
        Assert.Equal(LightingZone.CoastLightbar, applied.Zone);
        Assert.True(applied.Enabled);
        Assert.Equal(LightingEffect.Breathing, applied.Effect);
        Assert.Equal(new LightingRgb(0, 176, 255), applied.Color);
        Assert.Equal(1, applied.Brightness);
        Assert.Empty(controls.LightingPowerRequests);
    }

    [Fact]
    public async Task ApplySelectedLightingZone_WhenZoneIsOff_StoresDraftWithoutHardwareWrite()
    {
        var controls = new RecordingQuickControlsController();
        controls.AvailableLightingZones.Add(LightingZone.CoastLightbar);
        var viewModel = CreateViewModel(quickControlsController: controls);
        await viewModel.OpenLightingAsync(CancellationToken.None);
        viewModel.SelectLightingZone(LightingZone.CoastLightbar);
        viewModel.IsSelectedLightingZoneEnabled = false;
        viewModel.SelectedLightingEffect = LightingEffect.Breathing;
        viewModel.SetLightingColor("#FF334D");
        viewModel.LightingBrightness = 4;
        viewModel.LightingSpeed = 3;

        await viewModel.ApplySelectedLightingZoneAsync(CancellationToken.None);

        Assert.Empty(controls.AppliedLightingConfigurations);
        Assert.Equal(LightingEffect.Breathing, viewModel.SelectedLightingEffect);
        Assert.Equal("#FF334D", viewModel.LightingColorHex);
        Assert.Contains("已保存", viewModel.LightingStatusText, StringComparison.Ordinal);
        Assert.Contains("打开", viewModel.LightingStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void LightingEditorApplyHint_ExplainsOfflineDraftBehavior()
    {
        var viewModel = CreateViewModel(quickControlsController: new RecordingQuickControlsController());

        viewModel.IsSelectedLightingZoneEnabled = false;
        Assert.Contains("仅保存", viewModel.LightingEditorApplyHint, StringComparison.Ordinal);
        Assert.Contains("打开", viewModel.LightingEditorApplyHint, StringComparison.Ordinal);

        viewModel.IsSelectedLightingZoneEnabled = true;
        Assert.Contains("自动应用", viewModel.LightingEditorApplyHint, StringComparison.Ordinal);
        Assert.Contains("回读", viewModel.LightingEditorApplyHint, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SetSelectedLightingZonePower_WhenTurningOn_AppliesStoredOfflineDraft()
    {
        var controls = new RecordingQuickControlsController();
        controls.AvailableLightingZones.Add(LightingZone.CoastLightbar);
        var viewModel = CreateViewModel(quickControlsController: controls);
        await viewModel.OpenLightingAsync(CancellationToken.None);
        viewModel.SelectLightingZone(LightingZone.CoastLightbar);
        viewModel.IsSelectedLightingZoneEnabled = false;
        viewModel.SelectedLightingEffect = LightingEffect.Breathing;
        viewModel.SetLightingColor("#FF334D");
        viewModel.LightingBrightness = 4;
        viewModel.LightingSpeed = 3;
        await viewModel.ApplySelectedLightingZoneAsync(CancellationToken.None);
        viewModel.IsSelectedLightingZoneEnabled = true;

        await viewModel.SetSelectedLightingZonePowerAsync(CancellationToken.None);

        var applied = Assert.Single(controls.AppliedLightingConfigurations);
        Assert.True(applied.Enabled);
        Assert.Equal(LightingEffect.Breathing, applied.Effect);
        Assert.Equal(new LightingRgb(255, 51, 77), applied.Color);
        Assert.Equal(4, applied.Brightness);
        Assert.Equal(3, applied.Speed);
    }

    [Fact]
    public async Task SetSelectedLightingZonePower_WhenTurningOnStaticWaterLight_ReplacesBlackWithVisibleColor()
    {
        var controls = new RecordingQuickControlsController();
        controls.AvailableLightingZones.Add(LightingZone.LiquidCooler);
        var viewModel = CreateViewModel(quickControlsController: controls);
        await viewModel.OpenLightingAsync(CancellationToken.None);
        viewModel.SelectLightingZone(LightingZone.LiquidCooler);
        viewModel.SelectedLightingEffect = LightingEffect.Static;
        viewModel.SetLightingColor("#000000");
        viewModel.IsSelectedLightingZoneEnabled = true;

        await viewModel.SetSelectedLightingZonePowerAsync(CancellationToken.None);

        var applied = Assert.Single(controls.AppliedLightingConfigurations);
        Assert.Equal(LightingZone.LiquidCooler, applied.Zone);
        Assert.True(applied.Enabled);
        Assert.Equal(new LightingRgb(0, 176, 255), applied.Color);
    }

    [Fact]
    public async Task ApplySelectedLightingZone_SparseLogoEventDuringWrite_DoesNotRevertPendingEffect()
    {
        var controls = new RecordingQuickControlsController();
        controls.AvailableLightingZones.Add(LightingZone.Logo);
        var viewModel = CreateViewModel(quickControlsController: controls);
        await viewModel.OpenLightingAsync(CancellationToken.None);
        viewModel.SelectLightingZone(LightingZone.Logo);
        viewModel.SelectedLightingEffect = LightingEffect.Breathing;
        controls.OnApplyLighting = _ => viewModel.ApplyLightingZoneState(LightingZone.Logo, new LightingZoneState(
            true,
            "MEZone_Lighbar4",
            new(LightingZone.Logo, true, LightingEffect.Static, new(0, 0, 0), 3, 1,
                LightingDirection.None, 120),
            [LightingEffect.Static, LightingEffect.Breathing],
            false));

        await viewModel.ApplySelectedLightingZoneAsync(CancellationToken.None);

        Assert.Equal(LightingEffect.Breathing, viewModel.SelectedLightingEffect);
    }

    [Fact]
    public async Task ApplySelectedLightingZone_DelayedDetailedStatus_DoesNotRollbackFreshLocalChoice()
    {
        var now = new DateTimeOffset(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);
        var controls = new RecordingQuickControlsController();
        controls.AvailableLightingZones.Add(LightingZone.CoastLightbar);
        var viewModel = CreateViewModel(quickControlsController: controls, utcNow: () => now);
        await viewModel.OpenLightingAsync(CancellationToken.None);
        viewModel.SelectLightingZone(LightingZone.CoastLightbar);
        viewModel.SelectedLightingEffect = LightingEffect.Breathing;
        viewModel.LightingColorHex = "#00B0FF";

        await viewModel.ApplySelectedLightingZoneAsync(CancellationToken.None);
        viewModel.ApplyLightingZoneState(LightingZone.CoastLightbar, new LightingZoneState(
            true,
            "MEZone_Lighbar3",
            new(LightingZone.CoastLightbar, true, LightingEffect.Static, new(255, 0, 0), 1, 1,
                LightingDirection.None, 120),
            [LightingEffect.Static, LightingEffect.Breathing],
            true));

        Assert.Equal(LightingEffect.Breathing, viewModel.SelectedLightingEffect);
        Assert.Equal("#00B0FF", viewModel.LightingColorHex);
    }

    [Fact]
    public async Task ApplySelectedLightingZone_MatchingThenDelayedOldStatus_KeepsLocalChoiceForWholeWindow()
    {
        var now = new DateTimeOffset(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);
        var controls = new RecordingQuickControlsController();
        controls.AvailableLightingZones.Add(LightingZone.CoastLightbar);
        var viewModel = CreateViewModel(quickControlsController: controls, utcNow: () => now);
        await viewModel.OpenLightingAsync(CancellationToken.None);
        viewModel.SelectLightingZone(LightingZone.CoastLightbar);
        viewModel.SelectedLightingEffect = LightingEffect.Breathing;
        viewModel.LightingColorHex = "#00B0FF";
        await viewModel.ApplySelectedLightingZoneAsync(CancellationToken.None);

        viewModel.ApplyLightingZoneState(LightingZone.CoastLightbar, new LightingZoneState(
            true,
            "MEZone_Lighbar3",
            new(LightingZone.CoastLightbar, true, LightingEffect.Breathing, new(0, 176, 255), 3, 2,
                LightingDirection.None, 120),
            [LightingEffect.Static, LightingEffect.Breathing],
            true));
        viewModel.ApplyLightingZoneState(LightingZone.CoastLightbar, new LightingZoneState(
            true,
            "MEZone_Lighbar3",
            new(LightingZone.CoastLightbar, true, LightingEffect.Static, new(255, 0, 0), 1, 1,
                LightingDirection.None, 120),
            [LightingEffect.Static, LightingEffect.Breathing],
            true));

        Assert.Equal(LightingEffect.Breathing, viewModel.SelectedLightingEffect);
        Assert.Equal("#00B0FF", viewModel.LightingColorHex);
    }

    [Fact]
    public async Task ApplyLightingZoneState_AfterLocalSettlementWindow_AcceptsExternalChoice()
    {
        var now = new DateTimeOffset(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);
        var controls = new RecordingQuickControlsController();
        controls.AvailableLightingZones.Add(LightingZone.CoastLightbar);
        var viewModel = CreateViewModel(quickControlsController: controls, utcNow: () => now);
        await viewModel.OpenLightingAsync(CancellationToken.None);
        viewModel.SelectLightingZone(LightingZone.CoastLightbar);
        viewModel.SelectedLightingEffect = LightingEffect.Breathing;
        await viewModel.ApplySelectedLightingZoneAsync(CancellationToken.None);
        now = now.AddSeconds(4);

        viewModel.ApplyLightingZoneState(LightingZone.CoastLightbar, new LightingZoneState(
            true,
            "MEZone_Lighbar3",
            new(LightingZone.CoastLightbar, true, LightingEffect.Static, new(255, 0, 0), 1, 1,
                LightingDirection.None, 120),
            [LightingEffect.Static, LightingEffect.Breathing],
            true));

        Assert.Equal(LightingEffect.Static, viewModel.SelectedLightingEffect);
        Assert.Equal("#FF0000", viewModel.LightingColorHex);
    }

    [Fact]
    public async Task LightingEdit_OlderAcknowledgementCannotReplaceNewerColor()
    {
        var controls = new RecordingQuickControlsController();
        controls.AvailableLightingZones.Add(LightingZone.CoastLightbar);
        var viewModel = CreateViewModel(quickControlsController: controls);
        await viewModel.OpenLightingAsync(CancellationToken.None);
        viewModel.SelectLightingZone(LightingZone.CoastLightbar);
        viewModel.SetLightingColor("#FF334D");
        controls.OnApplyLighting = submitted =>
        {
            viewModel.SetLightingColor("#00B0FF");
            viewModel.ApplyLightingZoneState(submitted.Zone, new(true, "PerKey",
                submitted with { Direction = LightingDirection.None },
                [LightingEffect.Static, LightingEffect.Breathing]));
        };

        await viewModel.ApplySelectedLightingZoneAsync(CancellationToken.None);

        Assert.Equal("#00B0FF", viewModel.LightingColorHex);
        controls.OnApplyLighting = null;
        await viewModel.ApplySelectedLightingZoneAsync(CancellationToken.None);
        Assert.Equal(new LightingRgb(0, 176, 255), controls.AppliedLightingConfigurations.Last().Color);
    }

    [Fact]
    public async Task LightingEdit_StatusDuringDebounceCannotEraseSelection()
    {
        var controls = new RecordingQuickControlsController();
        controls.AvailableLightingZones.Add(LightingZone.CoastLightbar);
        var viewModel = CreateViewModel(quickControlsController: controls);
        await viewModel.OpenLightingAsync(CancellationToken.None);
        viewModel.SelectLightingZone(LightingZone.CoastLightbar);
        viewModel.SetLightingColor("#00B0FF");

        viewModel.ApplyLightingZoneState(LightingZone.CoastLightbar, new(true, "PerKey",
            new(LightingZone.CoastLightbar, true, LightingEffect.Static, new(36, 214, 106), 4, 1,
                LightingDirection.None, 120), [LightingEffect.Static]));

        Assert.Equal("#00B0FF", viewModel.LightingColorHex);
    }

    [Fact]
    public async Task OpenLighting_ReopeningDoesNotEraseOfflineColorDraft()
    {
        var controls = new RecordingQuickControlsController();
        controls.AvailableLightingZones.Add(LightingZone.CoastLightbar);
        var viewModel = CreateViewModel(quickControlsController: controls);
        await viewModel.OpenLightingAsync(CancellationToken.None);
        viewModel.SelectLightingZone(LightingZone.CoastLightbar);
        viewModel.IsSelectedLightingZoneEnabled = false;
        viewModel.SetLightingColor("#00B0FF");
        await viewModel.ApplySelectedLightingZoneAsync(CancellationToken.None);
        controls.LightingReadOverride = zone => new(true, "PerKey",
            new(zone, false, LightingEffect.Static, new(0, 0, 0), 0, 1,
                LightingDirection.None, 120), [LightingEffect.Static]);

        await viewModel.OpenLightingAsync(CancellationToken.None);

        Assert.False(viewModel.IsSelectedLightingZoneEnabled);
        Assert.Equal("#00B0FF", viewModel.LightingColorHex);
    }

    [Fact]
    public async Task LightingEdit_TabRoundTripPreservesUnsubmittedColor()
    {
        var controls = new RecordingQuickControlsController();
        controls.AvailableLightingZones.UnionWith([LightingZone.Keyboard, LightingZone.CoastLightbar]);
        var viewModel = CreateViewModel(quickControlsController: controls);
        await viewModel.OpenLightingAsync(CancellationToken.None);
        viewModel.SelectLightingZone(LightingZone.CoastLightbar);
        viewModel.SetLightingColor("#5267FF");
        viewModel.SelectLightingZone(LightingZone.Keyboard);
        viewModel.SelectLightingZone(LightingZone.CoastLightbar);

        Assert.Equal("#5267FF", viewModel.LightingColorHex);
    }

    [Fact]
    public void LightingStatus_EffectOnlyPacketDoesNotInventBlackColor()
    {
        var viewModel = CreateViewModel(quickControlsController: new RecordingQuickControlsController());
        viewModel.ApplyLightingZoneState(LightingZone.CoastLightbar, new(true, "PerKey",
            new(LightingZone.CoastLightbar, true, LightingEffect.Breathing, new(0, 176, 255), 4, 3,
                LightingDirection.None, 120), [LightingEffect.Static, LightingEffect.Breathing]));
        viewModel.SelectLightingZone(LightingZone.CoastLightbar);
        var partial = RevoHelper.Hardware.Windows.OemControl.GcuQuickControlsStatusParser.ParseLightingZone(
            LightingZone.CoastLightbar, "{\"powerStatus\":\"On\",\"effect\":\"Single\"}");

        viewModel.ApplyLightingZoneState(LightingZone.CoastLightbar, partial!);

        Assert.Equal(LightingEffect.Static, viewModel.SelectedLightingEffect);
        Assert.Equal("#00B0FF", viewModel.LightingColorHex);
        Assert.Equal(4, viewModel.LightingBrightness);
        Assert.Equal(3, viewModel.LightingSpeed);
    }

    [Fact]
    public void LightingStatus_ColorOnlyPacketDoesNotInventPowerOrEffect()
    {
        var viewModel = CreateViewModel(quickControlsController: new RecordingQuickControlsController());
        viewModel.ApplyLightingZoneState(LightingZone.CoastLightbar, new(true, "PerKey",
            new(LightingZone.CoastLightbar, true, LightingEffect.Breathing, new(255, 51, 77), 4, 3,
                LightingDirection.None, 120), [LightingEffect.Static, LightingEffect.Breathing]));
        viewModel.SelectLightingZone(LightingZone.CoastLightbar);
        var partial = RevoHelper.Hardware.Windows.OemControl.GcuQuickControlsStatusParser.ParseLightingZone(
            LightingZone.CoastLightbar, "{\"color\":{\"R\":0,\"G\":176,\"B\":255}}");

        Assert.NotNull(partial);
        viewModel.ApplyLightingZoneState(LightingZone.CoastLightbar, partial);

        Assert.True(viewModel.IsSelectedLightingZoneEnabled);
        Assert.Equal(LightingEffect.Breathing, viewModel.SelectedLightingEffect);
        Assert.Equal("#00B0FF", viewModel.LightingColorHex);
    }

    [Fact]
    public async Task LightingPreset_ReplacesOlderUnsubmittedDraft()
    {
        var controls = new RecordingQuickControlsController();
        controls.AvailableLightingZones.Add(LightingZone.Keyboard);
        var viewModel = CreateViewModel(quickControlsController: controls);
        await viewModel.OpenLightingAsync(CancellationToken.None);
        viewModel.SetLightingColor("#FF334D");

        await viewModel.ApplyLightingProfileAsync(LightingProfileCatalog.BuiltIns.First(p => p.Id == "ice-breath"), CancellationToken.None);

        Assert.Equal(controls.AppliedLightingConfigurations.Last().Color.Hex, viewModel.LightingColorHex);
        Assert.Equal(LightingEffect.Breathing, viewModel.SelectedLightingEffect);
    }

    [Fact]
    public async Task KeyboardPower_AppliesColorEditedWhileOff()
    {
        var controls = new RecordingQuickControlsController();
        controls.AvailableLightingZones.Add(LightingZone.Keyboard);
        var viewModel = CreateViewModel(quickControlsController: controls);
        await viewModel.OpenLightingAsync(CancellationToken.None);
        viewModel.IsSelectedLightingZoneEnabled = false;
        viewModel.SetLightingColor("#FF334D");
        await viewModel.ApplySelectedLightingZoneAsync(CancellationToken.None);
        viewModel.IsSelectedLightingZoneEnabled = true;

        await viewModel.SetSelectedLightingZonePowerAsync(CancellationToken.None);

        Assert.Equal(new LightingRgb(255, 51, 77), Assert.Single(controls.AppliedLightingConfigurations).Color);
    }

    [Fact]
    public async Task ApplyLightingSleepTimer_IsExplicitAndUsesOfficialSecondsValue()
    {
        var controls = new RecordingQuickControlsController();
        controls.AvailableLightingZones.Add(LightingZone.Keyboard);
        var viewModel = CreateViewModel(quickControlsController: controls);
        await viewModel.OpenLightingAsync(CancellationToken.None);
        viewModel.LightingSleepSeconds = 7200;

        await viewModel.ApplyLightingSleepTimerAsync(CancellationToken.None);

        Assert.Equal([7200], controls.LightingTimerRequests);
        Assert.Contains("灯效休眠", viewModel.LightingStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Constructor_RestoresSavedLightingProfileForLaterExplicitApply()
    {
        var controls = new RecordingQuickControlsController();
        controls.AvailableLightingZones.Add(LightingZone.Keyboard);
        var store = new RecordingLightingProfileStore { LoadedProfile = LightingProfileCatalog.BuiltIns[1] };
        var viewModel = CreateViewModel(quickControlsController: controls, lightingProfileStore: store);
        await viewModel.OpenLightingAsync(CancellationToken.None);

        await viewModel.ApplyImportedLightingProfileAsync(CancellationToken.None);

        var applied = Assert.Single(controls.AppliedLightingConfigurations);
        Assert.Equal(LightingEffect.Breathing, applied.Effect);
        Assert.Equal(1, store.LoadCalls);
    }

    [Fact]
    public async Task SetSystemToggle_UpdatesOnlyAfterControllerAccepts()
    {
        var controls = new RecordingQuickControlsController();
        var viewModel = CreateViewModel(quickControlsController: controls);

        await viewModel.SetSystemToggleAsync(SystemToggleControl.Bluetooth, true, CancellationToken.None);

        Assert.Equal(SystemToggleControl.Bluetooth, controls.LastSystemToggle);
        Assert.True(viewModel.IsBluetoothEnabled);
    }

    [Fact]
    public async Task SetBatteryChargeLimit_UsesVerifiedOemContract()
    {
        var controls = new RecordingQuickControlsController();
        var viewModel = CreateViewModel(quickControlsController: controls);

        await viewModel.SetBatteryChargeLimitAsync(80, CancellationToken.None);

        Assert.Equal(80, controls.LastBatteryChargeLimitPercent);
        Assert.Equal(80, viewModel.BatteryChargeLimitPercent);
        Assert.Contains("80%", viewModel.BatteryStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CustomPerformance_ReadsAndAppliesThroughDedicatedController()
    {
        var custom = new RecordingCustomPerformanceController();
        var viewModel = CreateViewModel(customPerformanceController: custom);
        viewModel.SelectProfile("custom");

        await viewModel.OpenPerformanceAsync(CancellationToken.None);
        Assert.Equal(150, viewModel.CustomGpuTgpWatts);
        viewModel.CustomGpuDynamicBoostWatts = 20;
        await viewModel.ApplyCustomPerformanceAsync(CancellationToken.None);

        Assert.Equal(20, custom.LastApplied!.GpuDynamicBoostWatts);
        Assert.Contains("回读", viewModel.CustomPerformanceStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GpuOffsetEdit_UsesIndependentControllerAndLiveBoundsWithoutWritingOnOpen()
    {
        var custom = new RecordingCustomPerformanceController();
        var vm = CreateViewModel(customPerformanceController: custom);
        vm.SelectProfile("custom");
        await vm.OpenPerformanceAsync(default);
        Assert.Null(custom.LastOffsets);
        Assert.Null(custom.LastApplied);
        Assert.True(vm.CanEditGpuClockOffsets);
        Assert.Equal(250, vm.CustomGpuCoreOffsetMaximum);
        vm.CustomGpuCoreOffsetMhz = 999;
        vm.CustomGpuMemoryOffsetMhz = 3000;
        await vm.ApplyGpuClockOffsetsAsync(default);
        Assert.Equal(new GpuClockOffsets(250, 1500), custom.LastOffsets);
        Assert.Null(custom.LastApplied);
        Assert.Null(custom.LastGpuApplied);
        vm.SelectProfile("balanced");
        vm.CustomGpuCoreOffsetMhz = 200;
        await vm.ApplyGpuClockOffsetsAsync(default);
        Assert.Equal(new GpuClockOffsets(250, 1500), custom.LastOffsets);
    }

    [Fact]
    public async Task SingleGpuSetting_RequiresSuperPerformanceOnWithoutReplayingFullDraft()
    {
        var custom = new RecordingCustomPerformanceController();
        var vm = CreateViewModel(customPerformanceController: custom);
        vm.SelectProfile("custom");
        await vm.OpenPerformanceAsync(default);
        Assert.True(vm.CanEditGpuClockOffsets);
        Assert.True(vm.CanEditGpuPower);
        Assert.Contains("负值须经 OEM 回读确认", vm.GpuOffsetRangeText);
        await vm.ApplyCustomPerformanceSettingAsync(CustomPerformanceSetting.GpuTgpWatts, 135, default);
        Assert.Equal((CustomPerformanceSetting.GpuTgpWatts, 135), custom.LastSetting);
        custom.ClearLastSetting();
        vm.CustomOverclockingEnabled = false;
        Assert.False(vm.CanEditGpuPower);
        Assert.False(vm.CanEditGpuClockOffsets);
        await vm.ApplyCustomPerformanceSettingAsync(CustomPerformanceSetting.GpuTgpWatts, 135, default);
        Assert.Null(custom.LastSetting);
        Assert.Null(custom.LastApplied);
        Assert.Null(custom.LastGpuApplied);
        Assert.Null(custom.LastOffsets);
        vm.SelectProfile("balanced");
        await vm.ApplyCustomPerformanceSettingAsync(CustomPerformanceSetting.GpuTgpWatts, 140, default);
        Assert.Null(custom.LastSetting);
    }

    [Fact]
    public async Task RejectedDynamicBoostEdit_RestoresVisibleSwitchFromLiveOemState()
    {
        var custom = new RecordingCustomPerformanceController
        {
            SingleSettingResultOverride = ControlResult.Rejected("OEM 回读仍为开启")
        };
        var vm = CreateViewModel(customPerformanceController: custom);
        vm.SelectProfile("custom");
        await vm.OpenPerformanceAsync(default);
        Assert.True(vm.CustomGpuDynamicBoostEnabled);

        vm.CustomGpuDynamicBoostEnabled = false;
        await vm.ApplyCustomPerformanceSettingAsync(CustomPerformanceSetting.GpuDynamicBoostEnabled, 0, default);

        Assert.Equal((CustomPerformanceSetting.GpuDynamicBoostEnabled, 0), custom.LastSetting);
        Assert.True(vm.CustomGpuDynamicBoostEnabled);
        Assert.Contains("回读", vm.CustomPerformanceStatusText);
    }

    [Fact]
    public async Task CustomPerformance_DoesNotReboundToStaleOemReadbackAfterApply()
    {
        var custom = new RecordingCustomPerformanceController { PreserveStaleReadbackAfterApply = true };
        var viewModel = CreateViewModel(customPerformanceController: custom);
        viewModel.SelectProfile("custom");
        await viewModel.OpenPerformanceAsync(CancellationToken.None);

        viewModel.CustomGpuDynamicBoostWatts = 20;
        await viewModel.ApplyCustomPerformanceAsync(CancellationToken.None);

        Assert.Equal(20, custom.LastApplied!.GpuDynamicBoostWatts);
        Assert.Equal(20, viewModel.CustomGpuDynamicBoostWatts);
        Assert.Contains("尚未同步", viewModel.CustomPerformanceStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PresetModeGpuPerformance_IsReadOnlyAndDoesNotWriteExperimentalFields()
    {
        var custom = new RecordingCustomPerformanceController();
        var viewModel = CreateViewModel(customPerformanceController: custom);
        viewModel.SelectProfile("balanced");
        await viewModel.OpenPerformanceAsync(CancellationToken.None);
        viewModel.CustomGpuTgpWatts = 140;

        await viewModel.ApplyCustomPerformanceAsync(CancellationToken.None);

        Assert.Null(custom.LastApplied);
        Assert.Null(custom.LastGpuApplied);
        Assert.False(viewModel.CanApplyCustomPerformance);
        Assert.Contains("自定义模式", viewModel.CustomPerformanceStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LiquidCooling_EmptyDiscoveryRetainsRememberedDeviceWithoutClaimingConnection()
    {
        var controls = new RecordingQuickControlsController { LiquidConnected = false, EmptyLiquidDiscovery = true };
        var viewModel = CreateViewModel(quickControlsController: controls);
        await viewModel.ScanLiquidCoolingDevicesAsync(CancellationToken.None);
        Assert.Equal(controls.SelectedLiquidDeviceId, Assert.Single(viewModel.LiquidCoolingDevices));
        Assert.Equal(controls.SelectedLiquidDeviceId, viewModel.SelectedLiquidCoolingDeviceId);
        Assert.False(viewModel.IsLiquidCoolingConnected);
        Assert.Equal(0, controls.LiquidConnectCalls);
        await viewModel.RefreshLiquidCoolingStatusAsync(CancellationToken.None);
        Assert.Equal(controls.SelectedLiquidDeviceId, Assert.Single(viewModel.LiquidCoolingDevices));
    }

    [Fact]
    public async Task LiquidCooling_FirstUseScansSelectsAndConnectsGcuDiscoveredDevice()
    {
        var controls = new RecordingQuickControlsController { LiquidConnected = false };
        var viewModel = CreateViewModel(quickControlsController: controls);

        await viewModel.ScanLiquidCoolingDevicesAsync(CancellationToken.None);
        Assert.Equal(new[] { "CoolingSystem LCT22002 (59:05)", "CoolingSystem LCT21001 (31:09)" }, viewModel.LiquidCoolingDevices);

        viewModel.SelectedLiquidCoolingDeviceId = "CoolingSystem LCT21001 (31:09)";
        await viewModel.ConnectLiquidCoolingAsync(CancellationToken.None);

        Assert.Equal("CoolingSystem LCT21001 (31:09)", controls.SelectedLiquidDeviceId);
        Assert.True(controls.LiquidConnected);
        Assert.True(viewModel.IsLiquidCoolingConnected);
    }

    [Fact]
    public async Task RefreshLiquidCoolingStatus_TracksExternalDisconnectAndReconnect()
    {
        var controls = new RecordingQuickControlsController();
        var viewModel = CreateViewModel(quickControlsController: controls);
        await viewModel.OpenLiquidCoolingAsync(CancellationToken.None);
        Assert.Equal("60%", viewModel.LiquidCoolingFanStatusText);

        controls.LiquidConnected = false;
        await viewModel.RefreshLiquidCoolingStatusAsync(CancellationToken.None);
        Assert.Equal("未连接", viewModel.LiquidCoolingFanStatusText);
        Assert.Equal("未连接", viewModel.LiquidCoolingPumpStatusText);

        controls.LiquidFanDuty = 40;
        controls.LiquidConnected = true;
        await viewModel.RefreshLiquidCoolingStatusAsync(CancellationToken.None);
        Assert.Equal("40%", viewModel.LiquidCoolingFanStatusText);
        Assert.Equal("运行中", viewModel.LiquidCoolingPumpStatusText);
    }

    [Fact]
    public async Task AutoConnectLiquidCooling_ConnectsRememberedDeviceAndUpdatesMonitor()
    {
        var controls = new RecordingQuickControlsController { LiquidConnected = false };
        var viewModel = CreateViewModel(
            quickControlsController: controls,
            verificationDelay: (_, _) => Task.CompletedTask);

        var result = await viewModel.TryAutoConnectLiquidCoolingAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1, controls.LiquidConnectCalls);
        Assert.True(viewModel.IsLiquidCoolingConnected);
        Assert.Contains("自动连接", viewModel.LiquidCoolingStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AutoConnectLiquidCooling_DisabledPreferenceDoesNotTouchHardware()
    {
        var controls = new RecordingQuickControlsController { LiquidConnected = false };
        var viewModel = CreateViewModel(
            quickControlsController: controls,
            settings: new ApplicationSettings(AutoConnectLiquidCooling: false));

        var result = await viewModel.TryAutoConnectLiquidCoolingAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(0, controls.LiquidConnectCalls);
    }

    [Fact]
    public void LiquidCoolingAutoConnectPreference_PersistsWithoutTouchingHardware()
    {
        var store = new RecordingSettingsStore();
        var controls = new RecordingQuickControlsController { LiquidConnected = false };
        var viewModel = CreateViewModel(store, quickControlsController: controls);

        viewModel.IsLiquidCoolingAutoConnectEnabled = false;

        Assert.False(store.LastSaved!.AutoConnectLiquidCooling);
        Assert.Equal(0, controls.LiquidConnectCalls);
    }

    [Fact]
    public async Task Priming_StartsFromIdleAndLocksWaterControlsUntilOfficialStateEnds()
    {
        var controls = new RecordingQuickControlsController { LiquidPriming = false };
        var viewModel = CreateViewModel(quickControlsController: controls);
        await viewModel.OpenLiquidCoolingAsync(CancellationToken.None);

        Assert.True(viewModel.CanPrimeLiquidCooling);
        await viewModel.StartLiquidCoolingPrimingAsync(CancellationToken.None);

        Assert.True(viewModel.IsLiquidCoolingPriming);
        Assert.True(viewModel.IsLiquidCoolingPrimingNoticeVisible);
        Assert.False(viewModel.CanPrimeLiquidCooling);
        Assert.False(viewModel.CanDisconnectLiquidCooling);
        Assert.False(viewModel.CanResetLiquidCooling);

        controls.LiquidPriming = false;
        await viewModel.RefreshLiquidCoolingStatusAsync(CancellationToken.None);

        Assert.False(viewModel.IsLiquidCoolingPriming);
        Assert.False(viewModel.IsLiquidCoolingPrimingNoticeVisible);
        Assert.True(viewModel.CanPrimeLiquidCooling);
    }

    [Fact]
    public async Task OpenDisplay_DetectsCurrentModeAndEveryRateExposedByCurrentGpuPath()
    {
        var display = new RecordingDisplayController(new(
            "DISPLAY1", "内置显示器", 2560, 1600, 240, [60, 240, 300]));
        var viewModel = CreateViewModel(
            quickControlsController: new RecordingQuickControlsController(),
            displayController: display);

        await viewModel.OpenDisplayAsync(CancellationToken.None);

        Assert.Equal(HomeDrawer.DisplayColor, viewModel.ActiveDrawer);
        Assert.Equal("2560 × 1600", viewModel.DisplayResolutionText);
        Assert.Equal("240 Hz", viewModel.CurrentDisplayRefreshRateText);
        Assert.Equal([60, 240, 300], viewModel.DisplayRefreshRates);
        Assert.Equal(240, viewModel.SelectedDisplayRefreshRateHz);
    }

    [Fact]
    public async Task OpenDisplay_PreservesWindowsModesAndReportsOemReadFailureWithoutExitInstruction()
    {
        var display = new RecordingDisplayController(new(
            "DISPLAY1", "内置显示器", 2560, 1600, 240, [60, 240]));
        var controls = new RecordingQuickControlsController
        {
            DisplayFeatureReadException = new InvalidOperationException(
                "检测到 CCUWinUI 正在运行，已拒绝写入以避免争用 GCU。")
        };
        var viewModel = CreateViewModel(quickControlsController: controls, displayController: display);

        await viewModel.OpenDisplayAsync(CancellationToken.None);

        Assert.Equal("240 Hz", viewModel.CurrentDisplayRefreshRateText);
        Assert.Equal([60, 240], viewModel.DisplayRefreshRates);
        Assert.Equal(240, viewModel.SelectedDisplayRefreshRateHz);
        Assert.Contains("OEM 显示功能状态读取失败", viewModel.DisplayColorStatusText, StringComparison.Ordinal);
        Assert.DoesNotContain("关闭机械革命控制中心", viewModel.DisplayColorStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisplayTabsAndRefreshRateApplyUseDetectedControllerState()
    {
        var display = new RecordingDisplayController(new(
            "DISPLAY1", "内置显示器", 2560, 1600, 240, [60, 240, 300]));
        var viewModel = CreateViewModel(displayController: display);
        await viewModel.OpenDisplayAsync(CancellationToken.None);

        viewModel.SelectDisplaySettingsPage(DisplaySettingsPage.RefreshRate);
        viewModel.SelectedDisplayRefreshRateHz = 300;
        await viewModel.ApplyDisplayRefreshRateAsync(CancellationToken.None);

        Assert.True(viewModel.IsDisplayRefreshRatePage);
        Assert.Equal(300, display.LastAppliedRefreshRateHz);
        Assert.Equal("300 Hz", viewModel.CurrentDisplayRefreshRateText);
    }

    [Fact]
    public async Task UnsupportedOemDisplayPagesAreHiddenAndRefreshRateBecomesTheActivePage()
    {
        var controls = new RecordingQuickControlsController
        {
            DisplayFeatures = new(null, null, null, false, false)
        };
        var display = new RecordingDisplayController(new(
            "DISPLAY1", "内置显示器", 2560, 1600, 240, [60, 240]));
        var viewModel = CreateViewModel(quickControlsController: controls, displayController: display);

        await viewModel.OpenDisplayAsync(CancellationToken.None);

        Assert.False(viewModel.SupportsDisplayColorManagement);
        Assert.False(viewModel.SupportsMiniLedLocalDimming);
        Assert.True(viewModel.SupportsDisplayRefreshRate);
        Assert.Equal(1, viewModel.DisplaySettingsTabCount);
        Assert.True(viewModel.IsDisplayRefreshRatePage);
        Assert.Equal([60, 240], viewModel.DisplayRefreshRates);
    }

    [Fact]
    public async Task OptionalSystemAndPowerCapabilitiesFollowNullableOemReadback()
    {
        var controls = new RecordingQuickControlsController
        {
            SystemToggles = new(true, true, null, null, null),
            PowerSettings = new(null, false, null, null, null, null, null)
        };
        var viewModel = CreateViewModel(quickControlsController: controls);

        await viewModel.OpenKeyboardControlsAsync(CancellationToken.None);
        await viewModel.OpenBatteryAsync(CancellationToken.None);

        Assert.True(viewModel.SupportsWifiToggle);
        Assert.True(viewModel.SupportsBluetoothToggle);
        Assert.False(viewModel.SupportsTouchpadToggle);
        Assert.False(viewModel.SupportsWebcamToggle);
        Assert.False(viewModel.SupportsCopilotKeyToggle);
        Assert.False(viewModel.SupportsUsbChargingWhileOff);
        Assert.False(viewModel.SupportsAcAutoBoot);
        Assert.False(viewModel.SupportsHighPerformancePowerMode);
        Assert.False(viewModel.SupportsDeepSleep);
        Assert.False(viewModel.SupportsBatteryChargeLimit);
    }

    [Fact]
    public async Task FailedOemDisplayWrites_ReassertOneWayBindingsAndKeepReadbackState()
    {
        var controls = new RecordingQuickControlsController
        {
            DisplayColorResult = ControlResult.Rejected("色域未收敛"),
            DisplayColorEnabledResult = ControlResult.Rejected("色彩开关未收敛"),
            LocalDimmingResult = ControlResult.Rejected("局部调光未收敛")
        };
        var viewModel = CreateViewModel(quickControlsController: controls);
        await viewModel.OpenDisplayAsync(CancellationToken.None);
        var changes = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changes.Add(args.PropertyName);

        await viewModel.SetDisplayColorAsync(DisplayColorMode.P3, CancellationToken.None);
        await viewModel.SetDisplayColorManagementAsync(false, CancellationToken.None);
        await viewModel.SetMiniLedLocalDimmingAsync(true, CancellationToken.None);

        Assert.Equal(DisplayColorMode.Default, viewModel.SelectedDisplayColorMode);
        Assert.True(viewModel.IsDisplayColorManagementEnabled);
        Assert.False(viewModel.IsMiniLedLocalDimmingEnabled);
        Assert.Contains(nameof(MainWindowViewModel.SelectedDisplayColorMode), changes);
        Assert.Contains(nameof(MainWindowViewModel.IsDisplayColorManagementEnabled), changes);
        Assert.Contains(nameof(MainWindowViewModel.IsMiniLedLocalDimmingEnabled), changes);
    }

    [Fact]
    public async Task AutomaticRefreshRate_UsesMinimumOnDcAndRestoresPreferredRateOnAc()
    {
        var store = new RecordingSettingsStore();
        var display = new RecordingDisplayController(new(
            "DISPLAY1", "内置显示器", 2560, 1600, 240, [60, 240, 300]));
        var viewModel = new MainWindowViewModel(
            CreateDashboard(),
            new ApplicationSettings(AutomaticRefreshRate: true, PreferredAcRefreshRateHz: 300),
            store,
            new RecordingStartupRegistration(),
            displayController: display);
        await viewModel.OpenDisplayAsync(CancellationToken.None);

        await viewModel.ApplyAutomaticRefreshRateForPowerSourceAsync(onAcPower: false, CancellationToken.None);
        await viewModel.ApplyAutomaticRefreshRateForPowerSourceAsync(onAcPower: true, CancellationToken.None);

        Assert.Equal([60, 300], display.AppliedRefreshRatesHz);
        Assert.True(viewModel.IsAutomaticRefreshRateEnabled);
        Assert.Equal(2, display.TemporaryCalls);
        Assert.Null(store.LastSaved);
    }

    [Fact]
    public async Task AutomaticRefreshRate_LatestAcEventSurvivesBusyDcRead()
    {
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var display = new RecordingDisplayController(new("DISPLAY1", "Panel", 2560, 1600, 240, [60, 240, 300]))
        {
            BeforeRead = () => ++calls == 1 ? waiting.Task : Task.CompletedTask
        };
        var vm = CreateViewModel(displayController: display,
            settings: new(AutomaticRefreshRate: true, PreferredAcRefreshRateHz: 300));
        var dc = vm.ApplyAutomaticRefreshRateForPowerSourceAsync(false, CancellationToken.None);
        var ac = vm.ApplyAutomaticRefreshRateForPowerSourceAsync(true, CancellationToken.None);
        waiting.SetResult();
        await Task.WhenAll(dc, ac);
        Assert.Equal([300], display.AppliedRefreshRatesHz);
    }

    [Fact]
    public async Task AutomaticRefreshRate_DisableCancelsQueuedLowRateAndRestores()
    {
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var display = new RecordingDisplayController(new("DISPLAY1", "Panel", 2560, 1600, 240, [60, 240]))
        {
            BeforeRead = () => ++calls == 1 ? waiting.Task : Task.CompletedTask
        };
        var vm = CreateViewModel(displayController: display, settings: new(AutomaticRefreshRate: true));
        var dc = vm.ApplyAutomaticRefreshRateForPowerSourceAsync(false, CancellationToken.None);
        var disabling = vm.SetAutomaticRefreshRateAsync(false, CancellationToken.None);
        waiting.SetResult();
        await Task.WhenAll(dc, disabling);
        Assert.Empty(display.AppliedRefreshRatesHz);
        Assert.Equal(1, display.RestoreCalls);
        Assert.False(vm.IsAutomaticRefreshRateEnabled);
    }

    [Fact]
    public void BatteryChargeLimit_DefaultIs80WithoutHardwareWrites()
    {
        var controls = new RecordingQuickControlsController();
        var viewModel = CreateViewModel(quickControlsController: controls);

        Assert.Equal(80, viewModel.BatteryChargeLimitPercent);
        Assert.False(viewModel.SupportsBatteryChargeLimit);
        Assert.Null(controls.LastBatteryChargeLimitPercent);
    }

    [Theory]
    [InlineData(null, 80, false)]
    [InlineData(0, 80, false)]
    [InlineData(101, 80, false)]
    [InlineData(65, 65, true)]
    [InlineData(100, 100, true)]
    public async Task OpenBattery_OnlyValidReadbackOverridesDefaultAndNeverWrites(int? readback, int expected, bool supported)
    {
        var controls = new RecordingQuickControlsController
        {
            PowerSettings = new(true, true, false, true, true, 30, readback)
        };
        var viewModel = CreateViewModel(quickControlsController: controls);

        await viewModel.OpenBatteryAsync(CancellationToken.None);

        Assert.Equal(expected, viewModel.BatteryChargeLimitPercent);
        Assert.Equal(supported, viewModel.SupportsBatteryChargeLimit);
        Assert.Null(controls.LastBatteryChargeLimitPercent);
    }

    [Fact]
    public void WindowScale_DefaultAndOptionsAreStable()
    {
        var viewModel = CreateViewModel();
        Assert.Equal(100, viewModel.WindowScalePercent);
        Assert.Equal([70, 80, 90, 100, 110, 120, 130], viewModel.WindowScaleOptions);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(95)]
    [InlineData(140)]
    public void WindowScale_InvalidSavedValueNormalizesTo100WithoutWriting(int invalid)
    {
        var store = new RecordingSettingsStore();
        var viewModel = CreateViewModel(store: store, settings: new(WindowScalePercent: invalid));
        Assert.Equal(100, viewModel.WindowScalePercent);
        Assert.Null(store.LastSaved);
    }

    [Fact]
    public void WindowScale_SuccessfulChangePersistsAndNotifiesOnlyOnce()
    {
        var store = new RecordingSettingsStore();
        var viewModel = CreateViewModel(store: store);
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        viewModel.WindowScalePercent = 80;
        viewModel.WindowScalePercent = 80;
        viewModel.WindowScalePercent = 85;

        Assert.Equal(80, viewModel.WindowScalePercent);
        Assert.Equal(80, store.LastSaved!.WindowScalePercent);
        Assert.Single(changed, property => property == nameof(MainWindowViewModel.WindowScalePercent));
        Assert.Equal(80, CreateViewModel(settings: store.LastSaved).WindowScalePercent);
    }

    [Fact]
    public void WindowScale_SaveFailureKeepsPreviousValueAndDoesNotNotifyChange()
    {
        var store = new RecordingSettingsStore { ThrowOnSave = true };
        var viewModel = CreateViewModel(store: store);
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        viewModel.WindowScalePercent = 130;

        Assert.Equal(100, viewModel.WindowScalePercent);
        Assert.DoesNotContain(nameof(MainWindowViewModel.WindowScalePercent), changed);
        Assert.Contains("设置保存失败", viewModel.StatusMessage);
    }

    private static MainWindowViewModel CreateViewModel(
        IApplicationSettingsStore? store = null,
        IStartupRegistration? startup = null,
        ICpuPolicyController? cpuPolicyController = null,
        IProcessorBoostController? boostController = null,
        IPlatformModeController? platformModeController = null,
        IGpuOutputModeController? gpuOutputModeController = null,
        IProcessorFrequencyLimitController? frequencyLimitController = null,
        Func<TimeSpan, CancellationToken, Task>? verificationDelay = null,
        IConflictServiceManager? conflictServiceManager = null,
        IQuickControlsController? quickControlsController = null,
        IDisplayController? displayController = null,
        Func<bool>? officialConsoleRunningProbe = null,
        ICustomPerformanceController? customPerformanceController = null,
        ILightingProfileStore? lightingProfileStore = null,
        IOemFanController? fanController = null,
        Func<DateTimeOffset>? utcNow = null,
        ApplicationSettings? settings = null,
        ExperimentalControlsViewModel? experiments = null) =>
        new(
            CreateDashboard(),
            settings ?? new ApplicationSettings(),
            store ?? new RecordingSettingsStore(),
            startup ?? new RecordingStartupRegistration(),
            cpuPolicyController,
            boostController,
            platformModeController,
            gpuOutputModeController,
            frequencyLimitController,
            verificationDelay,
            conflictServiceManager,
            quickControlsController,
            displayController,
            officialConsoleRunningProbe,
            customPerformanceController: customPerformanceController,
            lightingProfileStore: lightingProfileStore,
            fanController: fanController,
            utcNow: utcNow,
            experiments: experiments);

    private static HomeDashboard CreateDashboard() =>
        HomeDashboardFactory.Create(
            new MachineIdentity(
                "MECHREVO",
                "YAOSHI Series",
                "YAOSHI Series-X6AR55xY",
                "Intel(R) Core(TM) Ultra 9 275HX",
                "N.1.18MRO24",
                24,
                24),
            ["Intel(R) Graphics", "NVIDIA GeForce RTX 5090 Laptop GPU"]);

    private static FanCurveTable CreateFanTable()
    {
        var points = Enumerable.Range(0, 16)
            .Select(index => new FanCurvePoint(
                index,
                30 + index * 4,
                25 + index * 4,
                index == 15 ? 100 : index * 6))
            .ToArray();
        return new("Balanced", true, points, points);
    }

    private sealed class RecordingSettingsStore : IApplicationSettingsStore
    {
        public ApplicationSettings? LastSaved { get; private set; }

        public bool ThrowOnSave { get; init; }

        public ApplicationSettings Load() => new();

        public void Save(ApplicationSettings settings)
        {
            if (ThrowOnSave)
            {
                throw new IOException("Simulated settings failure");
            }

            LastSaved = settings;
        }
    }

    private sealed class RecordingLightingProfileStore : ILightingProfileStore
    {
        public LightingProfile? LoadedProfile { get; init; }
        public int LoadCalls { get; private set; }

        public LightingProfile? Load()
        {
            LoadCalls++;
            return LoadedProfile;
        }

        public void Save(LightingProfile profile) { }
        public LightingProfile Import(string path) => throw new NotSupportedException();
        public void Export(string path, LightingProfile profile) { }
    }

    private sealed class RecordingStartupRegistration : IStartupRegistration
    {
        public bool LastEnabled { get; private set; }

        public bool? ForcedReadState { get; init; }

        public List<bool> SetRequests { get; } = [];

        public bool IsEnabled() => ForcedReadState ?? LastEnabled;

        public void SetEnabled(bool enabled)
        {
            SetRequests.Add(enabled);
            LastEnabled = enabled;
        }
    }

    private sealed class RecordingConflictServiceManager : IConflictServiceManager
    {
        public IReadOnlyList<ConflictServiceStatus> DetectionResult { get; init; } = [];
        public Exception? OperationException { get; init; }
        public List<string> StoppedServices { get; } = [];
        public List<string> RestoredServices { get; } = [];

        public Task<IReadOnlyList<ConflictServiceStatus>> DetectAsync(CancellationToken cancellationToken) =>
            Task.FromResult(DetectionResult);

        public Task StopAsync(IReadOnlyList<string> serviceNames, CancellationToken cancellationToken)
        {
            if (OperationException is not null) throw OperationException;
            StoppedServices.AddRange(serviceNames);
            return Task.CompletedTask;
        }

        public Task RestoreAsync(IReadOnlyList<string> serviceNames, CancellationToken cancellationToken)
        {
            if (OperationException is not null) throw OperationException;
            RestoredServices.AddRange(serviceNames);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingCpuPolicyController(CpuPolicy current) : ICpuPolicyController
    {
        private int readsAfterApply;
        public CpuPolicy Current { get; private set; } = current;
        public CpuPolicy? LastApplied { get; private set; }
        public bool RejectApply { get; init; }
        public CpuPolicy? OverrideOnSecondReadAfterApply { get; init; }

        public void SetExternal(CpuPolicy policy) => Current = policy;

        public ValueTask<CpuPolicy?> ReadCpuPolicyAsync(CancellationToken cancellationToken)
        {
            if (LastApplied is not null && ++readsAfterApply == 2 && OverrideOnSecondReadAfterApply is not null)
            {
                Current = OverrideOnSecondReadAfterApply;
            }
            return ValueTask.FromResult<CpuPolicy?>(Current);
        }

        public ValueTask<ControlResult> ApplyCpuPolicyAsync(CpuPolicy policy, CancellationToken cancellationToken)
        {
            LastApplied = policy;
            if (RejectApply)
            {
                return ValueTask.FromResult(ControlResult.Rejected("simulated power failure"));
            }

            Current = policy;
            readsAfterApply = 0;
            return ValueTask.FromResult(new ControlResult(true, "ok"));
        }
    }

    private sealed class RecordingBoostController(ProcessorBoostPolicy current) : IProcessorBoostController
    {
        private ProcessorBoostPolicy current = current;
        public List<ProcessorBoostPolicy> AppliedPolicies { get; } = [];

        public ValueTask<ProcessorBoostPolicy> ReadAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(current);

        public ValueTask<ControlResult> ApplyAsync(ProcessorBoostPolicy policy, CancellationToken cancellationToken)
        {
            AppliedPolicies.Add(policy);
            current = policy;
            return ValueTask.FromResult(new ControlResult(true, "ok"));
        }
    }

    private sealed class RecordingFrequencyLimitController(ProcessorFrequencyLimitPolicy current)
        : IProcessorFrequencyLimitController
    {
        private ProcessorFrequencyLimitPolicy current = current;
        private int readsAfterApply;
        private bool overrideUsed;
        public ProcessorFrequencyLimitPolicy? LastApplied { get; private set; }
        public List<ProcessorFrequencyLimitPolicy> AppliedPolicies { get; } = [];
        public Func<ProcessorFrequencyLimitPolicy, Task>? BeforeApply { get; init; }
        public ProcessorFrequencyLimitPolicy? OverrideOnSecondReadAfterApply { get; init; }

        public ValueTask<ProcessorFrequencyLimitPolicy> ReadAsync(CancellationToken cancellationToken)
        {
            if (LastApplied is not null && ++readsAfterApply == 2 &&
                OverrideOnSecondReadAfterApply is not null && !overrideUsed)
            {
                current = OverrideOnSecondReadAfterApply;
                overrideUsed = true;
            }
            return ValueTask.FromResult(current);
        }

        public async ValueTask<ControlResult> ApplyAsync(
            ProcessorFrequencyLimitPolicy policy,
            CancellationToken cancellationToken)
        {
            if (BeforeApply is not null) await BeforeApply(policy);
            LastApplied = policy;
            AppliedPolicies.Add(policy);
            current = policy;
            readsAfterApply = 0;
            return new ControlResult(true, "ok");
        }
    }

    private sealed class RecordingPlatformModeController(PlatformModeSelection current) : IPlatformModeController
    {
        private PlatformModeSelection current = current;
        public PlatformModeSelection? LastApplied { get; private set; }
        public bool RejectApply { get; init; }
        public bool ThrowOnRead { get; init; }

        public void SetExternal(PlatformModeSelection selection) => current = selection;

        public ValueTask<PlatformModeSelection?> ReadPlatformModeAsync(CancellationToken cancellationToken)
        {
            if (ThrowOnRead) throw new InvalidOperationException("simulated read failure");
            return ValueTask.FromResult<PlatformModeSelection?>(current);
        }

        public ValueTask<ControlResult> SetPlatformModeAsync(
            PlatformModeSelection selection,
            CancellationToken cancellationToken)
        {
            LastApplied = selection;
            if (RejectApply) return ValueTask.FromResult(ControlResult.Rejected("simulated mode failure"));
            current = selection;
            return ValueTask.FromResult(new ControlResult(true, "ok"));
        }
    }

    private sealed class RecordingGpuOutputModeController(GpuOutputModeState current) : IGpuOutputModeController
    {
        private GpuOutputModeState current = current;
        public GpuOutputMode? LastApplied { get; private set; }

        public ValueTask<GpuOutputModeState?> ReadStateAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<GpuOutputModeState?>(current);

        public ValueTask<ControlResult> SetModeAsync(GpuOutputMode mode, CancellationToken cancellationToken)
        {
            LastApplied = mode;
            current = current with { Mode = mode };
            return ValueTask.FromResult(new ControlResult(true, "已写入并回读验证；重启后生效。"));
        }
    }

    private sealed class RecordingQuickControlsController : IQuickControlsController
    {
        public Exception? DisplayFeatureReadException { get; init; }
        public ControlResult? DisplayColorResult { get; init; }
        public ControlResult? DisplayColorEnabledResult { get; init; }
        public ControlResult? LocalDimmingResult { get; init; }
        public DisplayFeatureState? DisplayFeatures { get; init; } =
            new(true, DisplayColorMode.Default, false, true, true);
        public bool KeyboardLightingEnabled { get; private set; } = true;
        public HashSet<LightingZone> AvailableLightingZones { get; } = [];
        public List<LightingZone> AppliedLightingZones { get; } = [];
        public List<LightingZoneConfiguration> AppliedLightingConfigurations { get; } = [];
        public List<(LightingZone Zone, bool Enabled)> LightingPowerRequests { get; } = [];
        public Action<LightingZoneConfiguration>? OnApplyLighting { get; set; }
        public Exception? LightingApplyException { get; init; }
        public Func<LightingZone, LightingZoneState?>? LightingReadOverride { get; set; }
        public List<int> LightingTimerRequests { get; } = [];
        public SystemToggleControl? LastSystemToggle { get; private set; }
        public SystemToggleState SystemToggles { get; set; } = new(true, false, true, true, true);
        public bool KeepStaleSystemReadbackAfterWrite { get; init; }
        public int? LastBatteryChargeLimitPercent { get; private set; }
        public bool LiquidConnected { get; set; } = true;
        public bool EmptyLiquidDiscovery { get; init; }
        public int LiquidFanDuty { get; set; } = 60;
        public bool LiquidPriming { get; set; }
        public int LiquidConnectCalls { get; private set; }
        public string? SelectedLiquidDeviceId { get; private set; } = "CoolingSystem LCT22002 (59:05)";
        private static readonly LiquidCoolingDevice[] LiquidDevices =
        [
            new("CoolingSystem LCT22002 (59:05)"),
            new("CoolingSystem LCT21001 (31:09)")
        ];

        public ValueTask<ShortcutState?> ReadShortcutsAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<ShortcutState?>(new(true, true, true));

        public ValueTask<SystemToggleState?> ReadSystemTogglesAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<SystemToggleState?>(SystemToggles);

        public PowerSettingsState? PowerSettings { get; init; } = new(true, true, false, true, true, 30, 80);

        public ValueTask<PowerSettingsState?> ReadPowerSettingsAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(PowerSettings);

        public ValueTask<KeyboardLightingState?> ReadKeyboardLightingAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<KeyboardLightingState?>(new(KeyboardLightingEnabled, "SingleZone"));

        public ValueTask<LightingZoneState?> ReadLightingZoneAsync(LightingZone zone, CancellationToken cancellationToken) =>
            ValueTask.FromResult<LightingZoneState?>(LightingReadOverride is not null ? LightingReadOverride(zone) : AvailableLightingZones.Contains(zone)
                ? new(true, "PerKey", new(zone, true, LightingEffect.Static, new(0, 180, 255), 3, 2,
                    LightingDirection.Left, 2), zone == LightingZone.Logo
                        ? [LightingEffect.Static, LightingEffect.Breathing, LightingEffect.Colorful]
                        : [LightingEffect.OemDefault, LightingEffect.Static, LightingEffect.Breathing, LightingEffect.Rainbow])
                : new(false, null, null, []));

        public ValueTask<BatteryProtectionState?> ReadBatteryProtectionAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<BatteryProtectionState?>(new(BatteryProtectionMode.Healthy, 82));

        public ValueTask<DisplayFeatureState?> ReadDisplayFeaturesAsync(CancellationToken cancellationToken) =>
            DisplayFeatureReadException is { } exception
                ? ValueTask.FromException<DisplayFeatureState?>(exception)
                : ValueTask.FromResult(DisplayFeatures);

        public ValueTask<LiquidCoolingState?> ReadLiquidCoolingAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<LiquidCoolingState?>(new(LiquidConnected, LiquidConnected ? "Connected" : "Disconnected",
                LiquidCoolingFanMode.Balanced, LiquidCoolingPumpMode.Maximum, LiquidConnected ? LiquidFanDuty : 0,
                LiquidConnected ? 90 : 0, EmptyLiquidDiscovery ? [] : LiquidDevices, SelectedLiquidDeviceId,
                "CoolingSystem LCT22002 v2.0.0.4", true, LiquidPriming, true));

        public ValueTask<LiquidCoolingState?> ScanLiquidCoolingDevicesAsync(CancellationToken cancellationToken) =>
            ReadLiquidCoolingAsync(cancellationToken);

        public ValueTask<ControlResult> SetShortcutAsync(ShortcutControl control, bool enabled, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new ControlResult(true, "按键状态已回读"));

        public ValueTask<ControlResult> SetSystemToggleAsync(SystemToggleControl control, bool enabled, CancellationToken cancellationToken)
        {
            LastSystemToggle = control;
            if (KeepStaleSystemReadbackAfterWrite)
                return ValueTask.FromResult(new ControlResult(true, "系统开关状态已回读"));
            SystemToggles = control switch
            {
                SystemToggleControl.Wifi => SystemToggles with { WifiEnabled = enabled },
                SystemToggleControl.Bluetooth => SystemToggles with { BluetoothEnabled = enabled },
                SystemToggleControl.Touchpad => SystemToggles with { TouchpadEnabled = enabled },
                SystemToggleControl.Webcam => SystemToggles with { WebcamEnabled = enabled },
                SystemToggleControl.CopilotKey => SystemToggles with { CopilotKeyEnabled = enabled },
                _ => SystemToggles
            };
            return ValueTask.FromResult(new ControlResult(true, "系统开关状态已回读"));
        }

        public ValueTask<ControlResult> SetPowerToggleAsync(PowerToggleControl control, bool enabled, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new ControlResult(true, "电源开关状态已回读"));

        public ValueTask<ControlResult> SetDeepSleepAsync(bool enabled, int minutes, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new ControlResult(true, "深度睡眠状态已回读"));

        public ValueTask<ControlResult> SetBatteryChargeLimitAsync(int percent, CancellationToken cancellationToken)
        {
            LastBatteryChargeLimitPercent = percent;
            return ValueTask.FromResult(new ControlResult(true, $"充电上限已设为 {percent}% 并回读确认"));
        }

        public ValueTask<ControlResult> SetKeyboardLightingAsync(bool enabled, CancellationToken cancellationToken)
        {
            KeyboardLightingEnabled = enabled;
            return ValueTask.FromResult(new ControlResult(true, "键盘背光已回读"));
        }

        public ValueTask<ControlResult> ApplyLightingZoneAsync(
            LightingZoneConfiguration configuration,
            CancellationToken cancellationToken)
        {
            AppliedLightingZones.Add(configuration.Zone);
            AppliedLightingConfigurations.Add(configuration);
            if (LightingApplyException is not null) throw LightingApplyException;
            OnApplyLighting?.Invoke(configuration);
            return ValueTask.FromResult(new ControlResult(true, "灯效已应用并回读"));
        }

        public ValueTask<ControlResult> SetLightingZonePowerAsync(
            LightingZone zone,
            bool enabled,
            CancellationToken cancellationToken)
        {
            LightingPowerRequests.Add((zone, enabled));
            return ValueTask.FromResult(new ControlResult(true, "分区电源已应用并回读"));
        }

        public ValueTask<ControlResult> SetLightingSleepTimerAsync(int seconds, CancellationToken cancellationToken)
        {
            LightingTimerRequests.Add(seconds);
            return ValueTask.FromResult(new ControlResult(true, "灯效休眠已应用"));
        }

        public ValueTask<ControlResult> SetBatteryProtectionAsync(BatteryProtectionMode mode, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new ControlResult(true, "电池模式已回读"));

        public ValueTask<ControlResult> SetDisplayColorAsync(DisplayColorMode mode, CancellationToken cancellationToken) =>
            ValueTask.FromResult(DisplayColorResult ?? new ControlResult(true, "色彩已提交"));

        public ValueTask<ControlResult> SetDisplayColorEnabledAsync(bool enabled, DisplayColorMode mode, CancellationToken cancellationToken) =>
            ValueTask.FromResult(DisplayColorEnabledResult ?? new ControlResult(true, enabled ? "色彩管理已开启" : "色彩管理已关闭"));

        public ValueTask<ControlResult> SetLocalDimmingAsync(bool enabled, CancellationToken cancellationToken) =>
            ValueTask.FromResult(LocalDimmingResult ?? new ControlResult(true, enabled ? "局部调光已开启" : "局部调光已关闭"));

        public ValueTask<ControlResult> SetLiquidCoolingFanAsync(LiquidCoolingFanMode mode, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new ControlResult(true, "水冷风扇已回读"));

        public ValueTask<ControlResult> SetLiquidCoolingPumpAsync(LiquidCoolingPumpMode mode, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new ControlResult(true, "水冷泵已回读"));

        public ValueTask<ControlResult> SelectLiquidCoolingDeviceAsync(string deviceId, CancellationToken cancellationToken)
        {
            SelectedLiquidDeviceId = deviceId;
            return ValueTask.FromResult(new ControlResult(true, "设备已选择"));
        }

        public ValueTask<ControlResult> ConnectLiquidCoolingAsync(CancellationToken cancellationToken)
        {
            LiquidConnectCalls++;
            LiquidConnected = true;
            return ValueTask.FromResult(new ControlResult(true, "设备已连接"));
        }

        public ValueTask<ControlResult> DisconnectLiquidCoolingAsync(CancellationToken cancellationToken)
        {
            LiquidConnected = false;
            return ValueTask.FromResult(new ControlResult(true, "设备已断开"));
        }

        public ValueTask<ControlResult> StartLiquidCoolingPrimingAsync(CancellationToken cancellationToken)
        {
            LiquidPriming = true;
            return ValueTask.FromResult(new ControlResult(true, "注水模式已启动"));
        }

        public ValueTask<ControlResult> ResetLiquidCoolingDeviceAsync(CancellationToken cancellationToken)
        {
            LiquidConnected = false;
            SelectedLiquidDeviceId = null;
            return ValueTask.FromResult(new ControlResult(true, "设备记录已清除"));
        }
    }

    private sealed class RecordingCustomPerformanceController : ICustomPerformanceController
    {
        public GpuClockOffsets? LastOffsets { get; private set; }
        public (CustomPerformanceSetting, int)? LastSetting { get; private set; }
        public ControlResult? SingleSettingResultOverride { get; init; }
        public void ClearLastSetting() => LastSetting = null;
        public ValueTask<ControlResult> ApplySettingAsync(CustomPerformanceSetting setting, int value, CancellationToken token)
        {
            LastSetting = (setting, value);
            return ValueTask.FromResult(SingleSettingResultOverride ?? new ControlResult(true, "单项参数已回读确认"));
        }
        public ValueTask<GpuClockOffsetState?> ReadGpuClockOffsetsAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<GpuClockOffsetState?>(new(true, true, new(100, 500), new(-300, 250, -1000, 1500), new(95, 150, 5, 25)));
        public ValueTask<ControlResult> ApplyGpuClockOffsetsAsync(GpuClockOffsets offsets, CancellationToken cancellationToken)
        {
            LastOffsets = offsets;
            return ValueTask.FromResult(new ControlResult(true, "GPU 偏移已回读确认"));
        }
        private CustomPerformanceState state = new(true, true, 95, 145, true, 150, true, 25, 100, 500);
        public CustomPerformanceState? LastApplied { get; private set; }
        public GpuPerformanceState? LastGpuApplied { get; private set; }
        public bool PreserveStaleReadbackAfterApply { get; init; }

        public ValueTask<CustomPerformanceState?> ReadAsync(int profileIndex, CancellationToken cancellationToken) =>
            ValueTask.FromResult<CustomPerformanceState?>(state);

        public ValueTask<ControlResult> ApplyAsync(
            int profileIndex, CustomPerformanceState target, CancellationToken cancellationToken)
        {
            LastApplied = target;
            if (!PreserveStaleReadbackAfterApply)
                state = target;
            return ValueTask.FromResult(new ControlResult(true, "自定义参数已回读确认"));
        }

        public ValueTask<GpuPerformanceState?> ReadGpuAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<GpuPerformanceState?>(new(
                state.OverclockingEnabled,
                state.GpuConfigurableTgpWatts,
                state.GpuDynamicBoostEnabled,
                state.GpuDynamicBoostWatts,
                state.GpuCoreClockOffsetMhz,
                state.GpuMemoryClockOffsetMhz));

        public ValueTask<ControlResult> ApplyGpuAsync(
            GpuPerformanceState target, CancellationToken cancellationToken)
        {
            LastGpuApplied = target;
            return ValueTask.FromResult(new ControlResult(true, "GPU 参数已回读确认"));
        }
    }

    private sealed class RecordingDisplayController(DisplaySnapshot snapshot) : IDisplayController
    {
        private DisplaySnapshot current = snapshot;
        public int? LastAppliedRefreshRateHz { get; private set; }
        public List<int> AppliedRefreshRatesHz { get; } = [];
        public int TemporaryCalls { get; private set; }
        public int RestoreCalls { get; private set; }
        public Func<Task>? BeforeRead { get; set; }

        public async ValueTask<DisplaySnapshot> ReadAsync(CancellationToken cancellationToken)
        {
            if (BeforeRead is { } read) await read();
            return current;
        }

        public ValueTask<ControlResult> SetTemporaryRefreshRateAsync(int rate, CancellationToken cancellationToken)
        {
            TemporaryCalls++;
            return SetRefreshRateAsync(rate, cancellationToken);
        }

        public ValueTask<ControlResult> RestoreTemporaryRefreshRateAsync(CancellationToken cancellationToken)
        {
            RestoreCalls++;
            return ValueTask.FromResult(new ControlResult(true, "Restored"));
        }

        public ValueTask<ControlResult> SetRefreshRateAsync(int refreshRateHz, CancellationToken cancellationToken)
        {
            LastAppliedRefreshRateHz = refreshRateHz;
            AppliedRefreshRatesHz.Add(refreshRateHz);
            current = current with { CurrentRefreshRateHz = refreshRateHz };
            return ValueTask.FromResult(new ControlResult(true, $"已切换到 {refreshRateHz} Hz"));
        }
    }

    private sealed class RecordingFanController(
        FanCurveTable table,
        FanResponseSettings response) : IOemFanController
    {
        public FanCurveTable CurrentTable { get; private set; } = table;
        public FanResponseSettings CurrentResponse { get; private set; } = response;
        public FanCurveTable? LastAppliedTable { get; private set; }
        public FanResponseSettings? LastAppliedResponse { get; private set; }
        public FanCurveTable? RestoredTable { get; init; }
        public int RestoreCallCount { get; private set; }

        public ValueTask<FanCurveTable?> ReadAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<FanCurveTable?>(CurrentTable);

        public ValueTask<FanResponseSettings?> ReadResponseAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<FanResponseSettings?>(CurrentResponse);

        public ValueTask<ControlResult> ApplyAsync(
            FanCurveTable target,
            FanResponseSettings targetResponse,
            CancellationToken cancellationToken)
        {
            LastAppliedTable = target;
            LastAppliedResponse = targetResponse;
            CurrentTable = target with { IndependentCpuGpuControl = targetResponse.IndependentCpuGpuControl };
            CurrentResponse = targetResponse;
            return ValueTask.FromResult(new ControlResult(true, "风扇设置已写入并回读确认。"));
        }

        public ValueTask<FanRestoreResult> RestoreAsync(string profileName, CancellationToken cancellationToken)
        {
            RestoreCallCount++;
            CurrentTable = RestoredTable ?? CurrentTable;
            return ValueTask.FromResult(new FanRestoreResult(
                new ControlResult(true, "已恢复原厂默认曲线并回读确认。"), CurrentTable));
        }
    }
}
