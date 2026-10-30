using System.IO;
using System.Windows;
using RevoHelper.App.Presentation;
using RevoHelper.App.Settings;
using RevoHelper.App.Lifecycle;
using RevoHelper.App.SystemControl;
using RevoHelper.App.Monitoring;
using RevoHelper.App.Charging;
using RevoHelper.Core.Control;
using RevoHelper.Core.Control.DynamicPower;
using RevoHelper.Core.Dashboard;
using RevoHelper.Core.Discovery;
using RevoHelper.Core.Hardware;
using RevoHelper.Hardware.Windows.Discovery;
using RevoHelper.Hardware.Windows.Telemetry;
using RevoHelper.Hardware.Windows.OemStatus;
using RevoHelper.Hardware.Windows.OemProfiles;
using RevoHelper.Hardware.Windows.OemControl;
using RevoHelper.Hardware.Windows.PowerPolicy;
using RevoHelper.Hardware.Windows.CpuPower;
using RevoHelper.Hardware.Windows.Display;
using Microsoft.Win32;

namespace RevoHelper.App;

public partial class App : Application
{
    private IHardwareTelemetrySource? telemetrySource;
    private CancellationTokenSource? telemetryCancellation;
    private Task? telemetryTask;
    private IOemStatusSource? oemStatusSource;
    private CancellationTokenSource? oemStatusCancellation;
    private Task? oemStatusTask;
    private SystemTrayController? systemTrayController;
    private MainWindowViewModel? mainViewModel;
    private ApplicationInstanceGuard? instanceGuard;
    private MachineIdentity? capabilityIdentity;
    private OemControlInventory? capabilityInventory;
    private IReadOnlyList<CpuBackendCapability> capabilityCpuBackends = [];
    private OemRuntimeEvidence capabilityOemEvidence = OemRuntimeEvidence.None;
    private IGpuOutputModeController? capabilityGpuOutputController;
    private Task? capabilityProbeTask;
    private RetryableDiscoveryCoordinator? hardwareDiscovery;
    private DeferredCpuBackendProbe? directBackendProbe;
    private CpuBackendCapability? directCpuCapability;
    private MachineIdentity? lastDiscoveredIdentity;
    private bool powerProfilesDiscovered;
    private readonly CancellationTokenSource applicationLifetime = new();
    private bool isExiting;
    private SwitchableCpuPolicyController? directCpuPolicyController;
    private PlatformAwareCpuPolicyController? platformAwareCpuPolicyController;
    private ExperimentalControlsViewModel? experiments;
    private ExperimentalChargeClient? experimentalCharge;
    private SessionChargeController? sessionCharge;
    private WindowsProcessorFrequencyLimitController? processorFrequencyLimitController;
    private WindowsProcessorBoostController? processorBoostController;
    private PerformanceMonitorController? performanceMonitor;
    private bool previewOnly;
    private System.Windows.Forms.PowerLineStatus lastPowerLineStatus;
    private readonly WindowsDisplayController displayController = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (ExperimentalChargeHelperHost.IsHelper(e.Args))
        {
            previewOnly = true; // No settings, single-instance UI or vendor initialization.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Shutdown(ExperimentalChargeHelperHost.Run(e.Args));
            return;
        }
        if (e.Args is ["--snapshot-preview", var outputDirectory])
        {
            previewOnly = true;
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            SnapshotPreview.Render(outputDirectory);
            Shutdown();
            return;
        }
        var manualChargeLimit = ManualExperimentalChargeLaunch.ReadLimit(e.Args);
        var launchedFromWindowsStartup = StartupCommand.IsStartupLaunch(e.Args);
        var settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RevoHelper", "settings.json");
        var settingsStore = new JsonApplicationSettingsStore(settingsPath);
        var settings = settingsStore.Load();
        var english = LocalizedUiCopy.NormalizeLanguageCode(settings.UiLanguageCode) == "en-US";

        instanceGuard = new ApplicationInstanceGuard();
        if (!instanceGuard.TryAcquire())
        {
            MessageBox.Show(
                english ? "RevoHelper is already running. Exit the old instance from the system tray before starting this version.\n\nRevoHelper will not stop GCU, XTU, or other vendor services." : "检测到另一个 RevoHelper 正在运行。请先从系统托盘退出旧实例，再启动新版测试程序。\n\nRevoHelper 不会结束 GCU、XTU 或其他厂商服务。",
                english ? "RevoHelper is already running" : "RevoHelper 已在运行",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        var controlConflictDetector = new ControlConflictDetector();
        var controlConflicts = controlConflictDetector.Detect();
        if (controlConflicts.Count > 0 && !launchedFromWindowsStartup && manualChargeLimit is null)
        {
            var conflictList = string.Join("\n", controlConflicts.Select(conflict => $"• {conflict.DisplayName}"));
            MessageBox.Show(
                english ? $"These apps may also change performance, power, fan, or lighting settings:\n\n{conflictList}\n\nRevoHelper can coexist with the official control center. It will not stop or disable it."
                    : $"检测到以下也可能写入性能、功耗、风扇或灯光设置的控制程序：\n\n{conflictList}\n\n" +
                    "RevoHelper可以与官方控制台共存，不会停用或禁用它们",
                english ? "Other control apps" : "OEM 前台会话提醒",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        var dashboard = HomeDashboardFactory.Create(
            new MachineIdentity(
                "MECHREVO",
                "正在识别设备",
                string.Empty,
                "正在识别处理器",
                string.Empty),
            []);
        var startupRegistration = new WindowsStartupRegistration(
            Environment.ProcessPath ?? "RevoHelper.exe");

        try
        {
            settings = settings with
            {
                // Experimental exports must not migrate the stable version's startup task.
                StartWithWindows = startupRegistration.IsEnabled()
            };
        }
        catch (Exception exception)
        {
            // Keep the saved UI state if Windows startup registration cannot be queried or migrated.
            RuntimeDiagnosticLog.Write("startup-registration", $"{exception.GetType().Name}: {exception.Message}");
        }

        var gcuStatusSource = new GcuMqttStatusSource();
        oemStatusSource = gcuStatusSource;
        var gpuOutputController = new GcuGpuOutputModeController(
            gcuStatusSource,
            new WindowsGpuOutputModeCapabilitySource());
        var platformModeController = new GcuPlatformModeController(gcuStatusSource);
        directCpuPolicyController = new SwitchableCpuPolicyController();
        platformAwareCpuPolicyController = new PlatformAwareCpuPolicyController(
            platformModeController,
            new GcuCpuPowerLimitController(gcuStatusSource),
            directCpuPolicyController);
        var settingsDirectory = Path.GetDirectoryName(settingsPath)!;
        processorBoostController = new WindowsProcessorBoostController(
            new PowrProfPowerProfileApi(),
            new JsonProcessorBoostCompatibilityLeaseStore(Path.Combine(settingsDirectory, "boost-compatibility-lease.json")));
        var dynamicPowerController = new DynamicPowerController(
            new WindowsDynamicFrequencyBackend(new PowrProfPowerProfileApi()),
            new JsonDynamicPowerLeaseStore(Path.Combine(settingsDirectory, "dynamic-power-lease.json")));
        processorFrequencyLimitController = new WindowsProcessorFrequencyLimitController(
            new PowrProfPowerProfileApi(),
            new JsonProcessorFrequencyLimitLeaseStore(
                Path.Combine(settingsDirectory, "manual-frequency-lease.json")));

        // Recover both frequency writers before any profile initialization can read or
        // reapply stale state. The durable journals remain intact if recovery times out.
        using (var recoveryTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
        {
            BoundedShutdown.Run(async () =>
            {
                await processorBoostController.RecoverAsync(recoveryTimeout.Token).ConfigureAwait(false);
                await dynamicPowerController.RecoverAsync(recoveryTimeout.Token).ConfigureAwait(false);
                if (dynamicPowerController.Status.HasRecoveryPending)
                {
                    return;
                }

                var recovered = await processorFrequencyLimitController
                    .RecoverAsync(recoveryTimeout.Token).ConfigureAwait(false);
                if (recovered.Succeeded)
                {
                    await LegacyFrequencyLimitMigration.RunAsync(
                        settingsStore,
                        processorFrequencyLimitController,
                        recoveryTimeout.Token).ConfigureAwait(false);
                }
            }, TimeSpan.FromSeconds(3));
        }
        settings = settingsStore.Load();
        try
        {
            settings = settings with
            {
                StartWithWindows = startupRegistration.IsEnabled()
            };
        }
        catch (Exception exception)
        {
            // Keep the saved UI state if task readback is temporarily unavailable.
            RuntimeDiagnosticLog.Write("startup-registration", $"{exception.GetType().Name}: {exception.Message}");
        }

        experimentalCharge = new ExperimentalChargeClient();
        experimentalCharge.InspectPendingRecovery();
        sessionCharge = new SessionChargeController(experimentalCharge,
            () => System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online);
        experiments = new ExperimentalControlsViewModel(
            dynamicPowerController,
            new BootWaterTaskManager(new WindowsBootWaterTaskPlatform(), WindowsBootWaterTaskPlatform.InstalledPath),
            action => { if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background, action); },
            chargeController: sessionCharge);
        var viewModel = new MainWindowViewModel(
            dashboard,
            settings,
            settingsStore,
            startupRegistration,
            platformAwareCpuPolicyController,
            processorBoostController,
            platformModeController,
            gpuOutputController,
            processorFrequencyLimitController,
            conflictServiceManager: new WindowsConflictServiceManager(),
            quickControlsController: new GcuQuickControlsController(gcuStatusSource),
            displayController: displayController,
            officialConsoleRunningProbe: controlConflictDetector.IsOfficialConsoleRunning,
            customPerformanceController: new GcuCustomPerformanceController(gcuStatusSource,
                readMode: platformModeController.ReadPlatformModeAsync,
                readEnforcedGpuPowerLimit: NvidiaGpuPowerLimitReader.ReadAsync,
                powerBudget: new GpuPowerBudgetController(gcuStatusSource,
                    platformModeController.ReadPlatformModeAsync, NvidiaGpuPowerLimitReader.ReadAsync,
                    new JsonGpuPowerBudgetStore(Path.Combine(settingsDirectory, "gpu-power-budgets.json")))),
            lightingProfileStore: new JsonLightingProfileStore(JsonLightingProfileStore.DefaultPath),
            fanController: new GcuFanController(gcuStatusSource, readMode: platformModeController.ReadPlatformModeAsync),
            experiments: experiments,
            fanBoostController: new GcuFanBoostController(gcuStatusSource),
            shutdownScheduler: new WindowsShutdownScheduler());
        var window = new MainWindow(viewModel);
        mainViewModel = viewModel;

        MainWindow = window;
        performanceMonitor = new PerformanceMonitorController(Path.Combine(settingsDirectory, "performance-monitor.json"), viewModel.Monitor);
        performanceMonitor.SettingsRequested += () => ShowPerformanceMonitorSettings(window);
        capabilityIdentity = dashboard.Identity;
        capabilityInventory = UnprobedOemInventory();
        capabilityCpuBackends = new WindowsCpuBackendProbe().Probe(dashboard.Identity.Cpu);
        RefreshRuntimeCapabilities(viewModel);
        window.Show();
        if (manualChargeLimit is int manualLimit)
            _ = ApplyExplicitChargeLaunchAsync(viewModel, manualLimit);
        viewModel.CapabilityRecheckRequested += () =>
        {
            StartDeferredHardwareDiscovery(viewModel);
            StartRuntimeCapabilityProbe(gcuStatusSource, gpuOutputController, viewModel);
        };
        performanceMonitor.Initialize();
        _ = ApplyAutomaticRefreshRateForCurrentPowerSourceAsync(viewModel);
        StartConflictServiceDetection(viewModel);
        StartDeferredHardwareDiscovery(viewModel);
        systemTrayController = new(window, viewModel);
        if (launchedFromWindowsStartup)
        {
            window.Hide();
        }
        lastPowerLineStatus = System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus;
        SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;

        StartTelemetry(viewModel);
        StartOemStatus(viewModel);
        _ = viewModel.TryAutoConnectLiquidCoolingAsync(CancellationToken.None);
        _ = viewModel.MonitorLiquidCoolingAsync(applicationLifetime.Token);
        StartRuntimeCapabilityProbe(gcuStatusSource, gpuOutputController, viewModel);
        _ = InitializePlatformModeWhenReadyAsync(viewModel);
        _ = InitializePerformancePolicyWhenReadyAsync(viewModel);
        _ = SynchronizeScheduleWhenReadyAsync(viewModel);
    }

    private void StartConflictServiceDetection(MainWindowViewModel viewModel)
    {
        _ = viewModel.RefreshConflictServicesAsync(CancellationToken.None);
    }

    private static Task ApplyAutomaticRefreshRateForCurrentPowerSourceAsync(
        MainWindowViewModel viewModel)
    {
        var status = System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus;
        if (status == System.Windows.Forms.PowerLineStatus.Unknown) return Task.CompletedTask;
        return viewModel.ApplyAutomaticRefreshRateForPowerSourceAsync(
            status == System.Windows.Forms.PowerLineStatus.Online, CancellationToken.None);
    }

    private void StartDeferredHardwareDiscovery(MainWindowViewModel viewModel)
    {
        if (isExiting) return;
        hardwareDiscovery ??= new RetryableDiscoveryCoordinator(
            token => DiscoverHardwareAsync(viewModel, token),
            busy => DispatchDiscoveryUpdateAsync(() => viewModel.SetCapabilityDetectionBusy(busy)),
            exception => RuntimeDiagnosticLog.Write("hardware-discovery", $"{exception.GetType().Name}: {exception.Message}"));
        _ = hardwareDiscovery.RequestAsync();
    }

    private async Task<bool> DiscoverHardwareAsync(MainWindowViewModel viewModel, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IWmiQuery query = new RetryingWmiQuery(
            new SystemManagementWmiQuery(),
            maxAttempts: 3,
            delay: TimeSpan.FromMilliseconds(200));
        var identity = ReadIdentity(query);
        var displayAdapters = ReadDisplayAdapters(query);
        cancellationToken.ThrowIfCancellationRequested();
        OemControlInventory inventory;
        try
        {
            inventory = new OemControlInventoryReader(new WindowsOemEnvironmentProbe(query)).Read();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            inventory = new([], false, $"OEM control-chain probe failed: {exception.GetType().Name}");
        }
        IReadOnlyList<CpuPowerLimitProfile> powerProfiles = [];
        var profileEvidence = OemRuntimeEvidence.None;
        var profileDirectory = OemCpuPowerLimitProfileReader.ResolveProfileDirectory(inventory);
        try
        {
            powerProfiles = new OemCpuPowerLimitProfileReader(profileDirectory).Read();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            RuntimeDiagnosticLog.Write("power-profile-discovery", exception.GetType().Name);
        }
        try
        {
            // Basic mode discovery does not depend on CPU PL fields being available.
            profileEvidence = new OemPlatformModeProfileReader(profileDirectory).ReadEvidence();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            RuntimeDiagnosticLog.Write("mode-profile-discovery", exception.GetType().Name);
        }

        cancellationToken.ThrowIfCancellationRequested();
        await DispatchDiscoveryUpdateAsync(() =>
        {
            // Only prior real observations participate; the initial dashboard placeholder
            // must never turn an unknown manufacturer into a confirmed MECHREVO identity.
            identity = DiscoveryIdentityMerger.Merge(lastDiscoveredIdentity, identity);
            lastDiscoveredIdentity = identity;
            var dashboard = HomeDashboardFactory.Create(identity, displayAdapters);
            var cpuBackends = new WindowsCpuBackendProbe().Probe(identity.Cpu);
            viewModel.UpdateDashboard(dashboard);
            var firstProfilesRead = !powerProfilesDiscovered && powerProfiles.Count > 0;
            viewModel.ApplyCpuPowerLimitProfiles(powerProfiles, preserveDrafts: powerProfilesDiscovered);
            if (powerProfiles.Count > 0) powerProfilesDiscovered = true;
            capabilityIdentity = identity;
            capabilityInventory = inventory;
            capabilityCpuBackends = MergeDirectCpuCapability(cpuBackends, directCpuCapability);
            capabilityOemEvidence = MergeEvidence(capabilityOemEvidence,
                MergeEvidence(profileEvidence, OemCpuPowerLimitProfileCapabilityMapper.Map(powerProfiles)));
            RefreshRuntimeCapabilities(viewModel);
            if (firstProfilesRead) _ = InitializePerformancePolicyWhenReadyAsync(viewModel);
            // This separate one-shot worker can neither hold up OEM controls nor repeat
            // privileged write verification on reconnect or a manual read-only recheck.
            StartDirectCpuBackendProbe(identity.Cpu, viewModel);
        }).ConfigureAwait(false);

        var knownIdentity = !string.IsNullOrWhiteSpace(identity.Manufacturer) &&
            !identity.Manufacturer.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase);
        return knownIdentity && identity.Cpu.Vendor is not CpuVendor.Unknown &&
            (!identity.Manufacturer.Trim().Equals("MECHREVO", StringComparison.OrdinalIgnoreCase) ||
             inventory.HasCompletePlatformModeChain);
    }

    private Task DispatchDiscoveryUpdateAsync(Action update)
    {
        if (isExiting || Dispatcher.HasShutdownStarted) return Task.CompletedTask;
        return Dispatcher.InvokeAsync(() =>
        {
            if (!isExiting && !Dispatcher.HasShutdownStarted) update();
        }, System.Windows.Threading.DispatcherPriority.Background, applicationLifetime.Token).Task;
    }

    private void StartDirectCpuBackendProbe(CpuIdentity cpu, MainWindowViewModel viewModel)
    {
        if (isExiting) return;
        directBackendProbe ??= new DeferredCpuBackendProbe(
            (identity, token) => new DirectCpuPolicyBackendProbe().ProbeAsync(identity, AppContext.BaseDirectory, token),
            action => Dispatcher.InvokeAsync(action, System.Windows.Threading.DispatcherPriority.Background,
                applicationLifetime.Token).Task,
            result =>
            {
                if (result.Controller is not null) directCpuPolicyController!.SetController(result.Controller);
                // Ownership has transferred. A presentation error must not dispose the
                // installed controller while leaving the proxy pointing at it.
                try
                {
                    directCpuCapability = result.Capability;
                    capabilityCpuBackends = MergeDirectCpuCapability(capabilityCpuBackends, result.Capability);
                    RefreshRuntimeCapabilities(viewModel);
                    _ = InitializePerformancePolicyWhenReadyAsync(viewModel);
                }
                catch (Exception exception)
                {
                    RuntimeDiagnosticLog.Write("cpu-backend-presentation", $"{exception.GetType().Name}: {exception.Message}");
                }
            },
            exception => RuntimeDiagnosticLog.Write("cpu-backend-discovery", $"{exception.GetType().Name}: {exception.Message}"));
        _ = directBackendProbe.StartAsync(cpu);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (previewOnly) { base.OnExit(e); return; }
        isExiting = true;
        sessionCharge?.RequestStop("程序退出，独立监护继续恢复原值。");
        applicationLifetime.Cancel();
        hardwareDiscovery?.Dispose();
        directBackendProbe?.Dispose();
        performanceMonitor?.CloseWindows();
        mainViewModel?.BeginShutdown();
        systemTrayController?.Dispose();
        SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged;
        using (var displayTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
            BoundedShutdown.Run(() => displayController.StopAndRestoreAsync(displayTimeout.Token).AsTask(),
                TimeSpan.FromSeconds(3));
        // Do not let the manual layer mistake an unrestored dynamic cap for an
        // external change and discard the original session baseline.
        using (var frequencyTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(6)))
        {
            var recovered = false;
            var completed = BoundedShutdown.Run(async () => recovered = await LayeredFrequencyRecovery.RunAsync(
                token => experiments?.ShutdownAsync(token) ?? Task.FromResult(true),
                async token => processorFrequencyLimitController is null ||
                    (await processorFrequencyLimitController.RecoverAsync(token).ConfigureAwait(false)).Succeeded,
                frequencyTimeout.Token).ConfigureAwait(false), TimeSpan.FromSeconds(6));
            if (!completed) frequencyTimeout.Cancel();
            if (!completed || !recovered)
                RuntimeDiagnosticLog.Write("frequency-recovery", "Exit recovery incomplete; dependent recovery journals retained for next launch.");
        }
        experiments?.Dispose();
        sessionCharge?.Dispose();
        experimentalCharge?.Dispose();
        if (platformAwareCpuPolicyController is not null)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            BoundedShutdown.Run(() => platformAwareCpuPolicyController.RestoreActivePresetBaselineAsync(timeout.Token).AsTask(),
                TimeSpan.FromSeconds(3));
        }
        if (processorBoostController is not null)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            BoundedShutdown.Run(() => processorBoostController.RecoverAsync(timeout.Token).AsTask(), TimeSpan.FromSeconds(3));
        }
        if (performanceMonitor is not null)
            BoundedShutdown.Run(() => performanceMonitor.DisposeAsync().AsTask(), TimeSpan.FromSeconds(3));
        // Even a synchronous native read/dispose must not hold the UI shutdown forever.
        // On timeout the process exits; a frequency lease remains available on next launch.
        BoundedShutdown.Run(async () =>
        {
            if (telemetryCancellation is not null) await telemetryCancellation.CancelAsync().ConfigureAwait(false);
            if (oemStatusCancellation is not null) await oemStatusCancellation.CancelAsync().ConfigureAwait(false);
            try { if (telemetryTask is not null) await telemetryTask.ConfigureAwait(false); } catch (Exception) { }
            if (telemetrySource is not null)
                try { await telemetrySource.DisposeAsync().ConfigureAwait(false); } catch (Exception) { }
            try { if (oemStatusTask is not null) await oemStatusTask.ConfigureAwait(false); } catch (Exception) { }
            if (oemStatusSource is not null)
                try { await oemStatusSource.DisposeAsync().ConfigureAwait(false); } catch (Exception) { }
            telemetryCancellation?.Dispose();
            oemStatusCancellation?.Dispose();
            directCpuPolicyController?.Dispose();
        }, TimeSpan.FromSeconds(1));
        instanceGuard?.Dispose();
        base.OnExit(e);
    }

    private void StartTelemetry(MainWindowViewModel viewModel)
    {
        var displayUpdates = new LatestValueDispatcher<RevoHelper.Core.Telemetry.HardwareTelemetrySnapshot>(
            action => Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, action),
            viewModel.ApplyTelemetry);
        if (MainWindow is { } window)
        {
            void UpdateVisibility() => displayUpdates.SetPresentationEnabled(
                window.IsVisible && window.WindowState != WindowState.Minimized);
            window.IsVisibleChanged += (_, _) => UpdateVisibility();
            window.StateChanged += (_, _) => UpdateVisibility();
            UpdateVisibility();
        }
        telemetrySource = new LibreHardwareTelemetrySource();
        telemetryCancellation = new CancellationTokenSource();
        var pump = new TelemetryPump(telemetrySource, TimeSpan.FromSeconds(1));
        telemetryTask = pump.RunAsync(
            snapshot =>
            {
                performanceMonitor?.Publish(snapshot);
                displayUpdates.Publish(snapshot);
                _ = experiments?.ObserveAsync(snapshot);
                return ValueTask.CompletedTask;
            },
            exception =>
            {
                Dispatcher.BeginInvoke(() => viewModel.ReportTelemetryUnavailable(exception.Message));
                _ = experiments?.PauseAutomaticallyAsync("遥测不可用，动态节能已暂停。");
                return ValueTask.CompletedTask;
            },
            telemetryCancellation.Token);
    }

    private static async Task ApplyExplicitChargeLaunchAsync(MainWindowViewModel viewModel, int limit)
    {
        // The command-line switch is an explicit, risk-accepting manual launch,
        // not a saved preference. Ordinary double-click/startup never enters here.
        await viewModel.OpenBatteryAsync(CancellationToken.None);
        viewModel.Experiments.Charge.SelectedLimitPercent = limit;
        await viewModel.Experiments.Charge.EnableAsync();
    }

    public void ShowPerformanceMonitorSettings(Window owner)
    {
        if (owner is not MainWindow window) return;
        if (!window.IsVisible) window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.ShowPerformanceMonitorSettings();
        window.Activate();
    }

    private void StartOemStatus(MainWindowViewModel viewModel)
    {
        oemStatusSource ??= new GcuMqttStatusSource();
        oemStatusCancellation = new CancellationTokenSource();
        oemStatusTask = oemStatusSource.RunAsync(
            update =>
            {
                if (update is OemCpuStatusUpdate cpuSample)
                    performanceMonitor?.PublishOemCpuTemperature(cpuSample.Status.TemperatureC);
                if (update is OemConnectionStateUpdate connection)
                    RuntimeDiagnosticLog.Write("gcu-connection", $"{connection.State}: {connection.Message}");
                Dispatcher.BeginInvoke(() =>
                {
                    switch (update)
                    {
                        case OemCpuStatusUpdate cpu:
                            viewModel.ApplyOemCpuStatus(cpu.Status,
                                MainWindow is { IsVisible: true, WindowState: not WindowState.Minimized });
                            break;
                        case OemFanTableUpdate fan:
                            viewModel.ApplyFanTable(fan.Table);
                            capabilityOemEvidence = capabilityOemEvidence with { HasFanCurves = true };
                            RefreshRuntimeCapabilities(viewModel);
                            break;
                        case OemPlatformModeUpdate platform:
                            _ = viewModel.ObservePlatformModeAsync(platform.Selection, CancellationToken.None);
                            break;
                        case OemRuntimeCapabilitiesUpdate capabilities:
                            var merged = MergeEvidence(capabilityOemEvidence, capabilities.Evidence);
                            if (!ReferenceEquals(merged, capabilityOemEvidence))
                            {
                                capabilityOemEvidence = merged;
                                RefreshRuntimeCapabilities(viewModel);
                            }
                            break;
                        case OemLightingZoneUpdate lighting:
                            viewModel.ApplyLightingZoneState(lighting.Zone, lighting.State);
                            break;
                        case OemControlSessionUpdate:
                            StartDeferredHardwareDiscovery(viewModel);
                            if (oemStatusSource is GcuMqttStatusSource source && capabilityGpuOutputController is { } gpu)
                                StartRuntimeCapabilityProbe(source, gpu, viewModel);
                            _ = viewModel.SynchronizeControlSessionAsync(CancellationToken.None);
                            break;
                        case OemConnectionStateUpdate connectionState:
                            viewModel.ReportOemConnectionState(connectionState.Message);
                            break;
                    }
                });
                return ValueTask.CompletedTask;
            },
            exception =>
            {
                RuntimeDiagnosticLog.Write("gcu-error", $"{exception.GetType().Name}: {exception.Message}");
                Dispatcher.BeginInvoke(() => viewModel.ReportOemStatusUnavailable(exception.Message));
                return ValueTask.CompletedTask;
            },
            oemStatusCancellation.Token);
    }

    private void StartRuntimeCapabilityProbe(GcuMqttStatusSource source,
        IGpuOutputModeController gpu, MainWindowViewModel viewModel)
    {
        if (isExiting) return;
        capabilityGpuOutputController = gpu;
        if (capabilityProbeTask is { IsCompleted: false }) return;
        capabilityProbeTask = ProbeRuntimeCapabilitiesWhenReadyAsync(source, gpu, viewModel);
    }

    private async Task ProbeRuntimeCapabilitiesWhenReadyAsync(
        GcuMqttStatusSource gcuStatusSource,
        IGpuOutputModeController gpuOutputController,
        MainWindowViewModel viewModel)
    {
        for (var attempt = 0; attempt < 12; attempt++)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), applicationLifetime.Token);
                var status = await gcuStatusSource.ReadStatusAsync(applicationLifetime.Token);
                var evidence = GcuRuntimeCapabilityMapper.Map(status);
                GpuOutputModeState? gpuState = null;
                try
                {
                    gpuState = await gpuOutputController.ReadStateAsync(applicationLifetime.Token);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // GPU output mode is an independent optional capability.
                }

                evidence = evidence with
                {
                    HasGpuOutputModes = gpuState is not null && HasAnyGpuOutputMode(gpuState.Capabilities)
                };
                await DispatchDiscoveryUpdateAsync(() =>
                {
                    capabilityOemEvidence = MergeEvidence(capabilityOemEvidence, evidence);
                    if (gpuState is not null) viewModel.ApplyGpuOutputState(gpuState);
                    RefreshRuntimeCapabilities(viewModel);
                    _ = viewModel.SynchronizeControlSessionAsync(applicationLifetime.Token);
                });
                if (evidence.SupportedPlatformModes.Count > 0) return;
            }
            catch (OperationCanceledException) when (applicationLifetime.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The GCU observer owns reconnection. Each new session restarts a bounded probe.
            }
        }
    }

    private void RefreshRuntimeCapabilities(MainWindowViewModel viewModel)
    {
        var identity = capabilityIdentity ?? viewModel.Dashboard.Identity;
        var inventory = capabilityInventory ?? UnprobedOemInventory();
        viewModel.ApplyRuntimeCapabilities(RuntimeCapabilityFactory.Create(
            identity,
            inventory,
            capabilityOemEvidence,
            capabilityCpuBackends));
    }

    private static OemRuntimeEvidence MergeEvidence(
        OemRuntimeEvidence current,
        OemRuntimeEvidence update) => RuntimeEvidenceMerger.Merge(current, update);

    private static bool HasAnyGpuOutputMode(GpuOutputModeCapabilities capabilities) =>
        capabilities.SupportsHybrid || capabilities.SupportsDiscrete || capabilities.SupportsIntegrated;

    private static OemControlInventory UnprobedOemInventory() =>
        new([], false, "OEM control chain has not been probed yet.");

    private static IReadOnlyList<CpuBackendCapability> MergeDirectCpuCapability(
        IReadOnlyList<CpuBackendCapability> capabilities,
        CpuBackendCapability? directCapability)
    {
        if (directCapability is null)
        {
            return capabilities;
        }

        var merged = capabilities
            .Where(item => item.Backend != directCapability.Backend)
            .ToList();
        merged.Add(directCapability);
        return merged;
    }

    private void SystemEvents_PowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (mainViewModel is null) return;
        var powerLineStatus = System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus;
        _ = UpdateChargePowerAvailabilityAsync(e.Mode != PowerModes.Suspend
            && powerLineStatus == System.Windows.Forms.PowerLineStatus.Online);
        var sourceChanged = powerLineStatus != lastPowerLineStatus;
        if (e.Mode is PowerModes.Suspend or PowerModes.Resume || powerLineStatus != lastPowerLineStatus)
            _ = experiments?.PauseAutomaticallyAsync("供电或睡眠状态发生变化，动态节能已暂停。");
        lastPowerLineStatus = powerLineStatus;
        if (e.Mode is PowerModes.Resume)
        {
            Dispatcher.BeginInvoke(() =>
                _ = mainViewModel.SynchronizeControlSessionAsync(CancellationToken.None));
            Dispatcher.BeginInvoke(() =>
                _ = mainViewModel.TryAutoConnectLiquidCoolingAsync(CancellationToken.None));
        }
        if (e.Mode is PowerModes.Resume || (e.Mode is PowerModes.StatusChange && sourceChanged))
        {
            Dispatcher.BeginInvoke(() =>
                _ = ApplyAutomaticRefreshRateForCurrentPowerSourceAsync(mainViewModel));
        }
    }

    private async Task UpdateChargePowerAvailabilityAsync(bool available)
    {
        if (isExiting || sessionCharge is null) return;
        try { await sessionCharge.SetPowerAvailableAsync(available, applicationLifetime.Token); }
        catch (OperationCanceledException) when (applicationLifetime.IsCancellationRequested) { }
        catch (Exception exception)
        {
            sessionCharge.RequestStop("供电状态处理未完成，停止限充并保留恢复记录。");
            RuntimeDiagnosticLog.Write("charge-power-session", $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    private async Task InitializePerformancePolicyWhenReadyAsync(MainWindowViewModel viewModel)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500));
                await viewModel.InitializePerformancePolicyAsync(CancellationToken.None);
                if (!viewModel.PerformanceReadbackText.Contains("暂未读取", StringComparison.Ordinal))
                {
                    return;
                }
            }
            catch (Exception)
            {
                // The GCU observer owns reconnection; this warm-up only populates initial UI values.
            }
        }
    }

    private async Task InitializePlatformModeWhenReadyAsync(MainWindowViewModel viewModel)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500));
                if (await viewModel.InitializePlatformModeAsync(CancellationToken.None)) return;
            }
            catch (Exception)
            {
                // The shared GCU observer owns reconnection; retry startup mode read briefly.
            }
        }
    }

    private static async Task SynchronizeScheduleWhenReadyAsync(MainWindowViewModel viewModel)
    {
        for (var attempt = 0; attempt < 12; attempt++)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(750));
                if (await viewModel.SynchronizeControlSessionAsync(CancellationToken.None)) return;
            }
            catch (Exception)
            {
                // The status source owns reconnects; retry only during the short startup window.
            }
        }
    }

    private static MachineIdentity ReadIdentity(IWmiQuery query)
    {
        try
        {
            return new WindowsMachineIdentityReader(query, reportQueryFailure: (name, exception) =>
                RuntimeDiagnosticLog.Write("identity-discovery", $"{name}: {exception.GetType().Name}")).Read();
        }
        catch (Exception)
        {
            return new("UNKNOWN", "设备识别暂不可用", string.Empty, "处理器识别暂不可用", string.Empty);
        }
    }

    private static IReadOnlyList<string> ReadDisplayAdapters(IWmiQuery query)
    {
        try
        {
            return new WindowsDisplayAdapterReader(query).ReadNames();
        }
        catch (Exception)
        {
            return [];
        }
    }
}
