using System.IO;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using RevoHelper.App.Controls;
using RevoHelper.App.Monitoring;
using RevoHelper.App.Lifecycle;
using RevoHelper.App.Settings;
using RevoHelper.App.SystemControl;
using RevoHelper.Core.Dashboard;
using RevoHelper.Core.Telemetry;
using RevoHelper.Core.Fans;
using RevoHelper.Core.Control;
using RevoHelper.Core.Hardware;
using RevoHelper.Hardware.Windows.OemStatus;

namespace RevoHelper.App.Presentation;

public sealed partial class MainWindowViewModel : INotifyPropertyChanged
{
    private string selectedProfileId = "balanced";
    private TurboStrategy selectedTurboStrategy = TurboStrategy.Quiet;
    private HomeDrawer activeDrawer;
    private ApplicationSettings applicationSettings;
    private readonly IApplicationSettingsStore settingsStore;
    private readonly IStartupRegistration startupRegistration;
    private readonly ICpuPolicyController? cpuPolicyController;
    private readonly IProcessorBoostController? boostController;
    private readonly IPlatformModeController? platformModeController;
    private readonly IGpuOutputModeController? gpuOutputModeController;
    private readonly IProcessorFrequencyLimitController? frequencyLimitController;
    private readonly IConflictServiceManager? conflictServiceManager;
    private readonly IQuickControlsController? quickControlsController;
    private readonly ILightingProfileStore lightingProfileStore;
    private readonly IDisplayController? displayController;
    private readonly SemaphoreSlim displayOperationGate = new(1, 1);
    private long displayRequestRevision;
    private readonly ICustomPerformanceController? customPerformanceController;
    private readonly IOemFanController? fanController;
    private readonly IFanBoostController? fanBoostController;
    private readonly IShutdownScheduler? shutdownScheduler;
    private readonly Func<bool> officialConsoleRunningProbe;
    private readonly Func<DateTimeOffset> utcNow;
    private readonly PerformanceScheduleController? scheduleController;
    private readonly Func<TimeSpan, CancellationToken, Task> verificationDelay;
    private readonly Dictionary<string, FanCurveTable> fanReadbacksByProfile = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FanCurveTable> fanDraftsByProfile = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CpuPowerLimitProfile> powerReadbacksByProfile = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CpuPowerLimitDraft> powerDraftsByProfile = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ProcessorBoostMode> boostDraftsByProfile = new(StringComparer.OrdinalIgnoreCase)
    {
        ["office:1"] = ProcessorBoostMode.EfficientEnabled,
        ["balanced:1"] = ProcessorBoostMode.Aggressive,
        ["turbo:Quiet"] = ProcessorBoostMode.Aggressive,
        ["turbo:HighEfficiency"] = ProcessorBoostMode.Aggressive,
        ["custom:1"] = ProcessorBoostMode.Aggressive,
        ["custom:2"] = ProcessorBoostMode.Aggressive,
        ["custom:3"] = ProcessorBoostMode.Aggressive
    };
    private readonly Dictionary<string, int> frequencyDraftsByProfile = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> frequencyConfiguredByProfile = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<LightingZone, LightingLocalTarget> lightingLocalTargets = [];
    private readonly Dictionary<LightingZone, LightingZoneConfiguration> lightingEditorDrafts = [];
    private readonly Dictionary<LightingZone, string> lightingZoneFeedback = [];
    private string statusMessage = "正在初始化设备状态…";
    private string telemetryStatusMessage = "正在初始化设备状态…";
    private bool hasOperationFeedback;
    private readonly Dictionary<(DeviceKind Kind, string Model), DateTimeOffset> deviceSampleTimes = [];
    private DateTimeOffset? oemCpuTemperatureAt;
    private static readonly TimeSpan TelemetryMaximumAge = TimeSpan.FromSeconds(4);
    private FanCurveTable? fanReadback;
    private FanCurveTable? fanDraft;
    private bool showingCpuFan = true;
    private bool isFanCurveValid;
    private bool isFanBusy;
    private bool isFanIndependentOutputEnabled = true;
    private bool isFanTransitionSensitivityEnabled = true;
    private int fanTransitionSensitivity = 18;
    private string fanStatusText = "等待读取官方风扇表";
    private double? oemCpuTemperatureC;
    private int selectedCustomProfileIndex = 1;
    private ProcessorBoostMode selectedProcessorBoostMode = ProcessorBoostMode.Aggressive;
    private string? livePerformanceReadbackText;
    private bool isApplyingPerformancePolicy;
    private bool isApplyingPlatformMode;
    private long platformRequestRevision;
    private PlatformModeSelection? confirmedPlatformSelection;
    private readonly SemaphoreSlim performanceTransitionGate = new(1, 1);
    private volatile bool isShuttingDown;
    private PlatformModeSelection? pendingFrequencyObservation;
    private long pendingFrequencyObservationRevision;
    private PlatformModeSelection? lastSynchronizedPlatformSelection;
    private long platformObservationRevision;
    private LocalizedUiCopy localizedUiCopy;
    private GpuOutputMode selectedGpuOutputMode = GpuOutputMode.Hybrid;
    private GpuOutputModeState? gpuOutputState;
    private bool isApplyingGpuOutputMode;
    private string gpuOutputStatusText = "等待读取当前显卡输出模式";
    private bool isProcessorFrequencyLimitEnabled;
    private int selectedMaximumProcessorFrequencyMhz = 3100;
    private string processorFrequencyLimitStatusText = "等待读取当前 Windows 频率上限";
    private RuntimeCapabilitySnapshot? runtimeCapabilities;
    private IReadOnlySet<PlatformMode>? confirmedDisplayModes;
    private bool isManagingConflictServices;
    private string conflictServiceOperationStatusText = "选择服务后可临时停止或恢复运行。";
    private bool isOfficialConsoleRunning;
    private bool isWinKeyEnabled;
    private bool isFnKeyEnabled;
    private bool isOsdEnabled;
    private bool isWifiEnabled;
    private bool isBluetoothEnabled;
    private bool isTouchpadEnabled;
    private bool isWebcamEnabled;
    private bool isCopilotKeyEnabled;
    private bool isKeyboardLightingEnabled;
    private readonly Dictionary<LightingZone, LightingZoneState> lightingZoneStates = [];
    private LightingProfile? importedLightingProfile;
    private LightingZone selectedLightingZone = LightingZone.Keyboard;
    private LightingEffect selectedLightingEffect = LightingEffect.OemDefault;
    private bool isSelectedLightingZoneEnabled = true;
    private string lightingColorHex = "#8000FF";
    private int lightingBrightness = 3;
    private int lightingSpeed = 2;
    private LightingDirection selectedLightingDirection = LightingDirection.Left;
    private int lightingSleepSeconds = 120;
    private bool isLightingBusy;
    private bool isReadingLighting;
    private LightingZoneConfiguration? pendingLightingConfiguration;
    private string lightingStatusText = "等待读取灯光设备能力。";
    private string keyboardControlsStatusText = "等待读取官方按键与背光状态。";
    private bool isLiquidCoolingConnected;
    private LiquidCoolingFanMode selectedLiquidCoolingFanMode = LiquidCoolingFanMode.Balanced;
    private LiquidCoolingPumpMode selectedLiquidCoolingPumpMode = LiquidCoolingPumpMode.Balanced;
    private string? liquidCoolingConnectionStatus;
    private int? liquidCoolingFanDutyPercent;
    private int? liquidCoolingPumpDutyPercent;
    private string? selectedLiquidCoolingDeviceId;
    private string? liquidCoolingFirmwareVersion;
    private bool liquidCoolingCanControl;
    private bool isLiquidCoolingPriming;
    private bool? liquidCoolingMeterNormal;
    private bool isLiquidCoolingBusy;
    private bool isRefreshingLiquidCoolingStatus;
    private bool isAutoConnectingLiquidCooling;
    private string liquidCoolingStatusText = "等待读取官方水冷状态。";
    private BatteryProtectionMode selectedBatteryProtectionMode = BatteryProtectionMode.Balanced;
    private int? batteryPercent;
    private bool isUsbChargingWhileOffEnabled;
    private bool supportsAcAutoBoot;
    private bool isAcAutoBootEnabled;
    private bool isHighPerformancePowerModeEnabled;
    private bool isDeepSleepEnabled;
    private int selectedDeepSleepMinutes = 30;
    private int batteryChargeLimitPercent = 80;
    private bool supportsBatteryChargeLimit;
    private string batteryStatusText = "等待读取官方电池保护状态。";
    private DisplayColorMode selectedDisplayColorMode = DisplayColorMode.Default;
    private string displayColorStatusText = "选择色彩模式后会调用官方校准接口。";
    private DisplaySettingsPage selectedDisplaySettingsPage;
    private bool isDisplayColorManagementEnabled;
    private bool supportsDisplayColorManagement;
    private bool isMiniLedLocalDimmingEnabled;
    private bool supportsMiniLedLocalDimming;
    private string displayName = "当前显示器";
    private string displayResolutionText = "等待读取";
    private int currentDisplayRefreshRateHz;
    private int selectedDisplayRefreshRateHz;
    private bool isDisplayBusy;
    private bool customOverclockingEnabled;
    private bool customCpuTemperatureWallEnabled;
    private int customCpuTemperatureWallC = 95;
    private int customCpuBurstPowerLimitWatts = 145;
    private bool customCpuAdvancedMenuEnabled;
    private int customGpuTgpWatts = 150;
    private bool customGpuDynamicBoostEnabled;
    private int customGpuDynamicBoostWatts = 5;
    private int customGpuCoreOffsetMhz;
    private int customGpuMemoryOffsetMhz;
    private bool isApplyingCustomPerformance;
    private string customPerformanceStatusText = "打开性能调整后读取当前 OEM 模式的高级参数。";
    private bool isFanBoostSupported;
    private bool isFanBoostEnabled;
    private bool isFanBoostBusy;
    private string fanBoostStatusText = "等待读取强冷状态。";
    private int selectedShutdownMinutes = 30;
    private bool isShutdownScheduled;
    private bool isShutdownBusy;
    private DateTimeOffset? shutdownDueAt;
    private string shutdownStatusText = "选择时长后由 Windows 设置关机提醒。";

    public MainWindowViewModel(HomeDashboard dashboard)
        : this(dashboard, new(), NullSettingsStore.Instance, NullStartupRegistration.Instance)
    {
    }

    public MainWindowViewModel(
        HomeDashboard dashboard,
        ApplicationSettings settings,
        IApplicationSettingsStore settingsStore,
        IStartupRegistration startupRegistration,
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
        ExperimentalControlsViewModel? experiments = null,
        IFanBoostController? fanBoostController = null,
        IShutdownScheduler? shutdownScheduler = null)
    {
        Experiments = experiments ?? new();
        Dashboard = dashboard;
        deviceSampleTimes.Clear();
        Devices = new ObservableCollection<DeviceStatus>(dashboard.Devices);
        FanPoints = [];
        ConflictServices = [];
        LiquidCoolingDevices = [];
        DisplayRefreshRates = [];
        LightingEffectOptions = [];
        var uiLanguageCode = LocalizedUiCopy.NormalizeLanguageCode(settings.UiLanguageCode);
        applicationSettings = settings with
        {
            WindowScalePercent = WindowScaleGeometry.NormalizePercent(settings.WindowScalePercent),
            UiLanguageCode = uiLanguageCode
        };
        localizedUiCopy = LocalizedUiCopy.For(uiLanguageCode);
        UiLanguageService.Current.SetLanguage(uiLanguageCode);
        Monitor.RefreshLanguage();
        this.settingsStore = settingsStore;
        this.startupRegistration = startupRegistration;
        this.cpuPolicyController = cpuPolicyController;
        this.boostController = boostController;
        this.platformModeController = platformModeController;
        this.gpuOutputModeController = gpuOutputModeController;
        this.frequencyLimitController = frequencyLimitController;
        this.conflictServiceManager = conflictServiceManager;
        this.quickControlsController = quickControlsController;
        // Runtime composition supplies the persistent store explicitly. Standalone view models
        // (including previews/tests) must not read or modify the signed-in user's lighting file.
        this.lightingProfileStore = lightingProfileStore ?? new MemoryLightingProfileStore();
        this.displayController = displayController;
        this.customPerformanceController = customPerformanceController;
        this.fanController = fanController;
        this.fanBoostController = fanBoostController;
        this.shutdownScheduler = shutdownScheduler;
        this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        liquidCoolingReconnect = new(this.utcNow);
        this.officialConsoleRunningProbe = officialConsoleRunningProbe ?? (() => false);
        this.verificationDelay = verificationDelay ?? Task.Delay;
        RestoreSavedLightingProfile();
        if (cpuPolicyController is not null && boostController is not null && platformModeController is not null)
        {
            scheduleController = new(
                platformModeController,
                cpuPolicyController,
                boostController,
                frequencyLimitController ?? new NullProcessorFrequencyLimitController());
        }
        LoadSavedPerformanceProfiles(settings.PerformanceProfiles);
        SwitchFrequencyDraftForCurrentProfile();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? CapabilityRecheckRequested;
    private bool isCapabilityDetectionBusy;

    public bool IsCapabilityDetectionBusy => isCapabilityDetectionBusy;
    public bool CanRecheckCapabilities => !isCapabilityDetectionBusy && !isShuttingDown;
    public string CapabilityRecheckLabel => isCapabilityDetectionBusy ? "正在检测…" : "重新检测";

    public void SetCapabilityDetectionBusy(bool busy)
    {
        if (isCapabilityDetectionBusy == busy) return;
        isCapabilityDetectionBusy = busy;
        OnPropertyChanged(nameof(IsCapabilityDetectionBusy));
        OnPropertyChanged(nameof(CanRecheckCapabilities));
        OnPropertyChanged(nameof(CapabilityRecheckLabel));
        NotifyCapabilityReasons();
    }

    public void RequestCapabilityRecheck()
    {
        if (!CanRecheckCapabilities || CapabilityRecheckRequested is null) return;
        SetCapabilityDetectionBusy(true);
        StatusMessage = "正在重新检测设备与官方控制组件…";
        CapabilityRecheckRequested.Invoke();
    }

    public HomeDashboard Dashboard { get; private set; }

    public IReadOnlyList<PerformanceProfile> Profiles => Dashboard.Profiles;

    public ObservableCollection<DeviceStatus> Devices { get; }

    public ObservableCollection<FanCurvePointViewModel> FanPoints { get; }

    public ObservableCollection<ConflictServiceItemViewModel> ConflictServices { get; }

    public ObservableCollection<int> DisplayRefreshRates { get; }

    public ObservableCollection<LightingEffectOption> LightingEffectOptions { get; }

    public IReadOnlyList<LightingProfile> LightingBuiltInProfiles => LightingProfileCatalog.BuiltIns;

    public IReadOnlyList<LightingDirection> LightingDirections { get; } = Enum.GetValues<LightingDirection>();

    public IReadOnlyList<LightingSleepOption> LightingSleepOptions { get; } =
    [
        new(0, "关闭"),
        new(10, "10 秒"),
        new(15, "15 秒"),
        new(20, "20 秒"),
        new(30, "30 秒"),
        new(45, "45 秒"),
        new(60, "1 分钟"),
        new(120, "2 分钟"),
        new(300, "5 分钟"),
        new(600, "10 分钟"),
        new(1200, "20 分钟"),
        new(1800, "30 分钟"),
        new(3600, "1 小时"),
        new(7200, "2 小时"),
        new(10800, "3 小时")
    ];

    public bool CanStopSelectedConflictServices =>
        conflictServiceManager is not null &&
        !isManagingConflictServices &&
        ConflictServices.Any(service => service.IsSelected && service.IsInstalled && service.IsRunning);

    public bool CanRestoreSelectedConflictServices =>
        conflictServiceManager is not null &&
        !isManagingConflictServices &&
        ConflictServices.Any(service => service.IsSelected && service.IsInstalled && !service.IsRunning);

    public string ConflictServiceOperationStatusText
    {
        get => conflictServiceOperationStatusText;
        private set
        {
            if (conflictServiceOperationStatusText == value) return;
            conflictServiceOperationStatusText = value;
            OnPropertyChanged();
        }
    }

    public bool IsOfficialConsoleRunning
    {
        get => isOfficialConsoleRunning;
        private set
        {
            if (isOfficialConsoleRunning == value) return;
            isOfficialConsoleRunning = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OfficialConsoleStatusText));
            OnPropertyChanged(nameof(OfficialConsoleInstructionText));
        }
    }

    public string OfficialConsoleStatusText => IsOfficialConsoleRunning ? "正在运行" : "未运行";
    public string OfficialConsoleInstructionText =>
        "RevoHelper可以与官方控制台共存，不会停用或禁用它们";

    public void UpdateConflictServices(IReadOnlyList<ConflictServiceStatus> statuses)
    {
        var selected = ConflictServices
            .Where(service => service.IsSelected)
            .Select(service => service.ServiceName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var service in ConflictServices)
        {
            service.PropertyChanged -= ConflictService_PropertyChanged;
        }
        ConflictServices.Clear();
        foreach (var status in statuses)
        {
            var service = new ConflictServiceItemViewModel(status)
            {
                IsSelected = selected.Contains(status.ServiceName)
            };
            service.PropertyChanged += ConflictService_PropertyChanged;
            ConflictServices.Add(service);
        }
        RefreshConflictServiceActionProperties();
    }

    public async Task RefreshConflictServicesAsync(CancellationToken cancellationToken)
    {
        try { IsOfficialConsoleRunning = officialConsoleRunningProbe(); }
        catch { IsOfficialConsoleRunning = false; }
        if (conflictServiceManager is null) return;
        try
        {
            UpdateConflictServices(await conflictServiceManager.DetectAsync(cancellationToken));
        }
        catch (Exception exception)
        {
            ConflictServiceOperationStatusText = $"服务检测失败：{exception.Message}";
        }
    }

    public Task StopSelectedConflictServicesAsync(CancellationToken cancellationToken) =>
        ManageSelectedConflictServicesAsync(restore: false, cancellationToken);

    public Task RestoreSelectedConflictServicesAsync(CancellationToken cancellationToken) =>
        ManageSelectedConflictServicesAsync(restore: true, cancellationToken);

    private async Task ManageSelectedConflictServicesAsync(bool restore, CancellationToken cancellationToken)
    {
        if (conflictServiceManager is null || isManagingConflictServices) return;
        var serviceNames = ConflictServices
            .Where(service => service.IsSelected && service.IsInstalled && service.IsRunning == !restore)
            .Select(service => service.ServiceName)
            .ToArray();
        if (serviceNames.Length == 0) return;

        isManagingConflictServices = true;
        RefreshConflictServiceActionProperties();
        try
        {
            if (restore)
            {
                await conflictServiceManager.RestoreAsync(serviceNames, cancellationToken);
            }
            else
            {
                await conflictServiceManager.StopAsync(serviceNames, cancellationToken);
            }
            await RefreshConflictServicesAsync(cancellationToken);
            ConflictServiceOperationStatusText = restore
                ? "所选服务已恢复运行，并已重新检测状态。"
                : "所选服务已停止，并已重新检测状态。";
        }
        catch (Exception exception)
        {
            ConflictServiceOperationStatusText = $"服务{(restore ? "恢复" : "停止")}失败：{exception.Message}";
        }
        finally
        {
            isManagingConflictServices = false;
            RefreshConflictServiceActionProperties();
        }
    }

    private void ConflictService_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConflictServiceItemViewModel.IsSelected))
        {
            RefreshConflictServiceActionProperties();
        }
    }

    private void RefreshConflictServiceActionProperties()
    {
        OnPropertyChanged(nameof(CanStopSelectedConflictServices));
        OnPropertyChanged(nameof(CanRestoreSelectedConflictServices));
    }

    public bool IsShowingCpuFan => showingCpuFan;

    public bool IsShowingGpuFan => !showingCpuFan;

    public bool IsFanCurveValid
    {
        get => isFanCurveValid;
        private set
        {
            if (isFanCurveValid == value) return;
            isFanCurveValid = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsFanApplyEnabled));
        }
    }

    public bool CanEditFanSettings =>
        IsCustomProfileSelected && SupportsPlatformMode(PlatformMode.Custom) && SupportsFeature(HardwareFeature.OemFanCurves) &&
        fanController is not null && fanDraft is not null && !isFanBusy && !isApplyingPlatformMode;

    public bool IsFanApplyEnabled => CanEditFanSettings && IsFanCurveValid;

    public string FanEditingAvailabilityText => IsCustomProfileSelected
        ? "调整后会自动应用；温度越高，风扇转速应越高。"
        : "官方的预设通常无法调整风扇曲线，所以仅作参考。";

    public string FanCurveModeHeading => IsCustomProfileSelected
        ? "自定义风扇曲线"
        : "风扇曲线参考";

    public double FanCurveSurfaceOpacity => IsCustomProfileSelected ? 1d : 0.55d;

    public bool IsFanIndependentOutputEnabled
    {
        get => isFanIndependentOutputEnabled;
        set
        {
            if (isFanIndependentOutputEnabled == value) return;
            isFanIndependentOutputEnabled = value;
            if (fanDraft is not null)
            {
                fanDraft = fanDraft with { IndependentCpuGpuControl = value };
                fanDraftsByProfile[CurrentFanCurveKey] = fanDraft;
            }
            OnPropertyChanged();
        }
    }

    public bool IsFanTransitionSensitivityEnabled
    {
        get => isFanTransitionSensitivityEnabled;
        set
        {
            if (isFanTransitionSensitivityEnabled == value) return;
            isFanTransitionSensitivityEnabled = value;
            OnPropertyChanged();
        }
    }

    public int FanTransitionSensitivity
    {
        get => fanTransitionSensitivity;
        set
        {
            var clamped = Math.Clamp(value,
                FanResponseSettings.MinimumTransitionDelayMilliseconds,
                FanResponseSettings.MaximumTransitionDelayMilliseconds);
            if (fanTransitionSensitivity == clamped) return;
            fanTransitionSensitivity = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FanTransitionSensitivityText));
        }
    }

    public string FanTransitionSensitivityText => $"{FanTransitionSensitivity} ms/0.5%";

    public string FanStatusText
    {
        get => fanStatusText;
        private set
        {
            if (fanStatusText == value) return;
            fanStatusText = value;
            OnPropertyChanged();
        }
    }

    public string MachineName => Dashboard.Identity.ProductName;

    public bool CanUseOfficeMode => SupportsPlatformMode(PlatformMode.Office);
    public bool ShowOfficeMode => IsModeVisible(PlatformMode.Office);
    public bool ShowBalancedMode => IsModeVisible(PlatformMode.Balanced);
    public bool ShowTurboMode => IsModeVisible(PlatformMode.TurboQuiet) || IsModeVisible(PlatformMode.TurboHigh);
    public bool ShowTurboStrategies => IsModeVisible(PlatformMode.TurboQuiet) && IsModeVisible(PlatformMode.TurboHigh);
    public string TurboModeDescription => ShowTurboStrategies ? UiCopy.TurboDescription
        : SelectedUiLanguageCode == "en-US" ? "OEM performance preset" : "固件性能预设";
    public bool ShowCustomMode => IsModeVisible(PlatformMode.Custom);
    public bool HasTwoPresetModes => confirmedDisplayModes is { Count: 2 } modes &&
        modes.Contains(PlatformMode.Office) && modes.Contains(PlatformMode.Balanced);
    public int PresetRowSpan => HasTwoPresetModes ? 2 : 1;
    public string OfficeModeTitle => HasTwoPresetModes
        ? SelectedUiLanguageCode == "en-US" ? "Balanced" : "平衡模式" : UiCopy.OfficeTitle;
    public string BalancedModeTitle => HasTwoPresetModes
        ? SelectedUiLanguageCode == "en-US" ? "Performance" : "性能模式" : UiCopy.BalancedTitle;
    public string OfficeModeDescription => HasTwoPresetModes
        ? SelectedUiLanguageCode == "en-US" ? "OEM balanced preset" : "固件平衡预设" : UiCopy.OfficeDescription;
    public string BalancedModeDescription => HasTwoPresetModes
        ? SelectedUiLanguageCode == "en-US" ? "OEM performance preset" : "固件性能预设" : UiCopy.BalancedDescription;

    private bool IsModeVisible(PlatformMode mode) => confirmedDisplayModes?.Contains(mode) ?? true;

    private void RefreshModePresentation()
    {
        foreach (var property in new[] { nameof(ShowOfficeMode), nameof(ShowBalancedMode),
            nameof(ShowTurboMode), nameof(ShowTurboStrategies), nameof(TurboModeDescription), nameof(ShowCustomMode), nameof(HasTwoPresetModes), nameof(PresetRowSpan),
            nameof(OfficeModeTitle), nameof(BalancedModeTitle), nameof(OfficeModeDescription),
            nameof(BalancedModeDescription), nameof(CurrentFanProfileLabel) }) OnPropertyChanged(property);
        NotifyCapabilityReasons();
    }
    public bool CanUseBalancedMode => SupportsPlatformMode(PlatformMode.Balanced);
    public bool CanUseTurboQuietMode => SupportsPlatformMode(PlatformMode.TurboQuiet);
    public bool CanUseTurboHighMode => SupportsPlatformMode(PlatformMode.TurboHigh);
    public bool CanUseCustomMode => SupportsPlatformMode(PlatformMode.Custom);
    public bool CanOpenFanAdjustment => SupportsFeature(HardwareFeature.OemFanCurves);
    public string OfficeModeTooltip => CanUseOfficeMode ? OfficeModeDescription : ModeAvailabilityReason();
    public string BalancedModeTooltip => CanUseBalancedMode ? BalancedModeDescription : ModeAvailabilityReason();
    public string TurboModeTooltip => CanUseTurboQuietMode ? TurboModeDescription : ModeAvailabilityReason();
    public string CustomModeTooltip => CanUseCustomMode ? UiCopy.CustomDescription : ModeAvailabilityReason();
    public string PlatformModeAvailabilityReason => CapabilityReason(HardwareFeature.OemPlatformModes);

    private string ModeAvailabilityReason() => IsCapabilityDetectionBusy
        ? "正在检测设备与档位，请稍候；无需重复点击。"
        : SupportsFeature(HardwareFeature.OemPlatformModes)
            ? "尚未收到此档位的可用证据；可以重新检测，暂不代表机型不支持。"
            : PlatformModeAvailabilityReason;

    private void NotifyCapabilityReasons()
    {
        OnPropertyChanged(nameof(OfficeModeTooltip));
        OnPropertyChanged(nameof(BalancedModeTooltip));
        OnPropertyChanged(nameof(TurboModeTooltip));
        OnPropertyChanged(nameof(CustomModeTooltip));
        OnPropertyChanged(nameof(PlatformModeAvailabilityReason));
        OnPropertyChanged(nameof(FanAvailabilityReason));
        OnPropertyChanged(nameof(GpuOutputAvailabilityReason));
        OnPropertyChanged(nameof(LightingAvailabilityReason));
        OnPropertyChanged(nameof(LiquidCoolingAvailabilityReason));
    }
    public bool CanOpenGpuOutput => gpuOutputModeController is not null;
    public bool CanOpenLighting => SupportsFeature(HardwareFeature.Lighting);
    public bool CanOpenLiquidCooling => SupportsFeature(HardwareFeature.LiquidCooling);
    public bool CanOpenPerformanceAdjustment =>
        SupportsFeature(HardwareFeature.WindowsProcessorPolicy) ||
        SupportsFeature(HardwareFeature.OemCpuPowerLimits) ||
        HasVerifiedDirectPowerBackend;
    public bool CanEditOemPowerLimits =>
        SelectedProfileId == "custom" &&
        SupportsFeature(HardwareFeature.OemCpuPowerLimits);
    public bool CanEditDirectPowerLimits =>
        SelectedProfileId != "custom" &&
        HasVerifiedDirectPowerBackend;
    public bool CanEditPowerLimits => CanEditOemPowerLimits || CanEditDirectPowerLimits;
    public string OemPowerLimitAvailabilityReason => CanEditOemPowerLimits
        ? "当前自定义模式可以调整功耗。"
        : CanEditDirectPowerLimits
            ? "当前模式可以调整 CPU 功耗，调整后会检查是否生效。"
        : SelectedProfileId == "custom"
            ? CapabilityReason(HardwareFeature.OemCpuPowerLimits)
            : "当前模式的功耗由官方预设管理，显示的数值仅供参考，不能调整。";
    public string FanAvailabilityReason => CapabilityReason(HardwareFeature.OemFanCurves);
    public string GpuOutputAvailabilityReason => CapabilityReason(HardwareFeature.GpuOutputModes);
    public string LightingAvailabilityReason => CapabilityReason(HardwareFeature.Lighting);
    public string LiquidCoolingAvailabilityReason => CapabilityReason(HardwareFeature.LiquidCooling);
    public string PerformanceAvailabilityReason => CanOpenPerformanceAdjustment
        ? "Windows 处理器策略或 OEM 功耗控制后端已确认可用。"
        : "尚未确认可用的处理器控制后端。";

    public void ApplyRuntimeCapabilities(RuntimeCapabilitySnapshot snapshot)
    {
        runtimeCapabilities = snapshot;
        // No evidence is not evidence of an unsupported mode. Keep the last confirmed layout
        // through transport outages; capability gates still control whether commands are allowed.
        if (snapshot.SupportedPlatformModes.Count > 0)
            confirmedDisplayModes = new HashSet<PlatformMode>(snapshot.SupportedPlatformModes);
        RefreshModePresentation();
        OnPropertyChanged(nameof(CanUseOfficeMode));
        OnPropertyChanged(nameof(CanUseBalancedMode));
        OnPropertyChanged(nameof(CanUseTurboQuietMode));
        OnPropertyChanged(nameof(CanUseTurboHighMode));
        OnPropertyChanged(nameof(CanUseCustomMode));
        OnPropertyChanged(nameof(CanOpenFanAdjustment));
        OnPropertyChanged(nameof(CanOpenGpuOutput));
        OnPropertyChanged(nameof(CanOpenLighting));
        OnPropertyChanged(nameof(CanOpenLiquidCooling));
        OnPropertyChanged(nameof(CanOpenPerformanceAdjustment));
        OnPropertyChanged(nameof(CanEditOemPowerLimits));
        OnPropertyChanged(nameof(CanEditDirectPowerLimits));
        OnPropertyChanged(nameof(CanEditPowerLimits));
        OnPropertyChanged(nameof(OemPowerLimitAvailabilityReason));
        OnPropertyChanged(nameof(FanAvailabilityReason));
        OnPropertyChanged(nameof(GpuOutputAvailabilityReason));
        OnPropertyChanged(nameof(LightingAvailabilityReason));
        OnPropertyChanged(nameof(LiquidCoolingAvailabilityReason));
        OnPropertyChanged(nameof(PerformanceAvailabilityReason));
        OnPropertyChanged(nameof(CanEditFanSettings));
        OnPropertyChanged(nameof(IsFanApplyEnabled));
        NotifyCapabilityReasons();
    }

    private bool SupportsFeature(HardwareFeature feature) =>
        runtimeCapabilities?.Supports(feature) ?? true;

    private bool HasVerifiedDirectPowerBackend =>
        runtimeCapabilities?.Supports(HardwareFeature.IntelRapl) == true ||
        runtimeCapabilities?.Supports(HardwareFeature.AmdSmu) == true;

    private bool CanEditPowerLimitsFor(PlatformModeSelection selection) =>
        selection.Mode == PlatformMode.Custom
            ? SupportsFeature(HardwareFeature.OemCpuPowerLimits)
            : HasVerifiedDirectPowerBackend;

    private bool SupportsPlatformMode(PlatformMode mode) =>
        runtimeCapabilities?.SupportedPlatformModes.Contains(mode) ?? true;

    private string CapabilityReason(HardwareFeature feature)
    {
        if (runtimeCapabilities is null ||
            !runtimeCapabilities.Features.TryGetValue(feature, out var capability))
        {
            return "正在探测当前设备是否支持此功能。";
        }

        if (capability.Availability == CapabilityAvailability.Available) return "已通过当前设备的运行时探测。";
        if (IsCapabilityDetectionBusy) return "正在检测设备与档位，请稍候；无需重复点击。";
        return capability.Diagnostic switch
        {
            CapabilityDiagnostic.IdentityUnconfirmed => "设备身份尚未识别成功；请重新检测，不代表机型不支持。",
            CapabilityDiagnostic.OtherManufacturer => "当前设备未识别为机械革命，暂不开放 OEM 控制。",
            CapabilityDiagnostic.ControlChainIncomplete => "官方控制组件未确认完整；请确认官方控制台及其服务可用后重新检测。",
            CapabilityDiagnostic.AwaitingEvidence when feature == HardwareFeature.OemPlatformModes =>
                "已识别官方控制组件，尚未收到可用档位；请确认服务连接后重新检测。",
            CapabilityDiagnostic.AwaitingEvidence => "控制链存在，但尚未收到此功能的运行时证据。",
            _ => string.IsNullOrWhiteSpace(capability.Reason) ? "当前设备未确认支持此功能。" : capability.Reason
        };
    }

    public void UpdateDashboard(HomeDashboard dashboard)
    {
        Dashboard = dashboard;
        Devices.Clear();
        foreach (var device in dashboard.Devices)
        {
            Devices.Add(device);
        }
        OnPropertyChanged(nameof(Dashboard));
        OnPropertyChanged(nameof(Profiles));
        OnPropertyChanged(nameof(MachineName));
    }

    public bool IsReadOnlyPreview => true;

    public bool IsPlatformSwitchPending => isApplyingPlatformMode;

    public string ReleaseVersionText => "V1.0.2";

    public string ReleaseBadgeText => "V1.0.2";

    public string AppVersionText => $"RevoHelper {ReleaseVersionText} · {UiCopy.OpenSourceAndFree}";

    public LocalizedUiCopy UiCopy => localizedUiCopy;

    public AnnouncementCopy Announcements => AnnouncementCopy.For(SelectedUiLanguageCode, ReleaseVersionText);

    public IReadOnlyList<UiLanguageOption> UiLanguageOptions { get; } =
    [
        new("zh-CN", "简体中文"),
        new("en-US", "English")
    ];

    public string SelectedUiLanguageCode
    {
        get => applicationSettings.UiLanguageCode;
        set
        {
            var normalized = LocalizedUiCopy.NormalizeLanguageCode(value);
            if (string.Equals(normalized, applicationSettings.UiLanguageCode, StringComparison.Ordinal)) return;
            if (!TrySaveSettings(applicationSettings with { UiLanguageCode = normalized })) return;

            localizedUiCopy = LocalizedUiCopy.For(normalized);
            UiLanguageService.Current.SetLanguage(normalized);
            Monitor.RefreshLanguage();
            OnPropertyChanged();
            OnPropertyChanged(nameof(UiCopy));
            OnPropertyChanged(nameof(Announcements));
            RefreshModePresentation();
            OnPropertyChanged(nameof(DrawerTitle));
            OnPropertyChanged(nameof(DrawerDescription));
            OnPropertyChanged(nameof(AppVersionText));
        }
    }

    public IReadOnlyList<UiLanguageOption> UiThemeOptions { get; } =
    [
        new("light", "浅色"),
        new("dark", "深色")
    ];

    public string SelectedUiThemeCode
    {
        get => AppTheme.Normalize(applicationSettings.UiThemeCode);
        set
        {
            var normalized = AppTheme.Normalize(value);
            if (normalized == SelectedUiThemeCode) return;
            if (!TrySaveSettings(applicationSettings with { UiThemeCode = normalized })) return;
            OnPropertyChanged();
        }
    }

    public bool IsWinKeyEnabled
    {
        get => isWinKeyEnabled;
        private set { if (isWinKeyEnabled == value) return; isWinKeyEnabled = value; OnPropertyChanged(); }
    }

    public bool IsFnKeyEnabled
    {
        get => isFnKeyEnabled;
        private set { if (isFnKeyEnabled == value) return; isFnKeyEnabled = value; OnPropertyChanged(); }
    }

    public bool IsOsdEnabled
    {
        get => isOsdEnabled;
        private set { if (isOsdEnabled == value) return; isOsdEnabled = value; OnPropertyChanged(); }
    }

    public bool IsWifiEnabled
    {
        get => isWifiEnabled;
        private set { if (isWifiEnabled == value) return; isWifiEnabled = value; OnPropertyChanged(); }
    }

    public bool IsBluetoothEnabled
    {
        get => isBluetoothEnabled;
        private set { if (isBluetoothEnabled == value) return; isBluetoothEnabled = value; OnPropertyChanged(); }
    }

    public bool IsTouchpadEnabled
    {
        get => isTouchpadEnabled;
        private set { if (isTouchpadEnabled == value) return; isTouchpadEnabled = value; OnPropertyChanged(); }
    }

    public bool IsWebcamEnabled
    {
        get => isWebcamEnabled;
        private set { if (isWebcamEnabled == value) return; isWebcamEnabled = value; OnPropertyChanged(); }
    }

    public bool IsCopilotKeyEnabled
    {
        get => isCopilotKeyEnabled;
        private set { if (isCopilotKeyEnabled == value) return; isCopilotKeyEnabled = value; OnPropertyChanged(); }
    }

    public bool SupportsWifiToggle { get; private set; }
    public bool SupportsBluetoothToggle { get; private set; }
    public bool SupportsTouchpadToggle { get; private set; }
    public bool SupportsWebcamToggle { get; private set; }
    public bool SupportsCopilotKeyToggle { get; private set; }

    public bool IsKeyboardLightingEnabled
    {
        get => isKeyboardLightingEnabled;
        private set { if (isKeyboardLightingEnabled == value) return; isKeyboardLightingEnabled = value; OnPropertyChanged(); }
    }

    public LightingZone SelectedLightingZone
    {
        get => selectedLightingZone;
        set
        {
            if (selectedLightingZone == value) return;
            selectedLightingZone = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedLightingZoneLabel));
            OnPropertyChanged(nameof(IsKeyboardLightingZoneSelected));
            OnPropertyChanged(nameof(CanSelectLightingDirection));
            OnPropertyChanged(nameof(CanEditLightingColor));
            OnPropertyChanged(nameof(IsWaterDynamicLightingEffectSelected));
            OnPropertyChanged(nameof(IsFirmwarePaletteLightingEffectSelected));
            OnPropertyChanged(nameof(IsOemDefaultLightingEffectSelected));
            LoadLightingEditor(value);
            LightingStatusText = lightingZoneFeedback.GetValueOrDefault(value) ??
                $"{LightingZoneLabel(value)}：调整后自动应用，仅控制当前分区。";
        }
    }

    public string SelectedLightingZoneLabel => LightingZoneLabel(SelectedLightingZone);

    public LightingEffect SelectedLightingEffect
    {
        get => selectedLightingEffect;
        set
        {
            if (selectedLightingEffect == value) return;
            selectedLightingEffect = value;
            RememberLightingEditorDraft();
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanSelectLightingDirection));
            OnPropertyChanged(nameof(CanEditLightingColor));
            OnPropertyChanged(nameof(IsWaterDynamicLightingEffectSelected));
            OnPropertyChanged(nameof(IsFirmwarePaletteLightingEffectSelected));
            OnPropertyChanged(nameof(IsOemDefaultLightingEffectSelected));
        }
    }

    public bool IsSelectedLightingZoneEnabled
    {
        get => isSelectedLightingZoneEnabled;
        set
        {
            if (isSelectedLightingZoneEnabled == value) return;
            isSelectedLightingZoneEnabled = value;
            RememberLightingEditorDraft();
            OnPropertyChanged();
            OnPropertyChanged(nameof(LightingEditorApplyHint));
        }
    }

    public string LightingEditorApplyHint => IsSelectedLightingZoneEnabled
        ? "调整后自动应用并回读"
        : "灯区已关闭：参数仅保存，打开后自动应用";

    public string LightingColorHex
    {
        get => lightingColorHex;
        set { if (lightingColorHex == value) return; lightingColorHex = value; RememberLightingEditorDraft(); OnPropertyChanged(); }
    }

    public int LightingBrightness
    {
        get => lightingBrightness;
        set { var clamped = Math.Clamp(value, 0, 4); if (lightingBrightness == clamped) return; lightingBrightness = clamped; RememberLightingEditorDraft(); OnPropertyChanged(); }
    }

    public int LightingSpeed
    {
        get => lightingSpeed;
        set { var clamped = Math.Clamp(value, 1, 4); if (lightingSpeed == clamped) return; lightingSpeed = clamped; RememberLightingEditorDraft(); OnPropertyChanged(); }
    }

    public LightingDirection SelectedLightingDirection
    {
        get => selectedLightingDirection;
        set { if (selectedLightingDirection == value) return; selectedLightingDirection = value; RememberLightingEditorDraft(); OnPropertyChanged(); }
    }

    public int LightingSleepSeconds
    {
        get => lightingSleepSeconds;
        set { if (lightingSleepSeconds == value) return; lightingSleepSeconds = value; OnPropertyChanged(); }
    }

    public bool IsLightingBusy
    {
        get => isLightingBusy;
        private set
        {
            if (isLightingBusy == value) return;
            isLightingBusy = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanApplyLighting));
            OnPropertyChanged(nameof(CanToggleSelectedLightingZonePower));
            OnPropertyChanged(nameof(CanApplyLightingSleepTimer));
        }
    }

    public bool CanApplyLighting => quickControlsController is not null && !IsLightingBusy &&
                                    lightingZoneStates.GetValueOrDefault(SelectedLightingZone)?.Available == true &&
                                    (SelectedLightingZone != LightingZone.Keyboard || !IsKeyboardLightingPowerOnly);

    public bool CanToggleSelectedLightingZonePower => quickControlsController is not null && !IsLightingBusy &&
                                    lightingZoneStates.GetValueOrDefault(SelectedLightingZone)?.Available == true;

    public bool CanApplyLightingSleepTimer => quickControlsController is not null && !IsLightingBusy &&
                                               (IsKeyboardLightingZoneAvailable && !IsKeyboardLightingPowerOnly || IsCoastLightingZoneAvailable);

    public bool IsSynchronizingLightingEditor { get; private set; }

    public bool IsKeyboardLightingZoneSelected => SelectedLightingZone == LightingZone.Keyboard;

    public bool CanSelectLightingDirection => IsKeyboardLightingZoneSelected && SelectedLightingEffect == LightingEffect.Wave;

    public bool IsWaterDynamicLightingEffectSelected =>
        SelectedLightingZone == LightingZone.LiquidCooler && SelectedLightingEffect != LightingEffect.Static;

    public bool IsOemDefaultLightingEffectSelected => SelectedLightingEffect == LightingEffect.OemDefault;

    public bool IsFirmwarePaletteLightingEffectSelected =>
        SelectedLightingZone != LightingZone.LiquidCooler &&
        !IsOemDefaultLightingEffectSelected &&
        !UsesUserLightingColor(SelectedLightingEffect);

    public bool CanEditLightingColor =>
        !IsOemDefaultLightingEffectSelected &&
        !IsWaterDynamicLightingEffectSelected &&
        !IsFirmwarePaletteLightingEffectSelected;

    public string LightingStatusText
    {
        get => lightingStatusText;
        private set
        {
            lightingZoneFeedback[SelectedLightingZone] = value;
            if (lightingStatusText == value) return;
            lightingStatusText = value;
            OnPropertyChanged();
        }
    }

    public bool IsKeyboardLightingZoneAvailable => IsLightingZoneAvailable(LightingZone.Keyboard);
    public bool IsCoastLightingZoneAvailable => IsLightingZoneAvailable(LightingZone.CoastLightbar);
    public bool IsLogoLightingZoneAvailable => IsLightingZoneAvailable(LightingZone.Logo);
    public bool IsWaterLightingZoneAvailable => IsLightingZoneAvailable(LightingZone.LiquidCooler);

    public string KeyboardControlsStatusText
    {
        get => keyboardControlsStatusText;
        private set { if (keyboardControlsStatusText == value) return; keyboardControlsStatusText = value; OnPropertyChanged(); }
    }

    public bool IsLiquidCoolingConnected
    {
        get => isLiquidCoolingConnected;
        private set { if (isLiquidCoolingConnected == value) return; isLiquidCoolingConnected = value; OnPropertyChanged(); }
    }

    public LiquidCoolingFanMode SelectedLiquidCoolingFanMode
    {
        get => selectedLiquidCoolingFanMode;
        set
        {
            if (selectedLiquidCoolingFanMode == value) return;
            selectedLiquidCoolingFanMode = value;
            SynchronizeLiquidCoolingManualReadback(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsLiquidCoolingQuieterStrategy));
            OnPropertyChanged(nameof(IsLiquidCoolingQuietStrategy));
            OnPropertyChanged(nameof(IsLiquidCoolingBalancedStrategy));
            OnPropertyChanged(nameof(IsLiquidCoolingMaximumStrategy));
            OnPropertyChanged(nameof(IsLiquidCoolingSmartStrategy));
        }
    }

    public LiquidCoolingPumpMode SelectedLiquidCoolingPumpMode
    {
        get => selectedLiquidCoolingPumpMode;
        set { if (selectedLiquidCoolingPumpMode == value) return; selectedLiquidCoolingPumpMode = value; OnPropertyChanged(); }
    }

    public string LiquidCoolingStatusText
    {
        get => liquidCoolingStatusText;
        private set { if (liquidCoolingStatusText == value) return; liquidCoolingStatusText = value; OnPropertyChanged(); }
    }

    public string LiquidCoolingFanStatusText => !IsLiquidCoolingConnected
        ? "未连接"
        : liquidCoolingFanDutyPercent is { } percent ? $"{percent}%" : "状态未知";

    public string LiquidCoolingPumpStatusText => !IsLiquidCoolingConnected
        ? "未连接"
        : liquidCoolingPumpDutyPercent switch
        {
            > 0 => "运行中",
            0 => "已停止",
            _ => "状态未知"
        };

    public string LiquidCoolingConnectionStatusText => IsLiquidCoolingConnected ? "已连接" : "未连接";

    public ObservableCollection<string> LiquidCoolingDevices { get; }

    public string? SelectedLiquidCoolingDeviceId
    {
        get => selectedLiquidCoolingDeviceId;
        set
        {
            if (string.Equals(selectedLiquidCoolingDeviceId, value, StringComparison.Ordinal)) return;
            if (!applyingLiquidCoolingState)
            {
                CancelLiquidCoolingRecovery();
                liquidCoolingSelectionDraft = true;
            }
            selectedLiquidCoolingDeviceId = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LiquidCoolingDeviceDisplayName));
            RefreshLiquidCoolingActions();
        }
    }

    public string LiquidCoolingDeviceDisplayName => SelectedLiquidCoolingDeviceId ?? "等待蓝牙设备扫描";

    public string LiquidCoolingDeviceDetailsText => string.IsNullOrWhiteSpace(liquidCoolingFirmwareVersion)
        ? "由机械革命 GCU 扫描并识别兼容蓝牙水冷"
        : $"固件：{liquidCoolingFirmwareVersion} · 仪表{(liquidCoolingMeterNormal == false ? "异常" : "正常")}";

    public bool IsLiquidCoolingBusy
    {
        get => isLiquidCoolingBusy;
        private set
        {
            if (isLiquidCoolingBusy == value) return;
            isLiquidCoolingBusy = value;
            OnPropertyChanged();
            RefreshLiquidCoolingActions();
        }
    }

    public bool IsLiquidCoolingPriming
    {
        get => isLiquidCoolingPriming;
        private set
        {
            if (isLiquidCoolingPriming == value) return;
            isLiquidCoolingPriming = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsLiquidCoolingPrimingNoticeVisible));
            OnPropertyChanged(nameof(LiquidCoolingPrimingButtonText));
            RefreshLiquidCoolingActions();
        }
    }

    public bool IsLiquidCoolingPrimingNoticeVisible => IsLiquidCoolingPriming;
    public string LiquidCoolingPrimingButtonText => IsLiquidCoolingPriming ? "注水进行中" : "注水模式";

    public bool CanScanLiquidCooling => !IsLiquidCoolingBusy && !IsLiquidCoolingPriming;
    public bool CanConnectLiquidCooling => !IsLiquidCoolingBusy && !IsLiquidCoolingConnected &&
        !IsLiquidCoolingPriming && liquidCoolingCanControl && !string.IsNullOrWhiteSpace(SelectedLiquidCoolingDeviceId);
    public bool CanDisconnectLiquidCooling => !IsLiquidCoolingBusy && !IsLiquidCoolingPriming && IsLiquidCoolingConnected && liquidCoolingCanControl;
    public bool CanPrimeLiquidCooling => !IsLiquidCoolingBusy && !IsLiquidCoolingPriming && IsLiquidCoolingConnected && liquidCoolingCanControl;
    public bool CanResetLiquidCooling => !IsLiquidCoolingBusy && !IsLiquidCoolingPriming && liquidCoolingCanControl;
    public bool CanControlLiquidCoolingStrategy => !IsLiquidCoolingBusy && !IsLiquidCoolingPriming && IsLiquidCoolingConnected;

    public bool IsLiquidCoolingAutoConnectEnabled
    {
        get => applicationSettings.AutoConnectLiquidCooling;
        set
        {
            if (value == applicationSettings.AutoConnectLiquidCooling) return;
            if (!TrySaveSettings(applicationSettings with { AutoConnectLiquidCooling = value })) return;
            if (!value) CancelLiquidCoolingRecovery();
            else liquidCoolingStartupAutoConnectSuppressed = false;

            OnPropertyChanged();
            LiquidCoolingStatusText = value
                ? "已启用水冷自动连接；登录与睡眠唤醒后会尝试连接上次设备。"
                : "已关闭水冷自动连接；仍可在本页手动连接。";
        }
    }

    public bool IsLiquidCoolingQuieterStrategy => SelectedLiquidCoolingFanMode == LiquidCoolingFanMode.Quieter;
    public bool IsLiquidCoolingQuietStrategy => SelectedLiquidCoolingFanMode == LiquidCoolingFanMode.Quiet;
    public bool IsLiquidCoolingBalancedStrategy => SelectedLiquidCoolingFanMode == LiquidCoolingFanMode.Balanced;
    public bool IsLiquidCoolingMaximumStrategy => SelectedLiquidCoolingFanMode == LiquidCoolingFanMode.Maximum;
    public bool IsLiquidCoolingSmartStrategy => SelectedLiquidCoolingFanMode == LiquidCoolingFanMode.Auto;

    public BatteryProtectionMode SelectedBatteryProtectionMode
    {
        get => selectedBatteryProtectionMode;
        set
        {
            if (selectedBatteryProtectionMode == value) return;
            selectedBatteryProtectionMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsBatteryLongLifeMode));
            OnPropertyChanged(nameof(IsBatteryBalancedMode));
            OnPropertyChanged(nameof(IsBatteryWorkstationMode));
        }
    }

    public bool IsBatteryLongLifeMode => SelectedBatteryProtectionMode == BatteryProtectionMode.Performance;
    public bool IsBatteryBalancedMode => SelectedBatteryProtectionMode == BatteryProtectionMode.Balanced;
    public bool IsBatteryWorkstationMode => SelectedBatteryProtectionMode == BatteryProtectionMode.Healthy;

    public int? BatteryPercent
    {
        get => batteryPercent;
        private set { if (batteryPercent == value) return; batteryPercent = value; OnPropertyChanged(); }
    }

    public string BatteryStatusText
    {
        get => batteryStatusText;
        private set { if (batteryStatusText == value) return; batteryStatusText = value; OnPropertyChanged(); }
    }

    public bool IsUsbChargingWhileOffEnabled
    {
        get => isUsbChargingWhileOffEnabled;
        private set { if (isUsbChargingWhileOffEnabled == value) return; isUsbChargingWhileOffEnabled = value; OnPropertyChanged(); }
    }

    public bool SupportsAcAutoBoot
    {
        get => supportsAcAutoBoot;
        private set { if (supportsAcAutoBoot == value) return; supportsAcAutoBoot = value; OnPropertyChanged(); }
    }

    public bool IsAcAutoBootEnabled
    {
        get => isAcAutoBootEnabled;
        private set { if (isAcAutoBootEnabled == value) return; isAcAutoBootEnabled = value; OnPropertyChanged(); }
    }

    public bool IsHighPerformancePowerModeEnabled
    {
        get => isHighPerformancePowerModeEnabled;
        private set { if (isHighPerformancePowerModeEnabled == value) return; isHighPerformancePowerModeEnabled = value; OnPropertyChanged(); }
    }

    public bool IsDeepSleepEnabled
    {
        get => isDeepSleepEnabled;
        private set { if (isDeepSleepEnabled == value) return; isDeepSleepEnabled = value; OnPropertyChanged(); }
    }

    public IReadOnlyList<int> DeepSleepMinuteOptions { get; } = [15, 30, 60, 120];

    public int SelectedDeepSleepMinutes
    {
        get => selectedDeepSleepMinutes;
        set { if (selectedDeepSleepMinutes == value) return; selectedDeepSleepMinutes = value; OnPropertyChanged(); }
    }

    public int BatteryChargeLimitPercent
    {
        get => batteryChargeLimitPercent;
        set { if (batteryChargeLimitPercent == value) return; batteryChargeLimitPercent = value; OnPropertyChanged(); }
    }

    public bool SupportsBatteryChargeLimit
    {
        get => supportsBatteryChargeLimit;
        private set { if (supportsBatteryChargeLimit == value) return; supportsBatteryChargeLimit = value; OnPropertyChanged(); }
    }

    public bool SupportsUsbChargingWhileOff { get; private set; }
    public bool SupportsHighPerformancePowerMode { get; private set; }
    public bool SupportsDeepSleep { get; private set; }

    public DisplayColorMode SelectedDisplayColorMode
    {
        get => selectedDisplayColorMode;
        set
        {
            if (selectedDisplayColorMode == value) return;
            selectedDisplayColorMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDisplayColorDefault));
            OnPropertyChanged(nameof(IsDisplayColorSrgb));
            OnPropertyChanged(nameof(IsDisplayColorP3));
            OnPropertyChanged(nameof(IsDisplayColorAdobeRgb));
        }
    }

    public bool IsDisplayColorDefault => SelectedDisplayColorMode is DisplayColorMode.Default;
    public bool IsDisplayColorSrgb => SelectedDisplayColorMode is DisplayColorMode.Srgb;
    public bool IsDisplayColorP3 => SelectedDisplayColorMode is DisplayColorMode.P3;
    public bool IsDisplayColorAdobeRgb => SelectedDisplayColorMode is DisplayColorMode.AdobeRgb;

    public string DisplayColorStatusText
    {
        get => displayColorStatusText;
        private set { if (displayColorStatusText == value) return; displayColorStatusText = value; OnPropertyChanged(); }
    }

    public DisplaySettingsPage SelectedDisplaySettingsPage
    {
        get => selectedDisplaySettingsPage;
        private set
        {
            if (selectedDisplaySettingsPage == value) return;
            selectedDisplaySettingsPage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDisplayColorCalibrationPage));
            OnPropertyChanged(nameof(IsDisplayFeaturePage));
            OnPropertyChanged(nameof(IsDisplayRefreshRatePage));
        }
    }

    public bool IsDisplayColorCalibrationPage => SelectedDisplaySettingsPage is DisplaySettingsPage.ColorCalibration;
    public bool IsDisplayFeaturePage => SelectedDisplaySettingsPage is DisplaySettingsPage.DisplayFeatures;
    public bool IsDisplayRefreshRatePage => SelectedDisplaySettingsPage is DisplaySettingsPage.RefreshRate;

    public bool IsDisplayColorManagementEnabled
    {
        get => isDisplayColorManagementEnabled;
        private set { if (isDisplayColorManagementEnabled == value) return; isDisplayColorManagementEnabled = value; OnPropertyChanged(); }
    }

    public bool SupportsDisplayColorManagement
    {
        get => supportsDisplayColorManagement;
        private set
        {
            if (supportsDisplayColorManagement == value) return;
            supportsDisplayColorManagement = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplaySettingsTabCount));
        }
    }

    public bool IsMiniLedLocalDimmingEnabled
    {
        get => isMiniLedLocalDimmingEnabled;
        private set { if (isMiniLedLocalDimmingEnabled == value) return; isMiniLedLocalDimmingEnabled = value; OnPropertyChanged(); }
    }

    public bool SupportsMiniLedLocalDimming
    {
        get => supportsMiniLedLocalDimming;
        private set
        {
            if (supportsMiniLedLocalDimming == value) return;
            supportsMiniLedLocalDimming = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplaySettingsTabCount));
        }
    }

    public bool SupportsDisplayRefreshRate => DisplayRefreshRates.Count > 0;

    public int DisplaySettingsTabCount => Math.Max(1,
        (SupportsDisplayColorManagement ? 1 : 0) +
        (SupportsMiniLedLocalDimming ? 1 : 0) +
        (SupportsDisplayRefreshRate ? 1 : 0));

    public string DisplayName
    {
        get => displayName;
        private set { if (displayName == value) return; displayName = value; OnPropertyChanged(); }
    }

    public string DisplayResolutionText
    {
        get => displayResolutionText;
        private set { if (displayResolutionText == value) return; displayResolutionText = value; OnPropertyChanged(); }
    }

    public int CurrentDisplayRefreshRateHz
    {
        get => currentDisplayRefreshRateHz;
        private set
        {
            if (currentDisplayRefreshRateHz == value) return;
            currentDisplayRefreshRateHz = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentDisplayRefreshRateText));
        }
    }

    public string CurrentDisplayRefreshRateText => CurrentDisplayRefreshRateHz > 0
        ? $"{CurrentDisplayRefreshRateHz} Hz"
        : "状态未知";

    public int SelectedDisplayRefreshRateHz
    {
        get => selectedDisplayRefreshRateHz;
        set
        {
            if (selectedDisplayRefreshRateHz == value) return;
            selectedDisplayRefreshRateHz = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanApplyDisplayRefreshRate));
        }
    }

    public bool IsDisplayBusy
    {
        get => isDisplayBusy;
        private set
        {
            if (isDisplayBusy == value) return;
            isDisplayBusy = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanApplyDisplayRefreshRate));
        }
    }

    public bool CanApplyDisplayRefreshRate => !IsDisplayBusy && displayController is not null &&
        DisplayRefreshRates.Contains(SelectedDisplayRefreshRateHz);

    public bool IsAutomaticRefreshRateEnabled => applicationSettings.AutomaticRefreshRate;

    public int PreferredAcRefreshRateHz => applicationSettings.PreferredAcRefreshRateHz;

    public bool StartWithWindows
    {
        get => applicationSettings.StartWithWindows;
        set
        {
            if (value == applicationSettings.StartWithWindows)
            {
                return;
            }

            try
            {
                startupRegistration.SetEnabled(value);
                var confirmed = startupRegistration.IsEnabled();
                if (confirmed != value)
                {
                    StatusMessage = "启动项写入后未确认目标状态 · 未修改界面设置";
                    return;
                }
                if (TrySaveSettings(applicationSettings with { StartWithWindows = value }))
                {
                    StatusMessage = value ? "已启用随 Windows 启动" : "已关闭随 Windows 启动";
                    OnPropertyChanged();
                }
                else
                {
                    try
                    {
                        startupRegistration.SetEnabled(!value);
                        StatusMessage = "启动项设置失败 · 未修改现有状态";
                    }
                    catch (Exception)
                    {
                        applicationSettings = applicationSettings with { StartWithWindows = value };
                        OnPropertyChanged();
                        StatusMessage = "启动项已修改 · 本地设置保存及回滚失败";
                    }
                }
            }
            catch (Exception)
            {
                StatusMessage = "启动项设置失败 · 未修改现有状态";
            }
        }
    }

    public bool AlwaysOnTop
    {
        get => applicationSettings.AlwaysOnTop;
        set
        {
            if (value == applicationSettings.AlwaysOnTop)
            {
                return;
            }

            if (TrySaveSettings(applicationSettings with { AlwaysOnTop = value }))
            {
                OnPropertyChanged();
            }
        }
    }

    public bool RememberWindowPosition
    {
        get => applicationSettings.RememberWindowPosition;
        set
        {
            if (value == applicationSettings.RememberWindowPosition)
            {
                return;
            }

            if (TrySaveSettings(applicationSettings with { RememberWindowPosition = value }))
            {
                OnPropertyChanged();
            }
        }
    }

    public IReadOnlyList<int> WindowScaleOptions => WindowScaleGeometry.Options;

    public int WindowScalePercent
    {
        get => applicationSettings.WindowScalePercent;
        set
        {
            if (!WindowScaleOptions.Contains(value) || value == WindowScalePercent) return;
            if (TrySaveSettings(applicationSettings with { WindowScalePercent = value }))
                OnPropertyChanged();
        }
    }

    public double? SavedWindowLeft => applicationSettings.WindowLeft;

    public double? SavedWindowTop => applicationSettings.WindowTop;

    public HomeDrawer ActiveDrawer
    {
        get => activeDrawer;
        private set
        {
            if (activeDrawer == value)
            {
                return;
            }

            activeDrawer = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDrawerOpen));
            OnPropertyChanged(nameof(DrawerTitle));
            OnPropertyChanged(nameof(DrawerIcon));
            OnPropertyChanged(nameof(DrawerDescription));
        }
    }

    public bool IsDrawerOpen => ActiveDrawer is not HomeDrawer.None;

    public ExperimentalControlsViewModel Experiments { get; }

    public PerformanceMonitorPanelViewModel Monitor { get; } = new();

    public void OpenPerformanceMonitor()
    {
        ActiveDrawer = HomeDrawer.PerformanceMonitor;
        StatusMessage = "性能监控设置已展开";
    }

    private async Task<bool> PauseDynamicForManualControlAsync()
    {
        if (await Experiments.PauseForManualControlAsync()) return true;
        StatusMessage = ManualControlBlockedMessage;
        return false;
    }

    private string ManualControlBlockedMessage => Experiments.Charge.RecoveryPending
        ? "限充恢复未完成，请在电源设置中恢复原值后再调整。"
        : Experiments.Charge.IsBusy
            ? "限充正在切换或恢复，请等待完成后再调整。"
            : "动态频率恢复未完成，请在更多方案中重试恢复后再调整。";

    public void OpenMoreProfiles() => ActiveDrawer = HomeDrawer.MoreProfiles;

    public void OpenAnnouncements() => ActiveDrawer = HomeDrawer.Announcements;

    public IReadOnlyList<int> ShutdownMinuteOptions { get; } = [5, 15, 30, 60, 120];

    public int SelectedShutdownMinutes
    {
        get => selectedShutdownMinutes;
        set
        {
            if (!ShutdownMinuteOptions.Contains(value) || selectedShutdownMinutes == value) return;
            selectedShutdownMinutes = value;
            OnPropertyChanged();
        }
    }

    public bool IsShutdownScheduled
    {
        get => isShutdownScheduled;
        private set
        {
            if (isShutdownScheduled == value) return;
            isShutdownScheduled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanScheduleShutdown));
            OnPropertyChanged(nameof(CanCancelShutdown));
        }
    }

    public bool CanScheduleShutdown => shutdownScheduler is not null && !IsShutdownScheduled && !isShutdownBusy;
    public bool CanCancelShutdown => shutdownScheduler is not null && !isShutdownBusy;
    public DateTimeOffset? ShutdownDueAt => shutdownDueAt;
    public string ShutdownDueTimeText => shutdownDueAt?.ToLocalTime().ToString("HH:mm") ?? "--:--";
    public string ShutdownStatusText
    {
        get => shutdownStatusText;
        private set { if (shutdownStatusText == value) return; shutdownStatusText = value; OnPropertyChanged(); }
    }

    public string ShutdownCountdownText
    {
        get
        {
            if (!IsShutdownScheduled || shutdownDueAt is null) return "未设置";
            var seconds = Math.Max(0, (int)Math.Ceiling((shutdownDueAt.Value - utcNow()).TotalSeconds));
            if (seconds == 0) return "即将关机";
            var duration = TimeSpan.FromSeconds(seconds);
            return duration.TotalHours >= 1
                ? $"{(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}"
                : $"{duration.Minutes:00}:{duration.Seconds:00}";
        }
    }

    public bool IsFanBoostSupported
    {
        get => isFanBoostSupported;
        private set { if (isFanBoostSupported == value) return; isFanBoostSupported = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanToggleFanBoost)); }
    }

    public bool IsFanBoostEnabled
    {
        get => isFanBoostEnabled;
        private set { if (isFanBoostEnabled == value) return; isFanBoostEnabled = value; OnPropertyChanged(); }
    }

    public bool CanToggleFanBoost => fanBoostController is not null && IsFanBoostSupported && !isFanBoostBusy;
    public string FanBoostStatusText
    {
        get => fanBoostStatusText;
        private set { if (fanBoostStatusText == value) return; fanBoostStatusText = value; OnPropertyChanged(); }
    }

    public async Task OpenOtherAsync(CancellationToken cancellationToken)
    {
        ActiveDrawer = HomeDrawer.Other;
        if (fanBoostController is null)
        {
            FanBoostStatusText = "当前控制链未提供强冷接口。";
            return;
        }

        await RefreshFanBoostAsync(cancellationToken);
    }

    public async Task SetFanBoostAsync(bool enabled, CancellationToken cancellationToken)
    {
        if (fanBoostController is null || isFanBoostBusy) return;
        isFanBoostBusy = true;
        OnPropertyChanged(nameof(CanToggleFanBoost));
        try
        {
            var result = await fanBoostController.SetAsync(enabled, cancellationToken);
            FanBoostStatusText = result.Reason;
            await RefreshFanBoostAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            FanBoostStatusText = $"强冷切换失败：{exception.Message}";
            await RefreshFanBoostAsync(cancellationToken);
        }
        finally
        {
            isFanBoostBusy = false;
            OnPropertyChanged(nameof(CanToggleFanBoost));
        }
    }

    public async Task ScheduleShutdownAsync(CancellationToken cancellationToken)
    {
        if (shutdownScheduler is null || isShutdownBusy) return;
        isShutdownBusy = true;
        RefreshShutdownCommands();
        try
        {
            var result = await shutdownScheduler.ScheduleAsync(SelectedShutdownMinutes, cancellationToken);
            ShutdownStatusText = result.Message;
            if (result.Succeeded)
            {
                shutdownDueAt = result.DueAt;
                IsShutdownScheduled = result.IsScheduled;
                OnPropertyChanged(nameof(ShutdownDueAt));
                OnPropertyChanged(nameof(ShutdownDueTimeText));
                OnPropertyChanged(nameof(ShutdownCountdownText));
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ShutdownStatusText = $"设置定时关机失败：{exception.Message}";
        }
        finally { isShutdownBusy = false; RefreshShutdownCommands(); }
    }

    public async Task CancelShutdownAsync(CancellationToken cancellationToken)
    {
        if (shutdownScheduler is null || isShutdownBusy) return;
        isShutdownBusy = true;
        RefreshShutdownCommands();
        try
        {
            var result = await shutdownScheduler.CancelAsync(cancellationToken);
            ShutdownStatusText = result.Message;
            if (result.Succeeded)
            {
                shutdownDueAt = null;
                IsShutdownScheduled = false;
                OnPropertyChanged(nameof(ShutdownDueAt));
                OnPropertyChanged(nameof(ShutdownDueTimeText));
                OnPropertyChanged(nameof(ShutdownCountdownText));
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ShutdownStatusText = $"取消定时关机失败：{exception.Message}";
        }
        finally { isShutdownBusy = false; RefreshShutdownCommands(); }
    }

    public void RefreshShutdownCountdown() => OnPropertyChanged(nameof(ShutdownCountdownText));

    private async Task RefreshFanBoostAsync(CancellationToken cancellationToken)
    {
        if (fanBoostController is null) return;
        try
        {
            var state = await fanBoostController.ReadAsync(cancellationToken);
            IsFanBoostSupported = state.Supported;
            IsFanBoostEnabled = state.Enabled;
            FanBoostStatusText = state.Supported
                ? state.Enabled ? "强冷运行中，风扇保持最大转速。" : "强冷已关闭，风扇由当前 OEM 档位管理。"
                : "当前机型未返回强冷支持状态。";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            IsFanBoostSupported = false;
            FanBoostStatusText = $"强冷状态读取失败：{exception.Message}";
        }
    }

    private void RefreshShutdownCommands()
    {
        OnPropertyChanged(nameof(CanScheduleShutdown));
        OnPropertyChanged(nameof(CanCancelShutdown));
    }

    public string DrawerTitle => ActiveDrawer switch
    {
        HomeDrawer.PerformanceMonitor => UiCopy.MonitorTitle,
        HomeDrawer.Performance => UiCopy.PerformanceAdjustmentTitle,
        HomeDrawer.Fan => UiCopy.FanAdjustmentTitle,
        HomeDrawer.GpuOutput => UiCopy.GpuOutputTitle,
        HomeDrawer.Lighting => UiCopy.LightingTitle,
        HomeDrawer.LiquidCooling => UiCopy.LiquidCoolingTitle,
        HomeDrawer.Keyboard => UiCopy.KeyboardTitle,
        HomeDrawer.Battery => UiCopy.BatteryTitle,
        HomeDrawer.DisplayColor => UiCopy.DisplayTitle,
        HomeDrawer.Other => UiCopy.OtherTitle,
        HomeDrawer.MoreProfiles => UiCopy.MoreProfilesTitle,
        HomeDrawer.Announcements => Announcements.Title,
        _ => UiCopy.SettingsTitle
    };

    public string DrawerIcon => ActiveDrawer switch
    {
        HomeDrawer.PerformanceMonitor => "▥",
        HomeDrawer.Performance => "⚡",
        HomeDrawer.Fan => "❋",
        HomeDrawer.GpuOutput => "▣",
        HomeDrawer.Lighting => "✦",
        HomeDrawer.LiquidCooling => "●",
        HomeDrawer.Keyboard => "⌨",
        HomeDrawer.Battery => "▤",
        HomeDrawer.DisplayColor => "▤",
        HomeDrawer.Other => "•••",
        _ => "⚙"
    };

    public string DrawerDescription => ActiveDrawer switch
    {
        HomeDrawer.PerformanceMonitor => "设置独立监控小窗的位置和显示内容。",
        HomeDrawer.Performance => UiCopy.PerformanceDrawerDescription,
        HomeDrawer.Fan => UiCopy.FanDrawerDescription,
        HomeDrawer.GpuOutput => UiCopy.GpuOutputDrawerDescription,
        HomeDrawer.Lighting => UiCopy.LightingDrawerDescription,
        HomeDrawer.LiquidCooling => UiCopy.LiquidCoolingDrawerDescription,
        HomeDrawer.Keyboard => UiCopy.KeyboardDrawerDescription,
        HomeDrawer.Battery => UiCopy.BatteryDrawerDescription,
        HomeDrawer.DisplayColor => UiCopy.DisplayDrawerDescription,
        HomeDrawer.Other => UiCopy.OtherDrawerDescription,
        HomeDrawer.Announcements => Announcements.EntryDescription,
        _ => UiCopy.SettingsDrawerDescription
    };

    public GpuOutputMode SelectedGpuOutputMode
    {
        get => selectedGpuOutputMode;
        set
        {
            if (selectedGpuOutputMode == value) return;
            selectedGpuOutputMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsGpuOutputApplyEnabled));
        }
    }

    public bool SupportsHybridGpuMode => gpuOutputState?.Capabilities.SupportsHybrid == true;
    public bool SupportsDiscreteGpuMode => gpuOutputState?.Capabilities.SupportsDiscrete == true;
    public bool SupportsIntegratedGpuMode => gpuOutputState?.Capabilities.SupportsIntegrated == true;
    public bool IsGpuOutputApplyEnabled => gpuOutputModeController is not null && gpuOutputState is not null &&
        gpuOutputState.Capabilities.Supports(SelectedGpuOutputMode) &&
        gpuOutputState.Mode != SelectedGpuOutputMode && !isApplyingGpuOutputMode;

    public string GpuOutputStatusText
    {
        get => gpuOutputStatusText;
        private set
        {
            if (gpuOutputStatusText == value) return;
            gpuOutputStatusText = value;
            OnPropertyChanged();
        }
    }

    public TurboStrategy SelectedTurboStrategy
    {
        get => selectedTurboStrategy;
        private set
        {
            if (selectedTurboStrategy == value)
            {
                return;
            }

            selectedTurboStrategy = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Tradeoff));
            OnPropertyChanged(nameof(CurrentFanProfileLabel));
            RefreshPerformanceProperties();
        }
    }

    public PerformanceTradeoff Tradeoff =>
        PerformanceTradeoffCatalog.Get(SelectedProfileId, SelectedTurboStrategy);

    public bool IsTradeoffVisible => SelectedProfileId != "custom";

    public string CurrentFanProfileLabel => SelectedProfileId switch
    {
        "office" => $"当前：{OfficeModeTitle}",
        "balanced" => $"当前：{BalancedModeTitle}",
        "turbo" when !ShowTurboStrategies => "当前：狂暴模式",
        "turbo" => SelectedTurboStrategy is TurboStrategy.HighEfficiency
            ? "当前：狂暴模式 · 高能"
            : "当前：狂暴模式 · 静音",
        "custom" => "当前：自定义模式",
        _ => "当前：未知模式"
    };

    public string FanAccentColor => SelectedProfileId switch
    {
        "office" => "#22A967",
        "balanced" => "#2185F4",
        "turbo" => "#9339DB",
        "custom" => "#D58A00",
        _ => "#718095"
    };

    public string FanAccentSurfaceColor => SelectedProfileId switch
    {
        "office" => "#DDF8E8",
        "balanced" => "#DCEEFF",
        "turbo" => "#EEDCFF",
        "custom" => "#FFF0D8",
        _ => "#F1F4F9"
    };

    public string PerformanceAccentColor => FanAccentColor;

    public string PerformanceAccentSurfaceColor => FanAccentSurfaceColor;

    public int SelectedCustomProfileIndex => selectedCustomProfileIndex;
    public PlatformModeSelection ActivePlatformSelection => CurrentPlatformSelection();
    public PlatformModeSelection? ConfirmedPlatformSelection => confirmedPlatformSelection;

    public int SelectedPl1Watts
    {
        get => GetCurrentPowerDraft().Pl1Watts;
        set
        {
            var draft = GetCurrentPowerDraft();
            var clamped = Math.Clamp(value, 5, draft.Pl2Watts);
            if (draft.Pl1Watts == clamped) return;
            draft.Pl1Watts = clamped;
            RefreshPerformanceProperties();
        }
    }

    public int SelectedPl2Watts
    {
        get => GetCurrentPowerDraft().Pl2Watts;
        set
        {
            var draft = GetCurrentPowerDraft();
            var clamped = Math.Clamp(value, draft.Pl1Watts, 250);
            if (draft.Pl2Watts == clamped) return;
            draft.Pl2Watts = clamped;
            RefreshPerformanceProperties();
        }
    }

    public bool IsPerformanceDraftValid =>
        SelectedPl1Watts is >= 5 and <= 250 &&
        SelectedPl2Watts is >= 5 and <= 250 &&
        SelectedPl1Watts <= SelectedPl2Watts;

    public IReadOnlyList<ProcessorBoostModeOption> ProcessorBoostModes { get; } =
    [
        new(ProcessorBoostMode.Disabled, "Disabled"),
        new(ProcessorBoostMode.Enabled, "Enabled"),
        new(ProcessorBoostMode.Aggressive, "Aggressive"),
        new(ProcessorBoostMode.EfficientEnabled, "Efficient Enabled"),
        new(ProcessorBoostMode.EfficientAggressive, "Efficient Aggressive"),
        new(ProcessorBoostMode.AggressiveAtGuaranteed, "Aggressive At Guaranteed"),
        new(ProcessorBoostMode.EfficientAggressiveAtGuaranteed, "Efficient Aggressive At Guaranteed")
    ];

    public ProcessorBoostMode SelectedProcessorBoostMode
    {
        get => boostDraftsByProfile.TryGetValue(CurrentPowerLimitKey, out var mode)
            ? mode
            : selectedProcessorBoostMode;
        set
        {
            if (SelectedProcessorBoostMode == value) return;
            selectedProcessorBoostMode = value;
            boostDraftsByProfile[CurrentPowerLimitKey] = value;
            OnPropertyChanged();
        }
    }

    public bool IsPerformanceApplyEnabled =>
        cpuPolicyController is not null &&
        boostController is not null &&
        (!CanEditPowerLimits || IsPerformanceDraftValid) &&
        !isApplyingPerformancePolicy && !isApplyingPlatformMode;

    public string PerformanceReadbackText
    {
        get
        {
            if (livePerformanceReadbackText is not null)
            {
                return livePerformanceReadbackText;
            }

            var readback = GetCurrentPowerDraft().Readback;
            if (readback is null)
            {
                return "OEM 功耗档案暂未读取 · 当前仅保留本地草稿";
            }

            var pl1 = readback.Pl1Enabled ? "启用" : "未启用";
            var pl2 = readback.Pl2Enabled ? "启用" : "未启用";
            return SelectedProfileId == "custom"
                ? $"GCU 配置回读 · {readback.SourceName} · PL1 {readback.Pl1Watts} W（{pl1}） · PL2 {readback.Pl2Watts} W（{pl2}）"
                : $"OEM 固件预设 · GCU 配置回显 PL1 {readback.Pl1Watts} W（{pl1}） / PL2 {readback.Pl2Watts} W（{pl2}）· 非实际硬件 PL";
        }
    }

    public string PerformanceValidationText => IsPerformanceDraftValid
        ? "本地草稿有效 · PL1 不高于 PL2"
        : "数值无效 · 请检查 5–250 W 范围与 PL1 / PL2 顺序";

    public bool IsProcessorFrequencyLimitEnabled
    {
        get => isProcessorFrequencyLimitEnabled;
        set
        {
            if (isProcessorFrequencyLimitEnabled == value) return;
            isProcessorFrequencyLimitEnabled = value;
            frequencyConfiguredByProfile[CurrentPowerLimitKey] = true;
            frequencyDraftsByProfile[CurrentPowerLimitKey] = value
                ? selectedMaximumProcessorFrequencyMhz
                : 0;
            OnPropertyChanged();
        }
    }

    public int SelectedMaximumProcessorFrequencyMhz
    {
        get => selectedMaximumProcessorFrequencyMhz;
        set
        {
            var clamped = Math.Clamp(value, 1000, 6000);
            if (selectedMaximumProcessorFrequencyMhz == clamped) return;
            selectedMaximumProcessorFrequencyMhz = clamped;
            if (IsProcessorFrequencyLimitEnabled)
            {
                frequencyDraftsByProfile[CurrentPowerLimitKey] = clamped;
            }
            OnPropertyChanged();
        }
    }

    public string ProcessorFrequencyLimitStatusText
    {
        get => processorFrequencyLimitStatusText;
        private set
        {
            if (processorFrequencyLimitStatusText == value) return;
            processorFrequencyLimitStatusText = value;
            OnPropertyChanged();
        }
    }

    public bool CanApplyCustomPerformance => CanEditAdvancedPerformance;

    public bool CanEditAdvancedPerformance =>
        IsCustomProfileSelected && customPerformanceController is not null && !isApplyingCustomPerformance &&
        !isReadingCustomPerformance && !isApplyingPlatformMode;

    private bool isReadingCustomPerformance;
    private long customPerformanceReadRevision;

    public bool IsCustomProfileSelected => SelectedProfileId == "custom";

    public string AdvancedPerformanceTitle => IsCustomProfileSelected
        ? "自定义模式 · CPU / GPU 高级设置"
        : $"{CurrentFanProfileLabel.Replace("当前：", string.Empty)} · 高级参数只读";

    public string AdvancedPerformanceDescription => IsCustomProfileSelected
        ? "调整后自动应用到当前方案。"
        : "请切换到自定义模式后调整这些参数。";

    public string AdvancedPerformanceApplyLabel => IsCustomProfileSelected
        ? "调整后自动应用"
        : "仅在自定义模式可调整";

    public bool CustomOverclockingEnabled
    {
        get => customOverclockingEnabled;
        set
        {
            if (customOverclockingEnabled == value) return;
            customOverclockingEnabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanEditGpuPower));
            OnPropertyChanged(nameof(CanEditGpuClockOffsets));
        }
    }

    public bool CustomCpuTemperatureWallEnabled
    {
        get => customCpuTemperatureWallEnabled;
        set { if (customCpuTemperatureWallEnabled == value) return; customCpuTemperatureWallEnabled = value; OnPropertyChanged(); }
    }

    public int CustomCpuTemperatureWallC
    {
        get => customCpuTemperatureWallC;
        set { var v = Math.Clamp(value, 70, 105); if (customCpuTemperatureWallC == v) return; customCpuTemperatureWallC = v; OnPropertyChanged(); }
    }

    public int CustomCpuBurstPowerLimitWatts
    {
        get => customCpuBurstPowerLimitWatts;
        set { var v = Math.Clamp(value, 5, 400); if (customCpuBurstPowerLimitWatts == v) return; customCpuBurstPowerLimitWatts = v; OnPropertyChanged(); }
    }

    public bool CustomCpuAdvancedMenuEnabled
    {
        get => customCpuAdvancedMenuEnabled;
        set { if (customCpuAdvancedMenuEnabled == value) return; customCpuAdvancedMenuEnabled = value; OnPropertyChanged(); }
    }

    public int CustomGpuTgpWatts
    {
        get => customGpuTgpWatts;
        set { var v = Math.Clamp(value, 25, 250); if (customGpuTgpWatts == v) return; customGpuTgpWatts = v; OnPropertyChanged(); }
    }

    public bool CustomGpuDynamicBoostEnabled
    {
        get => customGpuDynamicBoostEnabled;
        set { if (customGpuDynamicBoostEnabled == value) return; customGpuDynamicBoostEnabled = value; OnPropertyChanged(); }
    }

    public int CustomGpuDynamicBoostWatts
    {
        get => customGpuDynamicBoostWatts;
        set { var v = Math.Clamp(value, 0, 50); if (customGpuDynamicBoostWatts == v) return; customGpuDynamicBoostWatts = v; OnPropertyChanged(); }
    }

    public int CustomGpuCoreOffsetMhz
    {
        get => customGpuCoreOffsetMhz;
        set { var v = Math.Clamp(value, CustomGpuCoreOffsetMinimum, CustomGpuCoreOffsetMaximum); if (customGpuCoreOffsetMhz == v) return; customGpuCoreOffsetMhz = v; OnPropertyChanged(); }
    }

    public int CustomGpuMemoryOffsetMhz
    {
        get => customGpuMemoryOffsetMhz;
        set { var v = Math.Clamp(value, CustomGpuMemoryOffsetMinimum, CustomGpuMemoryOffsetMaximum); if (customGpuMemoryOffsetMhz == v) return; customGpuMemoryOffsetMhz = v; OnPropertyChanged(); }
    }

    private GpuClockOffsetLimits gpuClockOffsetLimits = GpuClockOffsetLimits.InputBounds;
    private GpuPowerLimits? gpuPowerLimits;
    private bool hasGpuClockOffsetReadback;
    public int CustomGpuCoreOffsetMinimum => gpuClockOffsetLimits.CoreMinimum;
    public int CustomGpuCoreOffsetMaximum => gpuClockOffsetLimits.CoreMaximum;
    public int CustomGpuMemoryOffsetMinimum => gpuClockOffsetLimits.MemoryMinimum;
    public int CustomGpuMemoryOffsetMaximum => gpuClockOffsetLimits.MemoryMaximum;
    public bool CanEditGpuClockOffsets => CanEditAdvancedPerformance && CustomOverclockingEnabled && hasGpuClockOffsetReadback;
    public bool CanEditGpuPower => CanEditAdvancedPerformance && CustomOverclockingEnabled && gpuPowerLimits is not null;
    public int CustomGpuTgpMinimum => gpuPowerLimits is { } limits ? GpuPowerBudgetRange.MinimumTarget(limits) : 25;
    public int CustomGpuTgpMaximum => gpuPowerLimits?.TgpMaximum ?? 250;
    public int CustomGpuDynamicBoostMinimum => gpuPowerLimits?.DynamicBoostMinimum ?? 0;
    public int CustomGpuDynamicBoostMaximum => gpuPowerLimits?.DynamicBoostMaximum ?? 50;
    public string GpuOffsetRangeText => hasGpuClockOffsetReadback
        ? $"可尝试范围：核心 {CustomGpuCoreOffsetMinimum}～{CustomGpuCoreOffsetMaximum} MHz；显存 {CustomGpuMemoryOffsetMinimum}～{CustomGpuMemoryOffsetMaximum} MHz。" +
          (CustomGpuCoreOffsetMinimum >= 0 ? "本机未开放核心负偏移。" : "核心负值须经 OEM 回读确认，部分机型会拒绝。")
        : "偏移范围待读取；负值仅在 OEM 支持时可用。";

    private void ApplyGpuClockOffsetReadback(GpuClockOffsetState? state)
    {
        hasGpuClockOffsetReadback = state is { IsCustomMode: true, Limits: not null };
        gpuClockOffsetLimits = state?.Limits ?? GpuClockOffsetLimits.InputBounds;
        gpuPowerLimits = state is { IsCustomMode: true } ? state.PowerLimits : null;
        OnPropertyChanged(nameof(CustomGpuCoreOffsetMinimum));
        OnPropertyChanged(nameof(CustomGpuCoreOffsetMaximum));
        OnPropertyChanged(nameof(CustomGpuMemoryOffsetMinimum));
        OnPropertyChanged(nameof(CustomGpuMemoryOffsetMaximum));
        OnPropertyChanged(nameof(CanEditGpuClockOffsets));
        OnPropertyChanged(nameof(CanEditGpuPower));
        OnPropertyChanged(nameof(CustomGpuTgpMinimum));
        OnPropertyChanged(nameof(CustomGpuTgpMaximum));
        OnPropertyChanged(nameof(CustomGpuDynamicBoostMinimum));
        OnPropertyChanged(nameof(CustomGpuDynamicBoostMaximum));
        OnPropertyChanged(nameof(GpuOffsetRangeText));
        if (state is not null)
        {
            CustomOverclockingEnabled = state.OverclockingEnabled;
            CustomGpuCoreOffsetMhz = state.Offsets.CoreMhz;
            CustomGpuMemoryOffsetMhz = state.Offsets.MemoryMhz;
        }
    }

    public string CustomPerformanceStatusText
    {
        get => customPerformanceStatusText;
        private set { if (customPerformanceStatusText == value) return; customPerformanceStatusText = value; OnPropertyChanged(); }
    }

    private string gpuPowerStatusText = "";
    public string GpuPowerStatusText
    {
        get => gpuPowerStatusText;
        private set { if (gpuPowerStatusText == value) return; gpuPowerStatusText = value; OnPropertyChanged(); }
    }

    public string FanCurveLimitText
    {
        get
        {
            var points = fanDraft is null
                ? []
                : (showingCpuFan ? fanDraft.Cpu : fanDraft.Gpu)
                    .Where(point => point.UpTemperatureC < 255)
                    .ToArray();
            if (points.Length == 0)
            {
                return "正在读取风扇曲线…";
            }

            if (!IsCustomProfileSelected && fanReadback is null)
            {
                return "示意曲线，实际转速由电脑控制。";
            }

            var channel = showingCpuFan ? "CPU" : "GPU";
            return $"{channel} 曲线最高温度：{points[^1].UpTemperatureC}℃";
        }
    }

    public string SelectedProfileId
    {
        get => selectedProfileId;
        private set
        {
            if (selectedProfileId == value)
            {
                return;
            }

            selectedProfileId = value;
            hasGpuClockOffsetReadback = false;
            gpuPowerLimits = null;
            OnPropertyChanged(nameof(CanEditGpuClockOffsets));
            OnPropertyChanged(nameof(CanEditGpuPower));
            OnPropertyChanged(nameof(GpuOffsetRangeText));
            livePerformanceReadbackText = null;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Tradeoff));
            OnPropertyChanged(nameof(IsTradeoffVisible));
            OnPropertyChanged(nameof(CurrentFanProfileLabel));
            OnPropertyChanged(nameof(FanAccentColor));
            OnPropertyChanged(nameof(FanAccentSurfaceColor));
            OnPropertyChanged(nameof(PerformanceAccentColor));
            OnPropertyChanged(nameof(PerformanceAccentSurfaceColor));
            OnPropertyChanged(nameof(CanEditOemPowerLimits));
            OnPropertyChanged(nameof(CanEditDirectPowerLimits));
            OnPropertyChanged(nameof(CanEditPowerLimits));
            OnPropertyChanged(nameof(OemPowerLimitAvailabilityReason));
            OnPropertyChanged(nameof(CanApplyCustomPerformance));
            OnPropertyChanged(nameof(CanEditAdvancedPerformance));
            OnPropertyChanged(nameof(IsCustomProfileSelected));
            OnPropertyChanged(nameof(CanEditFanSettings));
            OnPropertyChanged(nameof(IsFanApplyEnabled));
            OnPropertyChanged(nameof(FanEditingAvailabilityText));
            OnPropertyChanged(nameof(FanCurveModeHeading));
            OnPropertyChanged(nameof(FanCurveSurfaceOpacity));
            OnPropertyChanged(nameof(AdvancedPerformanceTitle));
            OnPropertyChanged(nameof(AdvancedPerformanceDescription));
            OnPropertyChanged(nameof(AdvancedPerformanceApplyLabel));
            RefreshPerformanceProperties();
        }
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set
        {
            if (!hasOperationFeedback)
            {
                hasOperationFeedback = true;
                OnPropertyChanged(nameof(HasOperationFeedback));
            }
            if (statusMessage == value)
            {
                return;
            }

            statusMessage = value;
            OnPropertyChanged();
        }
    }

    public string TelemetryStatusMessage => telemetryStatusMessage;
    public bool HasOperationFeedback => hasOperationFeedback;

    private void SetTelemetryStatus(string message)
    {
        if (telemetryStatusMessage != message)
        {
            telemetryStatusMessage = message;
            OnPropertyChanged(nameof(TelemetryStatusMessage));
        }
        // Keep operation feedback until another operation replaces it. Sensor updates
        // have their own display line and must never hide a failed control request.
        if (!hasOperationFeedback && statusMessage != message)
        {
            statusMessage = message;
            OnPropertyChanged(nameof(StatusMessage));
        }
    }

    public void SelectProfile(string profileId)
    {
        if (Profiles.Any(profile => profile.Id == profileId))
        {
            SelectedProfileId = profileId;
            SwitchFanDraftForCurrentProfile();
            RefreshFanPoints();
            SwitchFrequencyDraftForCurrentProfile();
            StatusMessage = "界面预览已切换 · 未向硬件发送指令";
        }
    }

    public async Task<bool> InitializePlatformModeAsync(CancellationToken cancellationToken)
    {
        if (platformModeController is null || isApplyingPlatformMode) return false;
        var requestRevision = Volatile.Read(ref platformRequestRevision);
        try
        {
            var live = await platformModeController.ReadPlatformModeAsync(cancellationToken);
            if (live is null || isApplyingPlatformMode ||
                requestRevision != Volatile.Read(ref platformRequestRevision)) return false;
            ConfirmPlatformSelection(live);
            ApplyLivePlatformSelection(live);
            lastSynchronizedPlatformSelection = live;
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            StatusMessage = $"整机档位读取失败 · {exception.Message}";
            return false;
        }
    }

    public async Task<bool> ApplyPlatformProfileAsync(string profileId, CancellationToken cancellationToken)
    {
        if (platformModeController is null) return false;
        var selection = profileId switch
        {
            "office" => new PlatformModeSelection(PlatformMode.Office),
            "balanced" => new PlatformModeSelection(PlatformMode.Balanced),
            "turbo" => new PlatformModeSelection(
                SelectedTurboStrategy is TurboStrategy.HighEfficiency && CanUseTurboHighMode
                    ? PlatformMode.TurboHigh
                    : PlatformMode.TurboQuiet),
            "custom" => new PlatformModeSelection(PlatformMode.Custom, SelectedCustomProfileIndex),
            _ => null
        };
        if (selection is null) return false;
        return await ApplyPlatformSelectionAsync(selection, cancellationToken);
    }

    public Task<bool> ApplyTurboStrategyAsync(TurboStrategy strategy, CancellationToken cancellationToken) =>
        ApplyPlatformSelectionAsync(
            new(strategy is TurboStrategy.HighEfficiency ? PlatformMode.TurboHigh : PlatformMode.TurboQuiet),
            cancellationToken);

    public Task<bool> ApplyCustomProfileAsync(int profileIndex, CancellationToken cancellationToken)
    {
        if (profileIndex is < 1 or > 3) return Task.FromResult(false);
        return ApplyPlatformSelectionAsync(new(PlatformMode.Custom, profileIndex), cancellationToken);
    }

    public Task<bool> ApplyPlatformSelectionFromTrayAsync(
        PlatformModeSelection selection,
        CancellationToken cancellationToken) =>
        ApplyPlatformSelectionAsync(selection, cancellationToken);

    private async Task<bool> ApplyPlatformSelectionAsync(
        PlatformModeSelection selection,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (isShuttingDown || platformModeController is null) return false;
        if (!SupportsPlatformMode(selection.Mode))
        {
            StatusMessage = "当前设备尚未返回该 OEM 性能档位的可用证据";
            return false;
        }

        var previousPresentation = confirmedPlatformSelection ?? CurrentPlatformSelection();
        var requestRevision = Interlocked.Increment(ref platformRequestRevision);
        Interlocked.Increment(ref platformObservationRevision);
        isApplyingPlatformMode = true;
        OnPropertyChanged(nameof(IsPlatformSwitchPending));
        // This is presentation only. The confirmed selection is deliberately kept separate,
        // and parameter writes stay behind the transition gate until hardware confirmation.
        ApplyLivePlatformSelection(selection);
        StatusMessage = "正在切换整机档位并等待 GCU 回读…";
        NotifyCustomPerformanceAvailability();
        RefreshPerformanceProperties();
        var gateHeld = false;
        try
        {
            await performanceTransitionGate.WaitAsync(cancellationToken);
            gateHeld = true;
            // A newer click supersedes requests that have not started writing yet.
            if (isShuttingDown || requestRevision != Volatile.Read(ref platformRequestRevision)) return false;
            var succeeded = await ApplyPlatformSelectionCoreAsync(selection, requestRevision, cancellationToken);
            if (!succeeded && requestRevision == Volatile.Read(ref platformRequestRevision))
                await RestorePlatformPresentationAsync(requestRevision, previousPresentation);
            return succeeded;
        }
        catch (OperationCanceledException)
        {
            // Cancellation while queued must first let the in-flight switch finish;
            // otherwise an old confirmation could arrive after the fallback was shown.
            if (!gateHeld)
            {
                await performanceTransitionGate.WaitAsync(CancellationToken.None);
                gateHeld = true;
            }
            if (requestRevision == Volatile.Read(ref platformRequestRevision))
            {
                StatusMessage = "档位切换已取消，正在同步实际档位。";
                await RestorePlatformPresentationAsync(requestRevision, previousPresentation);
            }
            throw;
        }
        finally
        {
            if (requestRevision == Volatile.Read(ref platformRequestRevision))
            {
                isApplyingPlatformMode = false;
                OnPropertyChanged(nameof(IsPlatformSwitchPending));
                NotifyCustomPerformanceAvailability();
                RefreshPerformanceProperties();
            }
            if (gateHeld) performanceTransitionGate.Release();
        }
    }

    private async Task<bool> ApplyPlatformSelectionCoreAsync(
        PlatformModeSelection selection,
        long requestRevision,
        CancellationToken cancellationToken)
    {
        if (platformModeController is null) return false;
        bool IsLatestRequest() => requestRevision == Volatile.Read(ref platformRequestRevision);
        try
        {
            if (!await PauseDynamicForManualControlAsync()) return false;
            if (!IsLatestRequest()) return false;
            if (!SupportsPlatformMode(selection.Mode))
            {
                StatusMessage = "当前设备尚未返回该 OEM 性能档位的可用证据";
                return false;
            }
            StatusMessage = "正在切换整机档位并等待 GCU 回读…";
            if (scheduleController is null)
            {
                var modeResult = await platformModeController.SetPlatformModeAsync(selection, cancellationToken);
                if (!modeResult.Succeeded)
                {
                    if (IsLatestRequest()) StatusMessage = modeResult.Reason;
                    return false;
                }
                ConfirmPlatformSelection(selection);
                lastSynchronizedPlatformSelection = selection;
                if (IsLatestRequest()) StatusMessage = modeResult.Reason;
                return true;
            }

            var schedule = CreateSchedule(selection);
            var result = await scheduleController.ApplyAsync(
                schedule, platformAlreadySelected: false, cancellationToken);
            if (!result.Succeeded)
            {
                if (IsLatestRequest()) StatusMessage = result.Reason;
                return false;
            }
            ConfirmPlatformSelection(result.Readback?.Platform ?? selection);
            lastSynchronizedPlatformSelection = selection;
            if (!IsLatestRequest()) return true;
            UpdatePerformanceReadback(result.Readback);
            StatusMessage = "档位已切换，正在复查 Windows 频率策略…";
            var frequencySettlement = await SettleFrequencyAfterPlatformSwitchAsync(
                schedule.Frequency, requestRevision, cancellationToken);
            if (!IsLatestRequest()) return true;
            SavePerformanceProfile(selection);
            StatusMessage = frequencySettlement.Succeeded
                ? frequencySettlement.Reason
                : $"{result.Reason} · {frequencySettlement.Reason}";
            return frequencySettlement.Succeeded;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (IsLatestRequest()) StatusMessage = $"整机档位切换异常 · {exception.Message}";
            return false;
        }
    }

    private void ConfirmPlatformSelection(PlatformModeSelection selection)
    {
        if (confirmedPlatformSelection == selection) return;
        confirmedPlatformSelection = selection;
        OnPropertyChanged(nameof(ConfirmedPlatformSelection));
    }

    private async Task RestorePlatformPresentationAsync(long requestRevision, PlatformModeSelection fallback)
    {
        var reason = StatusMessage;
        var actual = confirmedPlatformSelection ?? fallback;
        var readSucceeded = false;
        try
        {
            using var readTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var live = await platformModeController!.ReadPlatformModeAsync(readTimeout.Token);
            if (live is not null)
            {
                actual = live;
                readSucceeded = true;
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A failed read must not keep the optimistic target displayed as confirmed.
        }
        if (requestRevision != Volatile.Read(ref platformRequestRevision)) return;
        if (readSucceeded) ConfirmPlatformSelection(actual);
        ApplyLivePlatformSelection(actual);
        StatusMessage = readSucceeded ? reason : $"{reason} · 实际档位暂时无法回读，已恢复上次显示。";
    }

    public async Task<bool> ObservePlatformModeAsync(
        PlatformModeSelection selection,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (isApplyingPlatformMode || isApplyingPerformancePolicy)
        {
            return false;
        }

        // Repeated notifications for the same mode share the existing settle window.
        if (pendingFrequencyObservation == selection) return true;

        var observationRevision = Interlocked.Increment(ref platformObservationRevision);
        ConfirmPlatformSelection(selection);
        ApplyLivePlatformSelection(selection);
        lastSynchronizedPlatformSelection = selection;
        if (frequencyLimitController is null)
        {
            StatusMessage = "已同步显示当前 OEM 档位；未向硬件追加写入。";
            return true;
        }

        pendingFrequencyObservation = selection;
        pendingFrequencyObservationRevision = observationRevision;
        var gateHeld = false;

        try
        {
            StatusMessage = "已同步当前 OEM 档位，正在核对对应的 Windows 频率策略…";
            await verificationDelay(TimeSpan.FromSeconds(2), cancellationToken);
            await performanceTransitionGate.WaitAsync(cancellationToken);
            gateHeld = true;
            if (observationRevision != Volatile.Read(ref platformObservationRevision))
            {
                return true;
            }

            var expected = CreateFrequencyPolicy(selection);
            var current = await frequencyLimitController.ReadAsync(cancellationToken);
            ProcessorFrequencyLimitStatusText = FormatFrequencyReadback(current);
            if (current == expected)
            {
                StatusMessage = "OEM 档位与对应的 Windows 频率策略已同步。";
                return true;
            }

            if (observationRevision != Volatile.Read(ref platformObservationRevision))
            {
                return true;
            }

            var applied = await frequencyLimitController.ApplyAsync(expected, cancellationToken);
            if (!applied.Succeeded)
            {
                StatusMessage = $"已同步 OEM 档位，但频率策略恢复失败：{applied.Reason}";
                return false;
            }

            ProcessorFrequencyLimitStatusText = FormatFrequencyReadback(expected);
            StatusMessage = "检测到上一档位遗留的频率限制，已按当前 OEM 档位恢复。";
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            StatusMessage = $"已同步 OEM 档位，但核对频率策略时发生异常：{exception.Message}";
            return false;
        }
        finally
        {
            if (gateHeld) performanceTransitionGate.Release();
            if (pendingFrequencyObservationRevision == observationRevision)
                pendingFrequencyObservation = null;
        }
    }

    public async Task<bool> SynchronizeControlSessionAsync(CancellationToken cancellationToken)
    {
        if (platformModeController is null || isApplyingPlatformMode) return false;
        var requestRevision = Volatile.Read(ref platformRequestRevision);
        try
        {
            var live = await platformModeController.ReadPlatformModeAsync(cancellationToken);
            if (live is null || isApplyingPlatformMode ||
                requestRevision != Volatile.Read(ref platformRequestRevision)) return false;
            lastSynchronizedPlatformSelection = null;
            return await ObservePlatformModeAsync(live, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            StatusMessage = $"控制会话恢复同步失败 · {exception.Message}";
            return false;
        }
    }

    private void ApplyLivePlatformSelection(PlatformModeSelection selection)
    {
        if (selection.Mode is PlatformMode.Custom && selection.ProfileIndex is >= 1 and <= 3)
        {
            if (selectedCustomProfileIndex != selection.ProfileIndex)
                InvalidateGpuRangesForProfileChange();
            selectedCustomProfileIndex = selection.ProfileIndex;
            OnPropertyChanged(nameof(SelectedCustomProfileIndex));
        }

        if (selection.Mode is PlatformMode.TurboQuiet or PlatformMode.TurboHigh)
        {
            SelectedTurboStrategy = selection.Mode is PlatformMode.TurboHigh
                ? TurboStrategy.HighEfficiency
                : TurboStrategy.Quiet;
        }

        SelectProfile(selection.Mode switch
        {
            PlatformMode.Office => "office",
            PlatformMode.Balanced => "balanced",
            PlatformMode.TurboQuiet or PlatformMode.TurboHigh => "turbo",
            PlatformMode.Custom => "custom",
            _ => selectedProfileId
        });
    }

    private void InvalidateGpuRangesForProfileChange()
    {
        hasGpuClockOffsetReadback = false;
        gpuPowerLimits = null;
        GpuPowerStatusText = "";
        OnPropertyChanged(nameof(CanEditGpuClockOffsets));
        OnPropertyChanged(nameof(CanEditGpuPower));
        OnPropertyChanged(nameof(CustomGpuTgpMinimum));
        OnPropertyChanged(nameof(CustomGpuTgpMaximum));
        OnPropertyChanged(nameof(CustomGpuDynamicBoostMinimum));
        OnPropertyChanged(nameof(CustomGpuDynamicBoostMaximum));
        OnPropertyChanged(nameof(GpuOffsetRangeText));
    }

    public void ApplyTelemetry(HardwareTelemetrySnapshot snapshot)
    {
        var freshOemTemperature = oemCpuTemperatureAt is { } sampledAt &&
            snapshot.CapturedAt - sampledAt <= TelemetryMaximumAge ? oemCpuTemperatureC : null;
        var cpuPackageTemperature = snapshot.Devices
            .FirstOrDefault(device => device.Kind is DeviceKind.Cpu)?.TemperatureC;
        if (cpuPackageTemperature is not { } packageTemperature ||
            !double.IsFinite(packageTemperature) ||
            packageTemperature <= 0)
        {
            cpuPackageTemperature = freshOemTemperature;
        }

        foreach (var telemetry in snapshot.Devices)
        {
            var matchIndex = FindTelemetryDeviceIndex(telemetry);
            if (matchIndex < 0)
            {
                continue;
            }

            var device = Devices[matchIndex];
            deviceSampleTimes[(device.Kind, device.Model)] = snapshot.CapturedAt;
            var formatted = TelemetryFormatter.Format(telemetry);
            var sharesCpuPackageTemperature = telemetry.Kind is DeviceKind.IntegratedGpu &&
                                               telemetry.TemperatureC is null &&
                                               cpuPackageTemperature is { };
            var defaultDetails = device.Details;
            foreach (var original in Dashboard.Devices)
            {
                if (original.Kind == device.Kind &&
                    string.Equals(original.Model, device.Model, StringComparison.OrdinalIgnoreCase))
                {
                    defaultDetails = original.Details;
                    break;
                }
            }
            var dedicatedMemoryDetails = telemetry.Kind is DeviceKind.DiscreteGpu or DeviceKind.UnknownGpu
                ? FormatDedicatedMemory(telemetry.DedicatedMemoryMegabytes)
                : null;
            var temperature = sharesCpuPackageTemperature
                ? FormatTemperature(cpuPackageTemperature!.Value)
                : telemetry.Kind is DeviceKind.Cpu &&
                  telemetry.TemperatureC is null &&
                  freshOemTemperature is { } oemTemperature
                    ? FormatTemperature(oemTemperature)
                    : formatted.Temperature;
            var details = dedicatedMemoryDetails ?? defaultDetails;
            if (device.Usage == formatted.Usage && device.Temperature == temperature &&
                device.Power == formatted.Power && device.Frequency == formatted.Frequency &&
                device.Details == details) continue;

            Devices[matchIndex] = device with
            {
                Usage = formatted.Usage,
                Temperature = temperature,
                Power = formatted.Power,
                Frequency = formatted.Frequency,
                Details = details
            };
        }

        ExpireTelemetry(snapshot.CapturedAt);
        SetTelemetryStatus($"只读传感器 · 更新于 {snapshot.CapturedAt:HH:mm:ss}");
    }

    private void ExpireTelemetry(DateTimeOffset now)
    {
        for (var index = 0; index < Devices.Count; index++)
        {
            var device = Devices[index];
            if (deviceSampleTimes.TryGetValue((device.Kind, device.Model), out var lastSeen) &&
                now - lastSeen <= TelemetryMaximumAge) continue;
            var temperature = device.Kind == DeviceKind.Cpu && oemCpuTemperatureAt is { } oemAt &&
                now - oemAt <= TelemetryMaximumAge && oemCpuTemperatureC is { } oemValue
                    ? FormatTemperature(oemValue) : "N/A";
            var expired = device with { Usage = "N/A", Temperature = temperature, Power = "N/A", Frequency = "N/A" };
            if (expired != device) Devices[index] = expired;
        }
    }

    private int FindTelemetryDeviceIndex(DeviceTelemetry telemetry)
    {
        var candidateIndex = -1;
        var candidateCount = 0;
        for (var index = 0; index < Devices.Count; index++)
        {
            var device = Devices[index];
            if (device.Kind != telemetry.Kind) continue;
            if (string.Equals(device.Model, telemetry.Model, StringComparison.OrdinalIgnoreCase))
                return index;
            candidateIndex = index;
            candidateCount++;
        }

        return candidateCount == 1 ? candidateIndex : -1;
    }

    private static string? FormatDedicatedMemory(double? megabytes)
    {
        if (megabytes is not { } value || !double.IsFinite(value) || value <= 0)
        {
            return null;
        }

        var gigabytes = Math.Round(value / 1024d, MidpointRounding.AwayFromZero);
        return gigabytes > 0 ? $"{gigabytes:0} GB 显存" : null;
    }

    public void ApplyOemCpuStatus(OemCpuStatus status, bool refreshDisplay = true)
    {
        if (status.TemperatureC is not { } temperature || !double.IsFinite(temperature) || temperature <= 0)
        {
            return;
        }

        oemCpuTemperatureC = temperature;
        oemCpuTemperatureAt = utcNow();
        if (!refreshDisplay) return;

        var match = Devices
            .Select((device, index) => (device, index))
            .FirstOrDefault(item => item.device.Kind is DeviceKind.Cpu);
        if (match.device is null)
        {
            return;
        }

        var updated = match.device with
        {
            Temperature = FormatTemperature(temperature)
        };
        if (updated != match.device) Devices[match.index] = updated;
    }

    public void ApplyFanTable(FanCurveTable table)
    {
        var key = CurrentFanCurveKey;
        fanReadback = CloneTable(table);
        fanDraft = CloneTable(fanReadback);
        fanReadbacksByProfile[key] = fanReadback;
        fanDraftsByProfile[key] = fanDraft;
        showingCpuFan = table.Cpu.Count > 0;
        isFanIndependentOutputEnabled = table.IndependentCpuGpuControl;
        OnPropertyChanged(nameof(IsFanIndependentOutputEnabled));
        RefreshFanPoints();
        FanStatusText = IsCustomProfileSelected
            ? "已读取当前方案的风扇设置"
            : "已读取官方风扇设置";
        StatusMessage = fanController is null
            ? "已读取官方风扇曲线 · 当前控制器不可用"
            : IsCustomProfileSelected
                ? "已读取风扇曲线 · 调整后自动应用"
                : "已读取风扇曲线 · 官方预设仅供参考";
        OnPropertyChanged(nameof(CanEditFanSettings));
        OnPropertyChanged(nameof(IsFanApplyEnabled));
    }

    public void SelectFanChannel(bool cpu)
    {
        if (showingCpuFan == cpu)
        {
            return;
        }

        showingCpuFan = cpu;
        OnPropertyChanged(nameof(IsShowingCpuFan));
        OnPropertyChanged(nameof(IsShowingGpuFan));
        RefreshFanPoints();
    }

    public void ResetFanDraft()
    {
        SwitchFanDraftForCurrentProfile();
        if (fanReadback is null)
        {
            return;
        }

        fanDraft = CloneTable(fanReadback);
        fanDraftsByProfile[CurrentFanCurveKey] = fanDraft;
        RefreshFanPoints();
        FanStatusText = "已恢复该档位原厂默认曲线 · 未写入硬件";
    }

    public async Task OpenFanCurveAsync(CancellationToken cancellationToken)
        => await RunProfileEditAsync(() => ReadFanCurveCoreAsync(cancellationToken), cancellationToken);

    private async Task ReadFanCurveCoreAsync(CancellationToken cancellationToken)
    {
        var targetKey = CurrentFanCurveKey;
        OpenFanCurve();
        if (fanController is null)
        {
            FanStatusText = "本机风扇写入控制器不可用。";
            return;
        }

        SetFanBusy(true);
        try
        {
            var table = await fanController.ReadAsync(cancellationToken);
            var response = await fanController.ReadResponseAsync(cancellationToken);
            if (isShuttingDown || targetKey != CurrentFanCurveKey) return;
            if (table is null || response is null)
            {
                FanStatusText = "未取得完整风扇曲线与响应设置，请稍后重试。";
                return;
            }
            ApplyFanTable(table);
            ApplyFanResponse(response);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            FanStatusText = $"读取风扇设置失败：{exception.Message}";
        }
        finally
        {
            SetFanBusy(false);
        }
    }

    public async Task ApplyFanCurveAsync(CancellationToken cancellationToken)
        => await RunProfileEditAsync(() => ApplyFanCurveCoreAsync(cancellationToken), cancellationToken);

    private async Task ApplyFanCurveCoreAsync(CancellationToken cancellationToken)
    {
        var targetKey = CurrentFanCurveKey;
        var targetSelection = CurrentPlatformSelection();
        bool IsCurrentTarget() => !isShuttingDown && targetKey == CurrentFanCurveKey && targetSelection == CurrentPlatformSelection();
        if (!IsCustomProfileSelected)
        {
            FanStatusText = "请切换到自定义模式后调整风扇。";
            StatusMessage = FanStatusText;
            return;
        }
        if (!SupportsPlatformMode(PlatformMode.Custom) || !SupportsFeature(HardwareFeature.OemFanCurves))
        {
            FanStatusText = "自定义档位或风扇控制尚未确认，曲线仅供查看。";
            StatusMessage = FanStatusText;
            return;
        }
        if (fanController is null || fanDraft is null || !IsFanCurveValid || isFanBusy) return;
        SetFanBusy(true);
        try
        {
            var target = CloneTable(fanDraft) with
            {
                IndependentCpuGpuControl = IsFanIndependentOutputEnabled
            };
            var response = new FanResponseSettings(
                IsFanIndependentOutputEnabled,
                IsFanTransitionSensitivityEnabled,
                FanTransitionSensitivity);
            var result = await fanController.ApplyAsync(target, response, cancellationToken);
            if (!IsCurrentTarget()) return;
            FanStatusText = result.Reason;
            StatusMessage = result.Reason;
            if (result.Succeeded)
            {
                var confirmed = await fanController.ReadAsync(cancellationToken);
                if (!IsCurrentTarget()) return;
                var confirmedResponse = await fanController.ReadResponseAsync(cancellationToken);
                if (!IsCurrentTarget()) return;
                if (confirmed is not null) ApplyFanTable(confirmed);
                if (confirmedResponse is not null) ApplyFanResponse(confirmedResponse);
                FanStatusText = result.Reason;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (!IsCurrentTarget()) return;
            FanStatusText = $"风扇设置失败：{exception.Message}";
            StatusMessage = FanStatusText;
        }
        finally
        {
            SetFanBusy(false);
        }
    }

    public async Task RestoreOemFanCurveAsync(CancellationToken cancellationToken)
        => await RunProfileEditAsync(() => RestoreOemFanCurveCoreAsync(cancellationToken), cancellationToken);

    private async Task RestoreOemFanCurveCoreAsync(CancellationToken cancellationToken)
    {
        var targetKey = CurrentFanCurveKey;
        var targetSelection = CurrentPlatformSelection();
        bool IsCurrentTarget() => !isShuttingDown && targetKey == CurrentFanCurveKey && targetSelection == CurrentPlatformSelection();
        if (!IsCustomProfileSelected)
        {
            FanStatusText = "请切换到自定义模式后恢复默认曲线。";
            StatusMessage = FanStatusText;
            return;
        }
        if (fanController is null || fanDraft is null || isFanBusy) return;
        SetFanBusy(true);
        try
        {
            var result = await fanController.RestoreAsync(fanDraft.Name, cancellationToken);
            if (!IsCurrentTarget()) return;
            FanStatusText = result.Result.Reason;
            StatusMessage = result.Result.Reason;
            if (result.Result.Succeeded && result.Table is not null)
            {
                ApplyFanTable(result.Table);
                FanStatusText = result.Result.Reason;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (!IsCurrentTarget()) return;
            FanStatusText = $"恢复原厂风扇曲线失败：{exception.Message}";
            StatusMessage = FanStatusText;
        }
        finally
        {
            SetFanBusy(false);
        }
    }

    public void ReportTelemetryUnavailable(string reason)
    {
        ExpireTelemetry(utcNow());
        SetTelemetryStatus("只读传感器暂不可用 · 将自动重试");
    }

    public void ReportOemStatusUnavailable(string reason)
    {
        if (fanReadback is null && fanReadbacksByProfile.Count == 0)
        {
            FanStatusText = reason.Contains("官方控制中心", StringComparison.Ordinal)
                ? reason
                : "官方 GCU 状态通道暂不可用 · 将自动重试";
        }
    }

    private string oemConnectionStatusText = "等待读取 GCU 控制通道状态。";
    public string OemConnectionStatusText => oemConnectionStatusText;
    public void ReportOemConnectionState(string message)
    {
        if (oemConnectionStatusText == message) return;
        oemConnectionStatusText = message;
        OnPropertyChanged(nameof(OemConnectionStatusText));
    }

    public void SelectTurboStrategy(TurboStrategy strategy)
    {
        SelectedTurboStrategy = strategy;
        SelectProfile("turbo");
        StatusMessage = strategy is TurboStrategy.Quiet
            ? "狂暴 · 静音策略预览 · 未向硬件发送指令"
            : "狂暴 · 高能策略预览 · 未向硬件发送指令";
    }

    public void SelectCustomProfile(int profileIndex)
    {
        if (profileIndex is < 1 or > 3)
        {
            return;
        }

        if (selectedCustomProfileIndex != profileIndex)
        {
            InvalidateGpuRangesForProfileChange();
            selectedCustomProfileIndex = profileIndex;
            OnPropertyChanged(nameof(SelectedCustomProfileIndex));
        }
        SelectProfile("custom");
        RefreshPerformanceProperties();
        StatusMessage = $"自定义方案 {profileIndex} 已切换 · 当前仅本地编辑";
    }

    public void ApplyCpuPowerLimitProfiles(IEnumerable<CpuPowerLimitProfile> profiles, bool preserveDrafts = false)
    {
        powerReadbacksByProfile.Clear();
        if (!preserveDrafts) powerDraftsByProfile.Clear();

        void UpdateDraft(string key, CpuPowerLimitProfile profile)
        {
            powerDraftsByProfile[key] = preserveDrafts && powerDraftsByProfile.TryGetValue(key, out var draft)
                ? new(profile, draft.Pl1Watts, draft.Pl2Watts)
                : CreatePowerDraft(profile);
        }

        foreach (var profile in profiles)
        {
            powerReadbacksByProfile[profile.Key] = profile;
            UpdateDraft(profile.Key, profile);
            if (profile.Key.Equals("turbo:1", StringComparison.OrdinalIgnoreCase))
            {
                UpdateDraft("turbo:Quiet", profile);
                UpdateDraft("turbo:HighEfficiency", profile);
            }
        }

        if (!preserveDrafts) LoadSavedPerformanceProfiles(applicationSettings.PerformanceProfiles);

        RefreshPerformanceProperties();
        if (!preserveDrafts) StatusMessage = powerReadbacksByProfile.Count == 0
            ? "OEM 功耗档案暂不可用 · 性能调整保持安全锁定"
            : $"已只读加载 {powerReadbacksByProfile.Count} 个 OEM 功耗档案";
    }

    public void ResetPerformanceDraft()
    {
        var key = CurrentPowerLimitKey;
        var readback = ResolvePowerReadback(key);
        if (readback is null)
        {
            return;
        }

        powerDraftsByProfile[key] = CreatePowerDraft(readback);
        RefreshPerformanceProperties();
        StatusMessage = "已恢复当前档位 OEM 功耗读取值 · 未写入硬件";
    }

    public async Task InitializePerformancePolicyAsync(CancellationToken cancellationToken)
    {
        if (isShuttingDown || isApplyingPlatformMode || isApplyingPerformancePolicy) return;
        if (cpuPolicyController is null || boostController is null)
        {
            StatusMessage = "性能控制通道未接入";
            return;
        }

        var selection = CurrentPlatformSelection();
        var requestRevision = Volatile.Read(ref platformRequestRevision);
        bool IsCurrentRead() => !isShuttingDown && !isApplyingPlatformMode && !isApplyingPerformancePolicy &&
            selection == CurrentPlatformSelection() && requestRevision == Volatile.Read(ref platformRequestRevision);

        try
        {
            var cpu = await cpuPolicyController.ReadCpuPolicyAsync(cancellationToken);
            if (!IsCurrentRead()) return;
            var boost = await boostController.ReadAsync(cancellationToken);
            if (!IsCurrentRead()) return;
            var frequency = frequencyLimitController is null
                ? null
                : await frequencyLimitController.ReadAsync(cancellationToken);
            if (!IsCurrentRead()) return;
            if (cpu is null)
            {
                StatusMessage = "GCU 未返回 CPU 功耗状态";
                return;
            }

            var draft = GetCurrentPowerDraft();
            if (CanEditPowerLimits &&
                applicationSettings.PerformanceProfiles?.ContainsKey(CurrentPowerLimitKey) != true &&
                (CanEditDirectPowerLimits || draft.Readback is null))
            {
                draft.Pl1Watts = cpu.Pl1Watts;
                draft.Pl2Watts = cpu.Pl2Watts;
                boostDraftsByProfile[CurrentPowerLimitKey] = boost.AcMode;
            }
            selectedProcessorBoostMode = boostDraftsByProfile.TryGetValue(CurrentPowerLimitKey, out var draftBoost)
                ? draftBoost
                : boost.AcMode;
            livePerformanceReadbackText = CanEditPowerLimits
                ? FormatLivePerformanceReadback(cpu, boost)
                : null;
            if (frequency is not null)
            {
                // A live Windows value may have been written by an OEM service or an older
                // RevoHelper build. It is hardware evidence only, never implicit user intent.
                // Only a saved RevoHelper profile or an explicit toggle edit may enable it.
                ProcessorFrequencyLimitStatusText = FormatFrequencyReadback(frequency);
            }
            StatusMessage = CanEditOemPowerLimits
                ? "已读取 Custom 的 GCU PL 配置、Boost 与频率策略"
                : CanEditDirectPowerLimits
                    ? "已读取 CPU 直连 PL1 / PL2、Boost 与频率策略"
                    : "已读取 OEM 固件预设、Boost 与频率策略；预设 PL 不提供硬件回读";
            RefreshPerformanceProperties();
            OnPropertyChanged(nameof(SelectedProcessorBoostMode));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (!IsCurrentRead()) return;
            StatusMessage = $"实时性能策略读取失败 · {exception.Message}";
        }
    }

    public async Task ApplyPerformancePolicyAsync(CancellationToken cancellationToken)
    {
        var selection = CurrentPlatformSelection();
        var revision = Volatile.Read(ref platformObservationRevision);
        await performanceTransitionGate.WaitAsync(cancellationToken);
        try
        {
            // An editor event captured before a mode change must never reselect its old mode.
            if (isShuttingDown || selection != CurrentPlatformSelection() ||
                revision != Volatile.Read(ref platformObservationRevision)) return;
            await ApplyPerformancePolicyCoreAsync(cancellationToken);
        }
        finally { performanceTransitionGate.Release(); }
    }

    private async Task ApplyPerformancePolicyCoreAsync(CancellationToken cancellationToken)
    {
        var selection = CurrentPlatformSelection();
        var requestRevision = Volatile.Read(ref platformRequestRevision);
        bool IsCurrentTarget() => !isShuttingDown && !isApplyingPlatformMode &&
            selection == CurrentPlatformSelection() && requestRevision == Volatile.Read(ref platformRequestRevision);
        if (!await Experiments.PauseForManualControlAsync())
        {
            if (IsCurrentTarget()) StatusMessage = ManualControlBlockedMessage;
            return;
        }
        if (!IsCurrentTarget() || !IsPerformanceApplyEnabled || cpuPolicyController is null || boostController is null)
        {
            return;
        }

        Interlocked.Increment(ref platformObservationRevision);

        if (scheduleController is not null)
        {
            isApplyingPerformancePolicy = true;
            RefreshPerformanceProperties();
            try
            {
                var schedule = CreateSchedule(selection) with
                {
                    Cpu = CanEditPowerLimits
                        ? new CpuPolicy(SelectedPl1Watts, SelectedPl2Watts, null)
                        : null,
                    Boost = new ProcessorBoostPolicy(
                        SelectedProcessorBoostMode, SelectedProcessorBoostMode)
                };
                var result = await scheduleController.ApplyAsync(
                    schedule, platformAlreadySelected: true, cancellationToken);
                if (!result.Succeeded)
                {
                    if (IsCurrentTarget()) StatusMessage = result.Reason;
                    return;
                }

                ConfirmPlatformSelection(result.Readback?.Platform ?? selection);
                if (!IsCurrentTarget()) return;
                UpdatePerformanceReadback(result.Readback);
                SavePerformanceProfile(selection);
                var verification = await VerifyPerformancePolicyStabilityAsync(
                    result.Readback!, cancellationToken);
                if (IsCurrentTarget()) StatusMessage = verification;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                if (IsCurrentTarget()) StatusMessage = $"性能策略应用异常 · {exception.Message}";
            }
            finally
            {
                isApplyingPerformancePolicy = false;
                RefreshPerformanceProperties();
            }
            return;
        }

        isApplyingPerformancePolicy = true;
        RefreshPerformanceProperties();
        ProcessorBoostPolicy? previousBoost = null;
        try
        {
            var targetBoost = new ProcessorBoostPolicy(SelectedProcessorBoostMode, SelectedProcessorBoostMode);
            var targetCpu = CanEditPowerLimits ? new CpuPolicy(SelectedPl1Watts, SelectedPl2Watts, null) : null;
            previousBoost = await boostController.ReadAsync(cancellationToken);
            if (!IsCurrentTarget()) return;
            var boostResult = await boostController.ApplyAsync(targetBoost, cancellationToken);
            if (!boostResult.Succeeded)
            {
                if (IsCurrentTarget()) StatusMessage = $"睿频策略应用失败 · {boostResult.Reason}";
                return;
            }

            if (!IsCurrentTarget()) return;
            if (targetCpu is not null)
            {
                var cpuResult = await cpuPolicyController.ApplyCpuPolicyAsync(targetCpu, cancellationToken);
                if (!cpuResult.Succeeded)
                {
                    var rollback = await boostController.ApplyAsync(previousBoost, cancellationToken);
                    if (IsCurrentTarget())
                        StatusMessage = rollback.Succeeded
                            ? $"PL1 / PL2 应用失败，已恢复睿频策略 · {cpuResult.Reason}"
                            : $"PL1 / PL2 应用失败，睿频策略恢复也失败 · {cpuResult.Reason}；{rollback.Reason}";
                    return;
                }
            }

            if (!IsCurrentTarget()) return;
            var cpuReadback = await cpuPolicyController.ReadCpuPolicyAsync(cancellationToken);
            if (!IsCurrentTarget()) return;
            var boostReadback = await boostController.ReadAsync(cancellationToken);
            if (!IsCurrentTarget()) return;
            if (cpuReadback is null)
            {
                StatusMessage = "设置已发送，但最终 GCU 回读不可用";
                return;
            }

            livePerformanceReadbackText = CanEditPowerLimits
                ? FormatLivePerformanceReadback(cpuReadback, boostReadback)
                : null;
            StatusMessage = CanEditPowerLimits
                ? $"已应用并回读 · PL1 {cpuReadback.Pl1Watts} W / PL2 {cpuReadback.Pl2Watts} W · {BoostModeLabel(boostReadback.AcMode)}"
                : $"已应用并回读 Boost · {BoostModeLabel(boostReadback.AcMode)}；OEM 预设 PL 未改动";
            RefreshPerformanceProperties();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (previousBoost is not null)
            {
                try
                {
                    var rollback = await boostController.ApplyAsync(previousBoost, cancellationToken);
                    if (IsCurrentTarget())
                        StatusMessage = rollback.Succeeded
                            ? $"性能策略应用异常，已恢复睿频策略 · {exception.Message}"
                            : $"性能策略应用及睿频恢复均异常 · {exception.Message}";
                }
                catch (Exception rollbackException) when (rollbackException is not OperationCanceledException)
                {
                    if (IsCurrentTarget()) StatusMessage = $"性能策略应用及睿频恢复均异常 · {exception.Message}；{rollbackException.Message}";
                }
            }
            else
            {
                if (IsCurrentTarget()) StatusMessage = $"性能策略应用异常 · {exception.Message}";
            }
        }
        finally
        {
            isApplyingPerformancePolicy = false;
            RefreshPerformanceProperties();
        }
    }

    public void OpenPerformance()
    {
        ActiveDrawer = HomeDrawer.Performance;
        StatusMessage = "性能调整面板已展开";
    }

    public async Task OpenPerformanceAsync(CancellationToken cancellationToken)
    {
        OpenPerformance();
        if (customPerformanceController is null || isApplyingPlatformMode || isApplyingCustomPerformance) return;
        var selection = CurrentPlatformSelection();
        var platformRevision = Volatile.Read(ref platformRequestRevision);
        var readRevision = Interlocked.Increment(ref customPerformanceReadRevision);
        bool IsCurrentRead() => !isShuttingDown && !isApplyingPlatformMode && !isApplyingCustomPerformance &&
            readRevision == Volatile.Read(ref customPerformanceReadRevision) &&
            platformRevision == Volatile.Read(ref platformRequestRevision) && selection == CurrentPlatformSelection();
        isReadingCustomPerformance = true;
        GpuPowerStatusText = "";
        NotifyCustomPerformanceAvailability();
        try
        {
            if (IsCustomProfileSelected)
            {
                hasGpuClockOffsetReadback = false;
                OnPropertyChanged(nameof(CanEditGpuClockOffsets));
                var profileIndex = SelectedCustomProfileIndex;
                var snapshot = await customPerformanceController.ReadSnapshotAsync(profileIndex, cancellationToken);
                if (!IsCurrentRead() || !IsCustomProfileSelected || profileIndex != SelectedCustomProfileIndex) return;
                var state = snapshot.State;
                if (state is null)
                {
                    CustomPerformanceStatusText = "OEM 未返回自定义高级参数，保留当前草稿。";
                    return;
                }
                ApplyCustomPerformanceState(state);
                ApplyGpuClockOffsetReadback(snapshot.Gpu);
                CustomPerformanceStatusText = hasGpuClockOffsetReadback
                    ? "已读取当前自定义方案的 CPU / GPU 高级参数。"
                    : "已读取高级参数；GPU 频率范围未确认，偏移调整暂不可用。";
                if (snapshot.PowerBudgetNote.Length > 0) CustomPerformanceStatusText = snapshot.PowerBudgetNote;
                return;
            }

            var gpu = await customPerformanceController.ReadGpuAsync(cancellationToken);
            if (!IsCurrentRead()) return;
            if (gpu is null)
            {
                CustomPerformanceStatusText = "OEM 当前模式未返回 GPU 高级字段；当前预设保持固件只读。";
                return;
            }
            ApplyGpuPerformanceState(gpu);
            CustomPerformanceStatusText = "已读取当前 OEM 模式的 GPU 字段，仅供参考；高级参数仅在自定义模式可调整。";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (!IsCurrentRead()) return;
            CustomPerformanceStatusText = $"高级参数读取失败：{exception.Message}";
        }
        finally
        {
            if (readRevision == Volatile.Read(ref customPerformanceReadRevision))
            {
                isReadingCustomPerformance = false;
                NotifyCustomPerformanceAvailability();
            }
        }
    }

    public async Task ApplyCustomPerformanceSettingAsync(CustomPerformanceSetting setting, int value, CancellationToken cancellationToken)
        => await RunProfileEditAsync(() => ApplyCustomPerformanceSettingCoreAsync(setting, value, cancellationToken), cancellationToken);

    private async Task ApplyCustomPerformanceSettingCoreAsync(CustomPerformanceSetting setting, int value, CancellationToken cancellationToken)
    {
        if (!CanEditAdvancedPerformance || customPerformanceController is null) return;
        if ((setting is CustomPerformanceSetting.GpuTgpWatts or CustomPerformanceSetting.GpuDynamicBoostEnabled or CustomPerformanceSetting.GpuDynamicBoostWatts) && !CanEditGpuPower) return;
        if ((setting is CustomPerformanceSetting.GpuCoreOffsetMhz or CustomPerformanceSetting.GpuMemoryOffsetMhz) && !CanEditGpuClockOffsets) return;
        var profileIndex = SelectedCustomProfileIndex;
        var targetSelection = CurrentPlatformSelection();
        var requestRevision = Volatile.Read(ref platformRequestRevision);
        var draftBeforeApply = CustomPerformanceDraftValue(setting);
        bool IsCurrentTarget() => !isShuttingDown && IsCustomProfileSelected &&
            profileIndex == SelectedCustomProfileIndex && targetSelection == CurrentPlatformSelection() &&
            requestRevision == Volatile.Read(ref platformRequestRevision);
        if (!await PauseDynamicForManualControlAsync() || !CanEditAdvancedPerformance || profileIndex != SelectedCustomProfileIndex) return;
        isApplyingCustomPerformance = true;
        NotifyCustomPerformanceAvailability();
        try
        {
            var result = await customPerformanceController.ApplySettingAsync(profileIndex, setting, value, cancellationToken);
            if (!IsCurrentTarget()) return;
            CustomPerformanceStatusText = result.Reason;
            StatusMessage = result.Reason;
            if (IsGpuPowerSetting(setting)) GpuPowerStatusText = result.Reason;
            await RefreshEditedGpuFieldAsync(setting, draftBeforeApply, IsCurrentTarget, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (!IsCurrentTarget()) return;
            CustomPerformanceStatusText = $"单项参数写入失败：{exception.Message}";
            StatusMessage = CustomPerformanceStatusText;
            if (IsGpuPowerSetting(setting)) GpuPowerStatusText = CustomPerformanceStatusText;
            try { await RefreshEditedGpuFieldAsync(setting, draftBeforeApply, IsCurrentTarget, cancellationToken); }
            catch (Exception readFailure) when (readFailure is not OperationCanceledException)
            {
                if (IsCurrentTarget())
                {
                    CustomPerformanceStatusText += " · 无法回读实际值，请重新读取。";
                    if (IsGpuPowerSetting(setting)) GpuPowerStatusText = CustomPerformanceStatusText;
                }
            }
        }
        finally
        {
            isApplyingCustomPerformance = false;
            NotifyCustomPerformanceAvailability();
        }
    }

    private static bool IsGpuPowerSetting(CustomPerformanceSetting setting) => setting is
        CustomPerformanceSetting.GpuTgpWatts or CustomPerformanceSetting.GpuDynamicBoostEnabled or CustomPerformanceSetting.GpuDynamicBoostWatts;

    private int? CustomPerformanceDraftValue(CustomPerformanceSetting setting) => setting switch
    {
        CustomPerformanceSetting.GpuOverclockingEnabled => CustomOverclockingEnabled ? 1 : 0,
        CustomPerformanceSetting.GpuTgpWatts => CustomGpuTgpWatts,
        CustomPerformanceSetting.GpuDynamicBoostEnabled => CustomGpuDynamicBoostEnabled ? 1 : 0,
        CustomPerformanceSetting.GpuDynamicBoostWatts => CustomGpuDynamicBoostWatts,
        CustomPerformanceSetting.GpuCoreOffsetMhz => CustomGpuCoreOffsetMhz,
        CustomPerformanceSetting.GpuMemoryOffsetMhz => CustomGpuMemoryOffsetMhz,
        _ => null
    };

    private async Task RefreshEditedGpuFieldAsync(CustomPerformanceSetting setting, int? submittedDraft,
        Func<bool> isCurrentTarget, CancellationToken token)
    {
        if (submittedDraft is null || customPerformanceController is null) return;
        if (setting == CustomPerformanceSetting.GpuOverclockingEnabled)
        {
            var coreDraft = CustomGpuCoreOffsetMhz;
            var memoryDraft = CustomGpuMemoryOffsetMhz;
            var readback = await customPerformanceController.ReadGpuClockOffsetsAsync(token);
            if (!isCurrentTarget() || CustomPerformanceDraftValue(setting) != submittedDraft) return;
            var coreChanged = CustomGpuCoreOffsetMhz != coreDraft;
            var memoryChanged = CustomGpuMemoryOffsetMhz != memoryDraft;
            var latestCore = CustomGpuCoreOffsetMhz;
            var latestMemory = CustomGpuMemoryOffsetMhz;
            ApplyGpuClockOffsetReadback(readback);
            if (coreChanged) CustomGpuCoreOffsetMhz = latestCore;
            if (memoryChanged) CustomGpuMemoryOffsetMhz = latestMemory;
            return;
        }
        var live = await customPerformanceController.ReadGpuAsync(token);
        if (live is null || !isCurrentTarget() || CustomPerformanceDraftValue(setting) != submittedDraft) return;
        // Synchronize only this submitted field. A later draft belongs to the next operation.
        switch (setting)
        {
            case CustomPerformanceSetting.GpuTgpWatts: CustomGpuTgpWatts = live.GpuConfigurableTgpWatts; break;
            case CustomPerformanceSetting.GpuDynamicBoostEnabled: CustomGpuDynamicBoostEnabled = live.GpuDynamicBoostEnabled; break;
            case CustomPerformanceSetting.GpuDynamicBoostWatts: CustomGpuDynamicBoostWatts = live.GpuDynamicBoostWatts; break;
            case CustomPerformanceSetting.GpuCoreOffsetMhz: CustomGpuCoreOffsetMhz = live.GpuCoreClockOffsetMhz; break;
            case CustomPerformanceSetting.GpuMemoryOffsetMhz: CustomGpuMemoryOffsetMhz = live.GpuMemoryClockOffsetMhz; break;
        }
    }

    private void NotifyCustomPerformanceAvailability()
    {
        OnPropertyChanged(nameof(CanEditFanSettings));
        OnPropertyChanged(nameof(IsFanApplyEnabled));
        OnPropertyChanged(nameof(CanApplyCustomPerformance));
        OnPropertyChanged(nameof(CanEditAdvancedPerformance));
        OnPropertyChanged(nameof(CanEditGpuPower));
        OnPropertyChanged(nameof(CanEditGpuClockOffsets));
    }

    public async Task ApplyCustomPerformanceAsync(CancellationToken cancellationToken)
        => await RunProfileEditAsync(() => ApplyCustomPerformanceCoreAsync(cancellationToken), cancellationToken);

    private async Task ApplyCustomPerformanceCoreAsync(CancellationToken cancellationToken)
    {
        var targetSelection = CurrentPlatformSelection();
        var profileIndex = SelectedCustomProfileIndex;
        bool IsCurrentTarget() => !isShuttingDown && IsCustomProfileSelected &&
            profileIndex == SelectedCustomProfileIndex && targetSelection == CurrentPlatformSelection();
        var state = new CustomPerformanceState(
            CustomOverclockingEnabled,
            CustomCpuTemperatureWallEnabled,
            CustomCpuTemperatureWallC,
            CustomCpuBurstPowerLimitWatts,
            CustomCpuAdvancedMenuEnabled,
            CustomGpuTgpWatts,
            CustomGpuDynamicBoostEnabled,
            CustomGpuDynamicBoostWatts,
            CustomGpuCoreOffsetMhz,
            CustomGpuMemoryOffsetMhz);
        if (!await PauseDynamicForManualControlAsync() || isShuttingDown ||
            profileIndex != SelectedCustomProfileIndex || targetSelection != CurrentPlatformSelection()) return;
        if (!IsCustomProfileSelected)
        {
            CustomPerformanceStatusText = "OEM 预设的 CPU / GPU 高级参数由固件锁定；仅在自定义模式允许调整。";
            StatusMessage = CustomPerformanceStatusText;
            return;
        }
        if (!CanApplyCustomPerformance || customPerformanceController is null) return;
        isApplyingCustomPerformance = true;
        OnPropertyChanged(nameof(CanApplyCustomPerformance));
        OnPropertyChanged(nameof(CanEditAdvancedPerformance));
        try
        {
            var result = await customPerformanceController.ApplyAsync(
                profileIndex, state, cancellationToken);
            if (!IsCurrentTarget()) return;
            CustomPerformanceStatusText = result.Reason;
            StatusMessage = result.Reason;
            if (result.Succeeded)
            {
                var readback = await customPerformanceController.ReadAsync(profileIndex, cancellationToken);
                if (!IsCurrentTarget() || readback is null) return;
                if (readback == state)
                    ApplyCustomPerformanceState(readback);
                else
                {
                    CustomPerformanceStatusText = "命令已提交；OEM 状态尚未同步，保留本次输入值。";
                    StatusMessage = CustomPerformanceStatusText;
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (!IsCurrentTarget()) return;
            CustomPerformanceStatusText = $"高级参数写入失败：{exception.Message}";
        }
        finally
        {
            isApplyingCustomPerformance = false;
            OnPropertyChanged(nameof(CanApplyCustomPerformance));
            OnPropertyChanged(nameof(CanEditAdvancedPerformance));
        }
    }

    public async Task ApplyGpuClockOffsetsAsync(CancellationToken cancellationToken)
        => await RunProfileEditAsync(() => ApplyGpuClockOffsetsCoreAsync(cancellationToken), cancellationToken);

    private async Task ApplyGpuClockOffsetsCoreAsync(CancellationToken cancellationToken)
    {
        var targetSelection = CurrentPlatformSelection();
        var profileIndex = SelectedCustomProfileIndex;
        bool IsCurrentTarget() => !isShuttingDown && IsCustomProfileSelected &&
            profileIndex == SelectedCustomProfileIndex && targetSelection == CurrentPlatformSelection();
        if (!IsCustomProfileSelected || !CustomOverclockingEnabled || !CanEditGpuClockOffsets ||
            customPerformanceController is null) return;
        var target = new GpuClockOffsets(CustomGpuCoreOffsetMhz, CustomGpuMemoryOffsetMhz);
        if (!await PauseDynamicForManualControlAsync()) return;
        if (!IsCurrentTarget() || !CustomOverclockingEnabled || !CanEditGpuClockOffsets) return;
        isApplyingCustomPerformance = true;
        OnPropertyChanged(nameof(CanEditAdvancedPerformance));
        OnPropertyChanged(nameof(CanEditGpuClockOffsets));
        try
        {
            var result = await customPerformanceController.ApplyGpuClockOffsetsAsync(profileIndex, target, cancellationToken);
            if (!IsCurrentTarget()) return;
            CustomPerformanceStatusText = result.Reason;
            StatusMessage = result.Reason;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (!IsCurrentTarget()) return;
            CustomPerformanceStatusText = $"GPU 频率偏移写入失败：{exception.Message}";
            StatusMessage = CustomPerformanceStatusText;
        }
        finally
        {
            isApplyingCustomPerformance = false;
            OnPropertyChanged(nameof(CanEditAdvancedPerformance));
            OnPropertyChanged(nameof(CanEditGpuClockOffsets));
        }
    }

    private void ApplyCustomPerformanceState(CustomPerformanceState state)
    {
        CustomOverclockingEnabled = state.OverclockingEnabled;
        CustomCpuTemperatureWallEnabled = state.CpuTemperatureWallEnabled;
        CustomCpuTemperatureWallC = state.CpuTemperatureWallC;
        CustomCpuBurstPowerLimitWatts = state.CpuBurstPowerLimitWatts;
        CustomCpuAdvancedMenuEnabled = state.CpuAdvancedMenuEnabled;
        CustomGpuTgpWatts = state.GpuConfigurableTgpWatts;
        CustomGpuDynamicBoostEnabled = state.GpuDynamicBoostEnabled;
        CustomGpuDynamicBoostWatts = state.GpuDynamicBoostWatts;
        CustomGpuCoreOffsetMhz = state.GpuCoreClockOffsetMhz;
        CustomGpuMemoryOffsetMhz = state.GpuMemoryClockOffsetMhz;
    }

    private void ApplyGpuPerformanceState(GpuPerformanceState state)
    {
        CustomOverclockingEnabled = state.OverclockingEnabled;
        CustomGpuTgpWatts = state.GpuConfigurableTgpWatts;
        CustomGpuDynamicBoostEnabled = state.GpuDynamicBoostEnabled;
        CustomGpuDynamicBoostWatts = state.GpuDynamicBoostWatts;
        CustomGpuCoreOffsetMhz = state.GpuCoreClockOffsetMhz;
        CustomGpuMemoryOffsetMhz = state.GpuMemoryClockOffsetMhz;
    }

    public void OpenFanCurve()
    {
        ActiveDrawer = HomeDrawer.Fan;
        StatusMessage = fanDraft is null
            ? "风扇抽拉面板已展开 · 正在等待读取"
            : fanController is null
                ? "风扇曲线已读取 · 当前控制器不可用"
                : "风扇曲线已读取 · 可应用并回读确认";
    }

    private void ApplyFanResponse(FanResponseSettings response)
    {
        isFanIndependentOutputEnabled = response.IndependentCpuGpuControl;
        isFanTransitionSensitivityEnabled = response.TransitionSensitivityEnabled;
        fanTransitionSensitivity = Math.Clamp(response.TransitionSensitivity,
            FanResponseSettings.MinimumTransitionDelayMilliseconds,
            FanResponseSettings.MaximumTransitionDelayMilliseconds);
        if (fanDraft is not null)
        {
            fanDraft = fanDraft with { IndependentCpuGpuControl = isFanIndependentOutputEnabled };
            fanDraftsByProfile[CurrentFanCurveKey] = fanDraft;
        }
        OnPropertyChanged(nameof(IsFanIndependentOutputEnabled));
        OnPropertyChanged(nameof(IsFanTransitionSensitivityEnabled));
        OnPropertyChanged(nameof(FanTransitionSensitivity));
        OnPropertyChanged(nameof(FanTransitionSensitivityText));
    }

    private void SetFanBusy(bool value)
    {
        if (isFanBusy == value) return;
        isFanBusy = value;
        OnPropertyChanged(nameof(CanEditFanSettings));
        OnPropertyChanged(nameof(IsFanApplyEnabled));
    }

    public async Task OpenGpuOutputAsync(CancellationToken cancellationToken)
    {
        ActiveDrawer = HomeDrawer.GpuOutput;
        await InitializeGpuOutputModeAsync(cancellationToken);
    }

    public async Task OpenLightingAsync(CancellationToken cancellationToken)
    {
        ActiveDrawer = HomeDrawer.Lighting;
        if (isReadingLighting) return;
        if (quickControlsController is null)
        {
            LightingStatusText = "官方灯光控制通道未接入。";
            return;
        }

        isReadingLighting = true;
        try
        {
            var reads = Enum.GetValues<LightingZone>().Select(async zone =>
            {
                try
                {
                    return (Zone: zone, State: await quickControlsController.ReadLightingZoneAsync(zone, cancellationToken));
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    return (Zone: zone, State: (LightingZoneState?)new(false, exception.Message, null, []));
                }
            });
            var results = await Task.WhenAll(reads);
            foreach (var (zone, state) in results)
            {
                ApplyLightingZoneState(zone, state ?? new(false, null, null, []));
            }
            // Older keyboards can report a plain backlight switch without a detailed RGB zone.
            // Discover that switch independently; missing RGB state must not remove its sole power control.
            if (!IsKeyboardLightingZoneAvailable)
            {
                try
                {
                    ApplyKeyboardLighting(await quickControlsController.ReadKeyboardLightingAsync(cancellationToken));
                }
                catch (Exception exception) when (exception is not OperationCanceledException) { }
            }
            if (lightingZoneStates.GetValueOrDefault(LightingZone.Keyboard)?.Configuration is { } keyboard)
                IsKeyboardLightingEnabled = keyboard.Enabled;
            NotifyLightingCapabilitiesChanged();
            var firstAvailable = Enum.GetValues<LightingZone>().FirstOrDefault(IsLightingZoneAvailable);
            SelectedLightingZone = IsLightingZoneAvailable(SelectedLightingZone) ? SelectedLightingZone : firstAvailable;
            LoadLightingEditor(SelectedLightingZone);
            var count = lightingZoneStates.Values.Count(state => state.Available);
            LightingStatusText = count == 0
                ? "未收到灯光状态；请确认 GCUBridge 正在运行后重试。"
                : $"已发现 {count} 个灯光分区；每个标签只控制对应硬件，键盘方案不会联动其它灯区。";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LightingStatusText = $"读取灯光能力失败：{exception.Message}";
        }
        finally
        {
            isReadingLighting = false;
        }
    }

    public void ApplyLightingZoneState(LightingZone zone, LightingZoneState state)
    {
        if (lightingLocalTargets.TryGetValue(zone, out var localTarget))
        {
            if (utcNow() >= localTarget.ExpiresAt)
            {
                lightingLocalTargets.Remove(zone);
            }
            else if (!state.HasDetailedConfiguration ||
                     !LightingConfigurationsMatch(state.Configuration, localTarget.Configuration))
            {
                var previous = lightingZoneStates.GetValueOrDefault(zone);
                lightingZoneStates[zone] = LightingStateAccumulator.Merge(previous, state)! with
                {
                    HardwareType = state.HardwareType ?? previous?.HardwareType,
                    Configuration = localTarget.Configuration,
                    SupportedEffects = state.SupportedEffects.Count > 0
                        ? state.SupportedEffects
                        : previous?.SupportedEffects ?? [localTarget.Configuration.Effect]
                };
                if (zone == LightingZone.Keyboard)
                    IsKeyboardLightingEnabled = localTarget.Configuration.Enabled;
                NotifyLightingCapabilitiesChanged();
                return;
            }
            // Keep the local target for the complete settlement window. One matching acknowledgement
            // does not prove that an older queued MQTT packet cannot still arrive afterward.
        }

        var previousState = lightingZoneStates.GetValueOrDefault(zone);
        state = LightingStateAccumulator.Merge(previousState, state)!;
        lightingZoneStates[zone] = state;
        if (zone == LightingZone.Keyboard && state.Configuration is { } keyboard)
            IsKeyboardLightingEnabled = keyboard.Enabled;
        NotifyLightingCapabilitiesChanged();
        if (SelectedLightingZone != zone) return;

        // A user edit is intent, not hardware evidence. Keep it intact until that exact
        // immutable request completes; an older acknowledgement must not edit the next request.
        if (lightingEditorDrafts.ContainsKey(zone)) return;

        LoadLightingEditor(zone);
        LightingStatusText = $"{LightingZoneLabel(zone)}已通过 GCU 状态事件同步。";
    }

    public async Task OpenKeyboardControlsAsync(CancellationToken cancellationToken)
    {
        ActiveDrawer = HomeDrawer.Keyboard;
        if (quickControlsController is null)
        {
            KeyboardControlsStatusText = "官方快捷控制通道未接入。";
            return;
        }

        var failures = new List<string>();
        var readAny = await ReadQuickControlPartAsync("按键状态",
            () => quickControlsController.ReadShortcutsAsync(cancellationToken), ApplyShortcuts, failures, cancellationToken);
        readAny |= await ReadQuickControlPartAsync("设备开关状态",
            () => quickControlsController.ReadSystemTogglesAsync(cancellationToken), ApplySystemToggles, failures, cancellationToken);
        readAny |= await ReadQuickControlPartAsync("键盘灯光状态",
            () => quickControlsController.ReadKeyboardLightingAsync(cancellationToken), ApplyKeyboardLighting, failures, cancellationToken);
        KeyboardControlsStatusText = failures.Count == 0
            ? "Windows 通用开关与官方按键状态已读取。"
            : (readAny ? "部分状态读取失败，已保留已确认状态：" : "按键与设备状态读取失败：") + string.Join("；", failures);
    }

    private static async Task<bool> ReadQuickControlPartAsync<T>(
        string label, Func<ValueTask<T?>> read, Action<T?> apply, List<string> failures,
        CancellationToken cancellationToken) where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var state = await read();
            if (state is null)
            {
                failures.Add($"{label}未返回可识别状态。");
                return false;
            }
            apply(state);
            if (state is SystemToggleState toggles) failures.AddRange(toggles.ReadWarnings);
            if (state is PowerSettingsState power) failures.AddRange(power.ReadWarnings);
            return state switch
            {
                SystemToggleState value => value.WifiEnabled.HasValue || value.BluetoothEnabled.HasValue ||
                    value.TouchpadEnabled.HasValue || value.WebcamEnabled.HasValue || value.CopilotKeyEnabled.HasValue,
                PowerSettingsState value => value.UsbChargingWhileOffEnabled.HasValue || value.SupportsAcAutoBoot ||
                    value.AcAutoBootEnabled.HasValue || value.HighPerformancePowerModeEnabled.HasValue ||
                    value.DeepSleepEnabled.HasValue || value.DeepSleepMinutes.HasValue || value.BatteryChargeLimitPercent.HasValue,
                _ => true
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            failures.Add($"{label}读取失败：{exception.Message}");
            return false;
        }
    }

    private static string AppendQuickControlWarnings(string message, IEnumerable<string> warnings)
    {
        var additional = warnings.Distinct(StringComparer.Ordinal)
            .Where(warning => !message.Contains(warning, StringComparison.Ordinal)).ToArray();
        return additional.Length == 0 ? message : $"{message} 部分状态未读取：{string.Join("；", additional)}";
    }

    public async Task OpenLiquidCoolingAsync(CancellationToken cancellationToken)
    {
        ActiveDrawer = HomeDrawer.LiquidCooling;
        if (quickControlsController is null)
        {
            LiquidCoolingStatusText = "官方快捷控制通道未接入。";
            return;
        }

        await liquidCoolingOperationGate.WaitAsync(cancellationToken);
        IsLiquidCoolingBusy = true;
        try
        {
            var revision = liquidCoolingSelectionRevision;
            var state = await quickControlsController.ReadLiquidCoolingAsync(cancellationToken);
            if (revision != liquidCoolingSelectionRevision || isShuttingDown) return;
            ApplyLiquidCooling(state);
            LiquidCoolingStatusText = IsLiquidCoolingConnected
                ? "水冷已连接，已读取风扇、水泵与散热策略状态。"
                : "未检测到连接的官方水冷设备。";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LiquidCoolingStatusText = $"读取水冷状态失败：{exception.Message}";
        }
        finally
        {
            IsLiquidCoolingBusy = false;
            liquidCoolingOperationGate.Release();
        }
    }

    public async Task RefreshLiquidCoolingStatusAsync(CancellationToken cancellationToken)
    {
        if (quickControlsController is null || isShuttingDown || IsLiquidCoolingBusy || isRefreshingLiquidCoolingStatus
            || !await liquidCoolingOperationGate.WaitAsync(0, cancellationToken)) return;
        isRefreshingLiquidCoolingStatus = true;
        try
        {
            var revision = liquidCoolingSelectionRevision;
            var state = await quickControlsController.ReadLiquidCoolingAsync(cancellationToken);
            if (revision == liquidCoolingSelectionRevision && !isShuttingDown) ApplyLiquidCooling(state);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            StatusMessage = $"水冷状态刷新失败：{exception.Message}";
        }
        finally
        {
            isRefreshingLiquidCoolingStatus = false;
            liquidCoolingOperationGate.Release();
        }
    }

    public async Task<ControlResult> TryAutoConnectLiquidCoolingAsync(CancellationToken cancellationToken)
    {
        if (!IsLiquidCoolingAutoConnectEnabled)
            return ControlResult.Rejected("水冷自动连接已关闭。");
        if (quickControlsController is null)
            return ControlResult.Rejected("官方水冷控制通道未接入。");
        if (isShuttingDown || liquidCoolingStartupAutoConnectSuppressed || isAutoConnectingLiquidCooling || IsLiquidCoolingPriming)
            return ControlResult.Rejected("水冷自动连接当前不可执行。");

        var revision = liquidCoolingSelectionRevision;
        await liquidCoolingOperationGate.WaitAsync(cancellationToken);
        isAutoConnectingLiquidCooling = true;
        using var connecting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, liquidCoolingLifetime.Token);
        liquidCoolingConnectCancellation = connecting;
        IsLiquidCoolingBusy = true;
        try
        {
            if (isShuttingDown || revision != liquidCoolingSelectionRevision)
                return ControlResult.Rejected("水冷自动连接已取消。");
            var connector = new LiquidCoolingAutoConnector(
                quickControlsController.ReadLiquidCoolingAsync,
                quickControlsController.ConnectLiquidCoolingAsync,
                verificationDelay);
            var result = await connector.TryConnectAsync(connecting.Token);
            if (revision != liquidCoolingSelectionRevision) return ControlResult.Rejected("水冷自动连接已取消。");
            LiquidCoolingStatusText = result.Succeeded
                ? $"自动连接：{result.Reason}"
                : result.Reason;

            if (result.Succeeded)
            {
                var state = await quickControlsController.ReadLiquidCoolingAsync(connecting.Token);
                if (revision == liquidCoolingSelectionRevision && !isShuttingDown) RememberLiquidCoolingConnection(state);
            }

            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return ControlResult.Rejected("水冷自动连接已取消。"); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var result = ControlResult.Rejected($"水冷自动连接失败：{exception.Message}");
            LiquidCoolingStatusText = result.Reason;
            return result;
        }
        finally
        {
            IsLiquidCoolingBusy = false;
            isAutoConnectingLiquidCooling = false;
            if (ReferenceEquals(liquidCoolingConnectCancellation, connecting)) liquidCoolingConnectCancellation = null;
            liquidCoolingOperationGate.Release();
        }
    }

    public async Task ScanLiquidCoolingDevicesAsync(CancellationToken cancellationToken)
    {
        if (quickControlsController is null || isShuttingDown) return;
        CancelLiquidCoolingRecovery();
        await liquidCoolingOperationGate.WaitAsync(cancellationToken);
        IsLiquidCoolingBusy = true;
        LiquidCoolingStatusText = "正在通过机械革命 GCU 扫描兼容蓝牙水冷设备…";
        try
        {
            if (isShuttingDown) return;
            var scanned = await quickControlsController.ScanLiquidCoolingDevicesAsync(cancellationToken);
            ApplyLiquidCooling(scanned);
            LiquidCoolingStatusText = LiquidCoolingDevices.Count == 0
                ? "暂未发现兼容水冷；请确认水冷已通电并靠近电脑，然后重新扫描。"
                : scanned?.Connected == true ? "水冷已连接，已读取设备状态。"
                : scanned?.Devices.Count == 0 ? "GCU 已记住此设备，但扫描列表暂为空；可以点击连接重试。"
                : $"发现 {LiquidCoolingDevices.Count} 个兼容水冷设备，请选择后连接。";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LiquidCoolingStatusText = $"扫描水冷设备失败：{exception.Message}";
        }
        finally
        {
            IsLiquidCoolingBusy = false;
            liquidCoolingOperationGate.Release();
        }
    }

    public async Task ConnectLiquidCoolingAsync(CancellationToken cancellationToken)
    {
        if (quickControlsController is null || isShuttingDown || string.IsNullOrWhiteSpace(SelectedLiquidCoolingDeviceId)) return;
        CancelLiquidCoolingRecovery();
        var device = SelectedLiquidCoolingDeviceId;
        var revision = liquidCoolingSelectionRevision;
        await liquidCoolingOperationGate.WaitAsync(cancellationToken);
        using var connecting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, liquidCoolingLifetime.Token);
        liquidCoolingConnectCancellation = connecting;
        IsLiquidCoolingBusy = true;
        try
        {
            if (isShuttingDown || revision != liquidCoolingSelectionRevision) return;
            var selection = await quickControlsController.SelectLiquidCoolingDeviceAsync(device, connecting.Token);
            if (!selection.Succeeded)
            {
                LiquidCoolingStatusText = selection.Reason;
                return;
            }
            if (revision != liquidCoolingSelectionRevision) return;
            liquidCoolingSelectionDraft = false;
            var result = await quickControlsController.ConnectLiquidCoolingAsync(connecting.Token);
            if (revision != liquidCoolingSelectionRevision) return;
            LiquidCoolingStatusText = result.Reason;
            StatusMessage = result.Reason;
            if (result.Succeeded)
            {
                var state = await quickControlsController.ReadLiquidCoolingAsync(connecting.Token);
                if (revision == liquidCoolingSelectionRevision && !isShuttingDown) RememberLiquidCoolingConnection(state);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LiquidCoolingStatusText = $"连接水冷失败：{exception.Message}";
        }
        finally
        {
            IsLiquidCoolingBusy = false;
            if (ReferenceEquals(liquidCoolingConnectCancellation, connecting)) liquidCoolingConnectCancellation = null;
            liquidCoolingOperationGate.Release();
        }
    }

    public Task DisconnectLiquidCoolingAsync(CancellationToken cancellationToken) =>
        RunLiquidCoolingActionAsync(
            controller => controller.DisconnectLiquidCoolingAsync(cancellationToken),
            "断开水冷失败", cancellationToken);

    public Task StartLiquidCoolingPrimingAsync(CancellationToken cancellationToken) =>
        RunLiquidCoolingActionAsync(
            controller => controller.StartLiquidCoolingPrimingAsync(cancellationToken),
            "启动注水模式失败", cancellationToken);

    public Task ResetLiquidCoolingDeviceAsync(CancellationToken cancellationToken) =>
        RunLiquidCoolingActionAsync(
            controller => controller.ResetLiquidCoolingDeviceAsync(cancellationToken),
            "重设水冷失败", cancellationToken);

    public async Task OpenBatteryAsync(CancellationToken cancellationToken)
    {
        ActiveDrawer = HomeDrawer.Battery;
        if (quickControlsController is null)
        {
            BatteryStatusText = "官方快捷控制通道未接入。";
            return;
        }

        var failures = new List<string>();
        var readAny = await ReadQuickControlPartAsync("电池保护状态",
            () => quickControlsController.ReadBatteryProtectionAsync(cancellationToken), ApplyBattery, failures, cancellationToken);
        readAny |= await ReadQuickControlPartAsync("电源设置状态",
            () => quickControlsController.ReadPowerSettingsAsync(cancellationToken), ApplyPowerSettings, failures, cancellationToken);
        BatteryStatusText = failures.Count > 0
            ? (readAny ? "部分状态读取失败，已保留已确认状态：" : "电源与电池状态读取失败：") + string.Join("；", failures)
            : BatteryPercent is { } percent
                ? $"电源与电池状态已读取 · 当前电量 {percent}%"
                : "电源与电池状态已读取。";
    }

    public async Task OpenDisplayAsync(CancellationToken cancellationToken)
    {
        await displayOperationGate.WaitAsync(cancellationToken);
        try { await OpenDisplayCoreAsync(cancellationToken); }
        finally { IsDisplayBusy = false; displayOperationGate.Release(); }
    }

    private async Task OpenDisplayCoreAsync(CancellationToken cancellationToken)
    {
        ActiveDrawer = HomeDrawer.DisplayColor;
        IsDisplayBusy = true;
        var windowsDisplayRead = false;
        try
        {
            if (displayController is not null)
            {
                ApplyDisplaySnapshot(await displayController.ReadAsync(cancellationToken));
                DisplayColorStatusText = "已读取当前显示器、分辨率与可用刷新率。";
                windowsDisplayRead = true;
            }
            else
            {
                DisplayColorStatusText = "Windows 显示控制器未接入。";
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            DisplayColorStatusText = $"读取 Windows 显示设置失败：{exception.Message}";
        }

        if (quickControlsController is not null)
        {
            try
            {
                ApplyDisplayFeatures(await quickControlsController.ReadDisplayFeaturesAsync(cancellationToken));
                if (windowsDisplayRead) DisplayColorStatusText = "显示器、刷新率与 OEM 显示功能状态已读取。";
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                DisplayColorStatusText = $"刷新率已读取；OEM 显示功能状态读取失败：{exception.Message}";
            }
        }
        EnsureSupportedDisplayPageSelected();
        IsDisplayBusy = false;
    }

    public Task OpenDisplayColorAsync(CancellationToken cancellationToken) => OpenDisplayAsync(cancellationToken);

    public void SelectDisplaySettingsPage(DisplaySettingsPage page)
    {
        if (IsDisplayPageSupported(page)) SelectedDisplaySettingsPage = page;
    }

    public async Task RefreshDisplayAsync(CancellationToken cancellationToken)
    {
        if (displayController is null || IsDisplayBusy) return;
        await displayOperationGate.WaitAsync(cancellationToken);
        IsDisplayBusy = true;
        try
        {
            ApplyDisplaySnapshot(await displayController.ReadAsync(cancellationToken));
            DisplayColorStatusText = "显示模式列表已刷新。";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            DisplayColorStatusText = $"刷新显示模式失败：{exception.Message}";
        }
        finally
        {
            IsDisplayBusy = false;
            displayOperationGate.Release();
        }
    }

    public Task SetWinKeyAsync(bool enabled, CancellationToken cancellationToken) =>
        SetShortcutAsync(ShortcutControl.WinKey, enabled, cancellationToken);

    public Task SetFnKeyAsync(bool enabled, CancellationToken cancellationToken) =>
        SetShortcutAsync(ShortcutControl.FnKey, enabled, cancellationToken);

    public Task SetOsdAsync(bool enabled, CancellationToken cancellationToken) =>
        SetShortcutAsync(ShortcutControl.Osd, enabled, cancellationToken);

    public async Task SetSystemToggleAsync(
        SystemToggleControl control, bool enabled, CancellationToken cancellationToken)
    {
        if (quickControlsController is null) return;
        try
        {
            var result = await quickControlsController.SetSystemToggleAsync(control, enabled, cancellationToken);
            KeyboardControlsStatusText = result.Reason;
            StatusMessage = result.Reason;
            if (result.Succeeded)
            {
                // The hardware controller has already waited for authoritative readback.
                // A second immediate GETSTATUS can still expose the preceding cached radio
                // state, especially while Bluetooth is completing its slow transition.
                ApplyConfirmedSystemToggle(control, enabled);
            }
            else
            {
                var failures = new List<string>();
                await ReadQuickControlPartAsync("设备开关状态",
                    () => quickControlsController.ReadSystemTogglesAsync(cancellationToken), ApplySystemToggles, failures, cancellationToken);
                KeyboardControlsStatusText = AppendQuickControlWarnings(result.Reason, failures);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            KeyboardControlsStatusText = $"系统开关切换失败：{exception.Message}";
        }
    }

    public Task SetKeyboardLightingAsync(bool enabled, CancellationToken cancellationToken) =>
        SetKeyboardLightingAsync(CaptureKeyboardLightingPower(enabled), cancellationToken);

    public LightingZoneConfiguration CaptureKeyboardLightingPower(bool enabled)
    {
        var target = BuildLightingDraft("keyboard-power", "keyboard-power").Zones[0] with { Enabled = enabled };
        if (lightingZoneStates.GetValueOrDefault(LightingZone.Keyboard)?.Configuration is null)
            target = target with { Effect = LightingEffect.OemDefault };
        lightingEditorDrafts[LightingZone.Keyboard] = target;
        if (SelectedLightingZone == LightingZone.Keyboard) LoadLightingEditor(LightingZone.Keyboard);
        return target;
    }

    public Task SetKeyboardLightingAsync(LightingZoneConfiguration target, CancellationToken cancellationToken) =>
        SetLightingZonePowerAsync(target, cancellationToken);

    public void SelectLightingZone(LightingZone zone)
    {
        if (IsLightingZoneAvailable(zone)) SelectedLightingZone = zone;
    }

    public void SetLightingColor(string hex)
    {
        if (TryParseLightingColor(hex, out var color)) LightingColorHex = color.Hex;
    }

    public Task ApplyOemDefaultLightingAsync(CancellationToken cancellationToken) =>
        ApplyLightingProfileAsync(LightingProfileCatalog.OemDefault, cancellationToken);

    public Task SetSelectedLightingZonePowerAsync(CancellationToken cancellationToken)
    {
        var target = CaptureLightingEditor();
        return target is null ? Task.CompletedTask : SetLightingZonePowerAsync(target, cancellationToken);
    }

    public async Task SetLightingZonePowerAsync(LightingZoneConfiguration targetConfiguration, CancellationToken cancellationToken)
    {
        if (quickControlsController is null || IsLightingBusy) return;
        var zone = targetConfiguration.Zone;
        var enabled = targetConfiguration.Enabled;
        var useLegacyKeyboardPower = zone == LightingZone.Keyboard &&
            (!IsKeyboardLightingZoneAvailable || IsKeyboardLightingPowerOnly);
        var hardwareTarget = useLegacyKeyboardPower
            ? targetConfiguration with { Effect = LightingEffect.OemDefault }
            : targetConfiguration;
        var previousEnabled = lightingZoneStates.GetValueOrDefault(zone)?.Configuration?.Enabled;
        IsLightingBusy = true;
        try
        {
            ControlResult result;
            pendingLightingConfiguration = targetConfiguration;
            TrackLightingLocalTarget(hardwareTarget);
            if (useLegacyKeyboardPower)
            {
                // Choose one protocol path, never send both the zone command and its legacy equivalent.
                result = await quickControlsController.SetKeyboardLightingAsync(enabled, cancellationToken);
            }
            else if (enabled && (zone != LightingZone.Keyboard || lightingEditorDrafts.ContainsKey(zone)))
            {
                result = await quickControlsController.ApplyLightingZoneAsync(targetConfiguration, cancellationToken);
            }
            else
            {
                result = await quickControlsController.SetLightingZonePowerAsync(zone, enabled, cancellationToken);
            }
            if (result.Succeeded)
            {
                if (useLegacyKeyboardPower)
                    StoreKeyboardLightingPower(hardwareTarget, lightingZoneStates.GetValueOrDefault(zone)?.HardwareType);
                else
                    StoreLightingZoneConfiguration(targetConfiguration);
            }
            if (zone == LightingZone.Keyboard) KeyboardControlsStatusText = result.Reason;
            ReportLightingFeedback(zone,
                lightingEditorDrafts.TryGetValue(zone, out var newer) && newer != targetConfiguration
                    ? $"{LightingZoneLabel(zone)}已保留最新选择，等待应用。"
                    : result.Reason);
            StatusMessage = result.Reason;
            if (result.Succeeded)
            {
                CompleteLightingEditorDraft(targetConfiguration);
                TrackLightingLocalTarget(hardwareTarget);
                if (useLegacyKeyboardPower && SelectedLightingZone == zone) LoadLightingEditor(zone);
            }
            else if (!result.Succeeded)
                lightingLocalTargets.Remove(zone);
            if (!result.Succeeded && previousEnabled is { } value &&
                lightingEditorDrafts.GetValueOrDefault(zone) == targetConfiguration)
            {
                lightingEditorDrafts[zone] = targetConfiguration with { Enabled = value };
                if (SelectedLightingZone == zone) LoadLightingEditor(zone);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            lightingLocalTargets.Remove(zone);
            var reason = $"切换{LightingZoneLabel(zone)}失败：{exception.Message}";
            if (zone == LightingZone.Keyboard) KeyboardControlsStatusText = reason;
            ReportLightingFeedback(zone, reason);
        }
        finally
        {
            pendingLightingConfiguration = null;
            IsLightingBusy = false;
        }
    }

    public Task ApplySelectedLightingZoneAsync(CancellationToken cancellationToken)
    {
        var configuration = CaptureLightingEditor();
        return configuration is null ? Task.CompletedTask : ApplyLightingConfigurationAsync(configuration, cancellationToken);
    }

    public LightingZoneConfiguration? CaptureLightingEditor()
    {
        if (TryBuildSelectedLightingConfiguration(out var configuration)) return configuration;
        LightingStatusText = "颜色格式无效，请输入 #RRGGBB；尚未发送。";
        return null;
    }

    public async Task ApplyLightingConfigurationAsync(
        LightingZoneConfiguration configuration, CancellationToken cancellationToken)
    {
        if (quickControlsController is null || IsLightingBusy) return;
        if (configuration.Zone == LightingZone.Keyboard && IsKeyboardLightingPowerOnly)
        {
            ReportLightingFeedback(configuration.Zone, "当前键盘仅确认支持背光开关，未发送灯效、颜色或亮度配置。");
            return;
        }
        if (!configuration.Enabled)
        {
            StoreLightingZoneConfiguration(configuration);
            ReportLightingFeedback(configuration.Zone,
                $"{LightingZoneLabel(configuration.Zone)}当前已关闭；颜色 {configuration.Color.Hex} 与灯效参数已保存，打开后自动应用。");
            return;
        }
        pendingLightingConfiguration = configuration;
        TrackLightingLocalTarget(configuration);
        IsLightingBusy = true;
        try
        {
            var result = await quickControlsController.ApplyLightingZoneAsync(configuration, cancellationToken);
            if (result.Succeeded)
            {
                StoreLightingZoneConfiguration(configuration);
                CompleteLightingEditorDraft(configuration);
                TrackLightingLocalTarget(configuration);
            }
            else
                lightingLocalTargets.Remove(configuration.Zone);
            ReportLightingFeedback(configuration.Zone,
                lightingEditorDrafts.TryGetValue(configuration.Zone, out var newer) && newer != configuration
                    ? $"{LightingZoneLabel(configuration.Zone)}已保留最新选择，等待应用；上一条反馈不会覆盖编辑值。"
                    : result.Reason);
            StatusMessage = result.Reason;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            lightingLocalTargets.Remove(configuration.Zone);
            ReportLightingFeedback(configuration.Zone, $"应用灯效失败：{exception.Message}");
        }
        finally
        {
            pendingLightingConfiguration = null;
            IsLightingBusy = false;
        }
    }

    private void StoreLightingZoneConfiguration(LightingZoneConfiguration configuration)
    {
        var previous = lightingZoneStates.GetValueOrDefault(configuration.Zone);
        lightingZoneStates[configuration.Zone] = new(
            true,
            previous?.HardwareType,
            configuration,
            previous?.SupportedEffects ?? [configuration.Effect]);
        if (configuration.Zone == LightingZone.Keyboard)
            IsKeyboardLightingEnabled = configuration.Enabled;
    }

    private void RememberLightingEditorDraft()
    {
        if (IsSynchronizingLightingEditor ||
            lightingZoneStates.GetValueOrDefault(SelectedLightingZone)?.Configuration is not { } previous) return;
        var color = TryParseLightingColor(LightingColorHex, out var parsed)
            ? parsed : lightingEditorDrafts.GetValueOrDefault(SelectedLightingZone)?.Color ?? previous.Color;
        lightingEditorDrafts[SelectedLightingZone] = new(SelectedLightingZone, IsSelectedLightingZoneEnabled,
            SelectedLightingEffect, color, LightingBrightness, LightingSpeed, SelectedLightingDirection, LightingSleepSeconds);
        LightingStatusText = IsSelectedLightingZoneEnabled
            ? $"{SelectedLightingZoneLabel}已选择 {color.Hex}；等待自动应用。"
            : $"{SelectedLightingZoneLabel}已保存 {color.Hex}；打开后自动应用。";
    }

    private void ReportLightingFeedback(LightingZone zone, string message)
    {
        lightingZoneFeedback[zone] = message;
        if (SelectedLightingZone == zone) LightingStatusText = message;
    }

    private void CompleteLightingEditorDraft(LightingZoneConfiguration completed)
    {
        if (lightingEditorDrafts.GetValueOrDefault(completed.Zone) == completed)
            lightingEditorDrafts.Remove(completed.Zone);
    }

    private bool TryBuildSelectedLightingConfiguration(out LightingZoneConfiguration configuration)
    {
        if (!TryParseLightingColor(LightingColorHex, out var color))
        {
            configuration = default!;
            return false;
        }

        configuration = new(
            SelectedLightingZone,
            IsSelectedLightingZoneEnabled,
            SelectedLightingEffect,
            color,
            LightingBrightness,
            LightingSpeed,
            SelectedLightingDirection,
            LightingSleepSeconds);

        if (configuration.Enabled &&
            configuration.Zone is LightingZone.CoastLightbar or LightingZone.Logo)
        {
            var normalizedColor = UsesUserLightingColor(configuration.Effect) &&
                                  configuration.Color == new LightingRgb(0, 0, 0)
                ? new LightingRgb(0, 176, 255)
                : configuration.Color;
            configuration = configuration with
            {
                Color = normalizedColor,
                Brightness = configuration.Brightness == 0 ? 1 : configuration.Brightness
            };
            LightingColorHex = configuration.Color.Hex;
            LightingBrightness = configuration.Brightness;
        }
        else if (configuration.Enabled &&
                 configuration.Zone == LightingZone.LiquidCooler &&
                 configuration.Effect == LightingEffect.Static &&
                 configuration.Color == new LightingRgb(0, 0, 0))
        {
            configuration = configuration with { Color = new(0, 176, 255) };
            LightingColorHex = configuration.Color.Hex;
        }
        return true;
    }

    private static bool UsesUserLightingColor(LightingEffect effect) => effect is not (
        LightingEffect.Rainbow or
        LightingEffect.ColorMarquee or
        LightingEffect.Colorful);

    private void TrackLightingLocalTarget(LightingZoneConfiguration configuration) =>
        lightingLocalTargets[configuration.Zone] = new(
            configuration,
            utcNow().AddSeconds(3));

    private static bool LightingConfigurationsMatch(
        LightingZoneConfiguration? actual,
        LightingZoneConfiguration expected)
    {
        if (actual is null || actual.Enabled != expected.Enabled) return false;
        if (!expected.Enabled || expected.Effect == LightingEffect.OemDefault) return true;
        if (actual.Effect != expected.Effect) return false;

        if (expected.Zone == LightingZone.LiquidCooler)
            return expected.Effect != LightingEffect.Static || actual.Color == expected.Color;

        if (actual.Brightness != expected.Brightness || actual.Speed != expected.Speed) return false;
        var expectedDirection = expected.Zone == LightingZone.Keyboard && expected.Effect == LightingEffect.Wave
            ? expected.Direction
            : LightingDirection.None;
        if (actual.Direction != expectedDirection) return false;
        return !UsesUserLightingColor(expected.Effect) || actual.Color == expected.Color;
    }

    public async Task ApplyLightingSleepTimerAsync(CancellationToken cancellationToken)
    {
        if (quickControlsController is null || IsLightingBusy) return;
        IsLightingBusy = true;
        try
        {
            var result = await quickControlsController.SetLightingSleepTimerAsync(
                LightingSleepSeconds, cancellationToken);
            LightingStatusText = result.Succeeded
                ? $"灯效休眠：{result.Reason}"
                : result.Reason;
            StatusMessage = LightingStatusText;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LightingStatusText = $"应用灯效休眠失败：{exception.Message}";
        }
        finally
        {
            IsLightingBusy = false;
        }
    }

    public async Task ApplyLightingProfileAsync(LightingProfile profile, CancellationToken cancellationToken)
    {
        if (quickControlsController is null || IsLightingBusy) return;
        if (!LightingProfileCatalog.Validate(profile, out var validationReason))
        {
            LightingStatusText = validationReason;
            return;
        }

        IsLightingBusy = true;
        var applied = 0;
        var skipped = 0;
        var lastReason = string.Empty;
        LightingZoneConfiguration? pendingProfileConfiguration = null;
        try
        {
            foreach (var configuration in profile.Zones)
            {
                if (!IsLightingZoneAvailable(configuration.Zone) ||
                    configuration.Zone == LightingZone.Keyboard && IsKeyboardLightingPowerOnly)
                {
                    skipped++;
                    continue;
                }
                var capability = lightingZoneStates[configuration.Zone];
                if (configuration.Effect != LightingEffect.OemDefault &&
                    !capability.SupportedEffects.Contains(configuration.Effect))
                {
                    skipped++;
                    continue;
                }
                pendingProfileConfiguration = configuration;
                lightingEditorDrafts[configuration.Zone] = configuration;
                if (SelectedLightingZone == configuration.Zone) LoadLightingEditor(configuration.Zone);
                TrackLightingLocalTarget(configuration);
                var result = await quickControlsController.ApplyLightingZoneAsync(configuration, cancellationToken);
                lastReason = result.Reason;
                if (!result.Succeeded)
                {
                    lightingLocalTargets.Remove(configuration.Zone);
                    if (lightingEditorDrafts.GetValueOrDefault(configuration.Zone) == configuration)
                        lightingEditorDrafts.Remove(configuration.Zone);
                    skipped++;
                    pendingProfileConfiguration = null;
                    continue;
                }
                applied++;
                lightingZoneStates[configuration.Zone] = capability with { Configuration = configuration };
                CompleteLightingEditorDraft(configuration);
                TrackLightingLocalTarget(configuration);
                pendingProfileConfiguration = null;
            }
            importedLightingProfile = profile;
            LightingStatusText = applied > 0
                ? $"{profile.Name}键盘方案已应用并回读。"
                : string.IsNullOrWhiteSpace(lastReason) ? $"{profile.Name}没有可应用的已发现分区。" : lastReason;
            StatusMessage = LightingStatusText;
            LoadLightingEditor(SelectedLightingZone);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (pendingProfileConfiguration is { } failedConfiguration)
            {
                lightingLocalTargets.Remove(failedConfiguration.Zone);
                if (lightingEditorDrafts.GetValueOrDefault(failedConfiguration.Zone) == failedConfiguration)
                    lightingEditorDrafts.Remove(failedConfiguration.Zone);
                if (SelectedLightingZone == failedConfiguration.Zone)
                    LoadLightingEditor(failedConfiguration.Zone);
            }
            LightingStatusText = $"应用灯光方案失败：{exception.Message}";
        }
        finally
        {
            IsLightingBusy = false;
        }
    }

    public void SaveMyLightingProfile()
    {
        try
        {
            var profile = BuildLightingDraft("my-lighting", "我的灯光方案");
            lightingProfileStore.Save(profile);
            importedLightingProfile = profile;
            LightingStatusText = "我的灯光方案已保存；可继续导出为 .revolight.json 分享。";
        }
        catch (Exception exception)
        {
            LightingStatusText = $"保存灯光方案失败：{exception.Message}";
        }
    }

    public void ImportLightingProfile(string path)
    {
        try
        {
            importedLightingProfile = lightingProfileStore.Import(path);
            var keyboard = importedLightingProfile.Zones[0];
            SelectedLightingZone = LightingZone.Keyboard;
            LoadLightingConfiguration(keyboard,
                lightingZoneStates.GetValueOrDefault(LightingZone.Keyboard)?.SupportedEffects ?? [keyboard.Effect]);
            LightingStatusText = $"已载入 {importedLightingProfile.Name}；确认参数后点击应用并回读。";
        }
        catch (Exception exception)
        {
            LightingStatusText = $"导入灯光方案失败：{exception.Message}";
        }
    }

    public void ExportLightingProfile(string path)
    {
        try
        {
            var profile = importedLightingProfile ?? BuildLightingDraft("my-lighting", "我的灯光方案");
            lightingProfileStore.Export(path, profile);
            LightingStatusText = $"已导出 {profile.Name}。";
        }
        catch (Exception exception)
        {
            LightingStatusText = $"导出灯光方案失败：{exception.Message}";
        }
    }

    public Task ApplyImportedLightingProfileAsync(CancellationToken cancellationToken) =>
        importedLightingProfile is null
            ? Task.CompletedTask
            : ApplyLightingProfileAsync(importedLightingProfile, cancellationToken);

    public async Task SetBatteryProtectionAsync(BatteryProtectionMode mode, CancellationToken cancellationToken)
    {
        if (quickControlsController is null) return;
        try
        {
            var result = await quickControlsController.SetBatteryProtectionAsync(mode, cancellationToken);
            BatteryStatusText = result.Reason;
            StatusMessage = result.Reason;
            if (result.Succeeded) SelectedBatteryProtectionMode = mode;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            BatteryStatusText = $"电池保护切换失败：{exception.Message}";
        }
    }

    public async Task SetPowerToggleAsync(
        PowerToggleControl control, bool enabled, CancellationToken cancellationToken)
    {
        if (quickControlsController is null) return;
        try
        {
            var result = await quickControlsController.SetPowerToggleAsync(control, enabled, cancellationToken);
            BatteryStatusText = result.Reason;
            StatusMessage = result.Reason;
            var failures = new List<string>();
            await ReadQuickControlPartAsync("电源设置状态",
                () => quickControlsController.ReadPowerSettingsAsync(cancellationToken), ApplyPowerSettings, failures, cancellationToken);
            BatteryStatusText = AppendQuickControlWarnings(result.Reason, failures);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            BatteryStatusText = $"电源开关切换失败：{exception.Message}";
        }
    }

    public async Task SetDeepSleepAsync(bool enabled, CancellationToken cancellationToken)
    {
        if (quickControlsController is null) return;
        try
        {
            var result = await quickControlsController.SetDeepSleepAsync(enabled, SelectedDeepSleepMinutes, cancellationToken);
            BatteryStatusText = result.Reason;
            StatusMessage = result.Reason;
            var failures = new List<string>();
            await ReadQuickControlPartAsync("电源设置状态",
                () => quickControlsController.ReadPowerSettingsAsync(cancellationToken), ApplyPowerSettings, failures, cancellationToken);
            BatteryStatusText = AppendQuickControlWarnings(result.Reason, failures);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            BatteryStatusText = $"深度睡眠切换失败：{exception.Message}";
        }
    }

    public async Task SetBatteryChargeLimitAsync(int percent, CancellationToken cancellationToken)
    {
        if (quickControlsController is null) return;
        var previous = BatteryChargeLimitPercent;
        BatteryChargeLimitPercent = percent;
        try
        {
            var result = await quickControlsController.SetBatteryChargeLimitAsync(percent, cancellationToken);
            BatteryStatusText = result.Reason;
            StatusMessage = result.Reason;
            if (result.Succeeded)
            {
                BatteryChargeLimitPercent = percent;
                SupportsBatteryChargeLimit = true;
            }
            else BatteryChargeLimitPercent = previous;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            BatteryChargeLimitPercent = previous;
            BatteryStatusText = $"充电上限设置失败：{exception.Message}";
        }
    }

    public async Task SetDisplayColorAsync(DisplayColorMode mode, CancellationToken cancellationToken)
    {
        if (quickControlsController is null) return;
        try
        {
            var result = await quickControlsController.SetDisplayColorAsync(mode, cancellationToken);
            DisplayColorStatusText = result.Reason;
            StatusMessage = result.Reason;
            if (result.Succeeded) SelectedDisplayColorMode = mode;
            else ReassertDisplayControlBindings();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            DisplayColorStatusText = $"屏幕色彩切换失败：{exception.Message}";
            ReassertDisplayControlBindings();
        }
    }

    public async Task SetDisplayColorManagementAsync(bool enabled, CancellationToken cancellationToken)
    {
        if (quickControlsController is null || !SupportsDisplayColorManagement) return;
        try
        {
            var result = await quickControlsController.SetDisplayColorEnabledAsync(enabled, SelectedDisplayColorMode, cancellationToken);
            DisplayColorStatusText = result.Reason;
            StatusMessage = result.Reason;
            if (result.Succeeded) IsDisplayColorManagementEnabled = enabled;
            else ReassertDisplayControlBindings();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            DisplayColorStatusText = $"色彩管理切换失败：{exception.Message}";
            ReassertDisplayControlBindings();
        }
    }

    public async Task SetMiniLedLocalDimmingAsync(bool enabled, CancellationToken cancellationToken)
    {
        if (quickControlsController is null || !SupportsMiniLedLocalDimming) return;
        try
        {
            var result = await quickControlsController.SetLocalDimmingAsync(enabled, cancellationToken);
            DisplayColorStatusText = result.Reason;
            StatusMessage = result.Reason;
            if (result.Succeeded) IsMiniLedLocalDimmingEnabled = enabled;
            else ReassertDisplayControlBindings();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            DisplayColorStatusText = $"Mini LED 局部调光切换失败：{exception.Message}";
            ReassertDisplayControlBindings();
        }
    }

    public async Task ApplyDisplayRefreshRateAsync(CancellationToken cancellationToken)
    {
        if (!CanApplyDisplayRefreshRate || displayController is null) return;
        var selectedRate = SelectedDisplayRefreshRateHz;
        Interlocked.Increment(ref displayRequestRevision);
        await displayOperationGate.WaitAsync(cancellationToken);
        IsDisplayBusy = true;
        try
        {
            var result = await displayController.SetRefreshRateAsync(selectedRate, cancellationToken);
            DisplayColorStatusText = result.Reason;
            StatusMessage = result.Reason;
            if (result.Succeeded && TrySaveSettings(applicationSettings with
            { PreferredAcRefreshRateHz = selectedRate }))
            {
                OnPropertyChanged(nameof(PreferredAcRefreshRateHz));
            }
            ApplyDisplaySnapshot(await displayController.ReadAsync(cancellationToken));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            DisplayColorStatusText = $"刷新率切换失败：{exception.Message}";
        }
        finally
        {
            IsDisplayBusy = false;
            displayOperationGate.Release();
        }
    }

    public async Task SetAutomaticRefreshRateAsync(bool enabled, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (TrySaveSettings(applicationSettings with { AutomaticRefreshRate = enabled }))
        {
            Interlocked.Increment(ref displayRequestRevision);
            OnPropertyChanged(nameof(IsAutomaticRefreshRateEnabled));
            DisplayColorStatusText = enabled
                ? "自动刷新率已开启：DC 使用最低刷新率，AC 恢复偏好刷新率。"
                : "自动刷新率已关闭。";
            if (!enabled && displayController is not null)
            {
                await displayOperationGate.WaitAsync(cancellationToken);
                try
                {
                    var restored = await displayController.RestoreTemporaryRefreshRateAsync(cancellationToken);
                    DisplayColorStatusText = restored.Reason;
                    ApplyDisplaySnapshot(await displayController.ReadAsync(cancellationToken));
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    DisplayColorStatusText = $"自动刷新率已关闭，但临时状态恢复失败：{exception.Message}";
                }
                finally { displayOperationGate.Release(); }
            }
        }
    }

    public async Task ApplyAutomaticRefreshRateForPowerSourceAsync(bool onAcPower, CancellationToken cancellationToken)
    {
        if (!IsAutomaticRefreshRateEnabled || displayController is null) return;
        var revision = Interlocked.Increment(ref displayRequestRevision);
        await displayOperationGate.WaitAsync(cancellationToken);
        try
        {
            if (!IsAutomaticRefreshRateEnabled || revision != Volatile.Read(ref displayRequestRevision)) return;
            IsDisplayBusy = true;
            var snapshot = await displayController.ReadAsync(cancellationToken);
            ApplyDisplaySnapshot(snapshot);
            if (snapshot.AvailableRefreshRatesHz.Count == 0) return;
            var preferred = PreferredAcRefreshRateHz;
            if (!IsAutomaticRefreshRateEnabled || revision != Volatile.Read(ref displayRequestRevision)) return;
            // Without a valid explicit AC preference, restore our own temporary change;
            // do not choose a new maximum rate on the user's behalf.
            var result = onAcPower && !snapshot.AvailableRefreshRatesHz.Contains(preferred)
                ? await displayController.RestoreTemporaryRefreshRateAsync(cancellationToken)
                : await displayController.SetTemporaryRefreshRateAsync(
                    onAcPower ? preferred : snapshot.AvailableRefreshRatesHz.Min(), cancellationToken);
            DisplayColorStatusText = result.Reason;
            StatusMessage = result.Reason;
            ApplyDisplaySnapshot(await displayController.ReadAsync(cancellationToken));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            DisplayColorStatusText = $"自动刷新率应用失败：{exception.Message}";
        }
        finally
        {
            IsDisplayBusy = false;
            displayOperationGate.Release();
        }
    }

    public async Task SetLiquidCoolingFanAsync(LiquidCoolingFanMode mode, CancellationToken cancellationToken)
    {
        if (quickControlsController is null || isShuttingDown) return;
        await liquidCoolingOperationGate.WaitAsync(cancellationToken);
        try
        {
            if (isShuttingDown) return;
            IsLiquidCoolingBusy = true;
            var result = await quickControlsController.SetLiquidCoolingFanAsync(mode, cancellationToken);
            LiquidCoolingStatusText = result.Reason;
            StatusMessage = result.Reason;
            if (result.Succeeded)
            {
                SelectedLiquidCoolingFanMode = mode;
                ApplyLiquidCooling(await quickControlsController.ReadLiquidCoolingAsync(cancellationToken));
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LiquidCoolingStatusText = $"水冷风扇切换失败：{exception.Message}";
        }
        finally { IsLiquidCoolingBusy = false; liquidCoolingOperationGate.Release(); }
    }

    public async Task SetLiquidCoolingPumpAsync(LiquidCoolingPumpMode mode, CancellationToken cancellationToken)
    {
        if (quickControlsController is null || isShuttingDown) return;
        await liquidCoolingOperationGate.WaitAsync(cancellationToken);
        try
        {
            if (isShuttingDown) return;
            IsLiquidCoolingBusy = true;
            var result = await quickControlsController.SetLiquidCoolingPumpAsync(mode, cancellationToken);
            LiquidCoolingStatusText = result.Reason;
            StatusMessage = result.Reason;
            if (result.Succeeded) SelectedLiquidCoolingPumpMode = mode;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LiquidCoolingStatusText = $"水冷泵切换失败：{exception.Message}";
        }
        finally { IsLiquidCoolingBusy = false; liquidCoolingOperationGate.Release(); }
    }

    private async Task SetShortcutAsync(ShortcutControl control, bool enabled, CancellationToken cancellationToken)
    {
        if (quickControlsController is null) return;
        try
        {
            var result = await quickControlsController.SetShortcutAsync(control, enabled, cancellationToken);
            KeyboardControlsStatusText = result.Reason;
            StatusMessage = result.Reason;
            if (!result.Succeeded) return;
            switch (control)
            {
                case ShortcutControl.WinKey: IsWinKeyEnabled = enabled; break;
                case ShortcutControl.FnKey: IsFnKeyEnabled = enabled; break;
                case ShortcutControl.Osd: IsOsdEnabled = enabled; break;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            KeyboardControlsStatusText = $"按键开关切换失败：{exception.Message}";
        }
    }

    private void ApplyShortcuts(ShortcutState? state)
    {
        if (state is null) return;
        IsWinKeyEnabled = state.WinKeyEnabled;
        IsFnKeyEnabled = state.FnKeyEnabled;
        IsOsdEnabled = state.OsdEnabled;
    }

    private void ApplySystemToggles(SystemToggleState? state)
    {
        if (state is null) return;
        var partial = state.ReadWarnings.Count > 0;
        SupportsWifiToggle = state.WifiEnabled.HasValue || (partial && SupportsWifiToggle);
        SupportsBluetoothToggle = state.BluetoothEnabled.HasValue || (partial && SupportsBluetoothToggle);
        SupportsTouchpadToggle = state.TouchpadEnabled.HasValue || (partial && SupportsTouchpadToggle);
        SupportsWebcamToggle = state.WebcamEnabled.HasValue || (partial && SupportsWebcamToggle);
        SupportsCopilotKeyToggle = state.CopilotKeyEnabled.HasValue || (partial && SupportsCopilotKeyToggle);
        OnPropertyChanged(nameof(SupportsWifiToggle));
        OnPropertyChanged(nameof(SupportsBluetoothToggle));
        OnPropertyChanged(nameof(SupportsTouchpadToggle));
        OnPropertyChanged(nameof(SupportsWebcamToggle));
        OnPropertyChanged(nameof(SupportsCopilotKeyToggle));
        if (state.WifiEnabled is { } wifi) IsWifiEnabled = wifi;
        if (state.BluetoothEnabled is { } bluetooth) IsBluetoothEnabled = bluetooth;
        if (state.TouchpadEnabled is { } touchpad) IsTouchpadEnabled = touchpad;
        if (state.WebcamEnabled is { } webcam) IsWebcamEnabled = webcam;
        if (state.CopilotKeyEnabled is { } copilot) IsCopilotKeyEnabled = copilot;
    }

    private void ApplyConfirmedSystemToggle(SystemToggleControl control, bool enabled)
    {
        switch (control)
        {
            case SystemToggleControl.Wifi: IsWifiEnabled = enabled; break;
            case SystemToggleControl.Bluetooth: IsBluetoothEnabled = enabled; break;
            case SystemToggleControl.Touchpad: IsTouchpadEnabled = enabled; break;
            case SystemToggleControl.Webcam: IsWebcamEnabled = enabled; break;
            case SystemToggleControl.CopilotKey: IsCopilotKeyEnabled = enabled; break;
        }
    }

    private void ApplyKeyboardLighting(KeyboardLightingState? state)
    {
        if (state is null) return;
        IsKeyboardLightingEnabled = state.Enabled;
        if (IsKeyboardLightingZoneAvailable && !IsKeyboardLightingPowerOnly) return;
        var configuration = lightingZoneStates.GetValueOrDefault(LightingZone.Keyboard)?.Configuration ??
            new(LightingZone.Keyboard, state.Enabled, LightingEffect.OemDefault, new(0, 0, 0),
                3, 2, LightingDirection.None, 120);
        StoreKeyboardLightingPower(configuration with { Enabled = state.Enabled }, state.Type);
        if (SelectedLightingZone == LightingZone.Keyboard) LoadLightingEditor(LightingZone.Keyboard);
    }

    private void StoreKeyboardLightingPower(LightingZoneConfiguration configuration, string? hardwareType)
    {
        // A confirmed power acknowledgement is not evidence of RGB configuration support.
        lightingZoneStates[LightingZone.Keyboard] = new(true, hardwareType,
            configuration with { Effect = LightingEffect.OemDefault }, [LightingEffect.OemDefault],
            HasDetailedConfiguration: false, ReportedFields: LightingReportedFields.Power);
        IsKeyboardLightingEnabled = configuration.Enabled;
        NotifyLightingCapabilitiesChanged();
    }

    private void ApplyBattery(BatteryProtectionState? state)
    {
        if (state is null) return;
        SelectedBatteryProtectionMode = state.Mode;
        BatteryPercent = state.BatteryPercent;
    }

    private void ApplyPowerSettings(PowerSettingsState? state)
    {
        if (state is null) return;
        var partial = state.ReadWarnings.Count > 0;
        SupportsUsbChargingWhileOff = state.UsbChargingWhileOffEnabled.HasValue || (partial && SupportsUsbChargingWhileOff);
        SupportsHighPerformancePowerMode = state.HighPerformancePowerModeEnabled.HasValue || (partial && SupportsHighPerformancePowerMode);
        SupportsDeepSleep = state.DeepSleepEnabled.HasValue || (partial && SupportsDeepSleep);
        OnPropertyChanged(nameof(SupportsUsbChargingWhileOff));
        OnPropertyChanged(nameof(SupportsHighPerformancePowerMode));
        OnPropertyChanged(nameof(SupportsDeepSleep));
        if (state.UsbChargingWhileOffEnabled is { } usb) IsUsbChargingWhileOffEnabled = usb;
        if (state.SettingStatusReadSucceeded) SupportsAcAutoBoot = state.SupportsAcAutoBoot;
        if (state.AcAutoBootEnabled is { } ac) IsAcAutoBootEnabled = ac;
        if (state.HighPerformancePowerModeEnabled is { } performance) IsHighPerformancePowerModeEnabled = performance;
        if (state.DeepSleepEnabled is { } deepSleep) IsDeepSleepEnabled = deepSleep;
        if (state.DeepSleepMinutes is { } minutes && minutes is >= 5 and <= 120) SelectedDeepSleepMinutes = minutes;
        if (state.BatteryChargeLimitPercent is { } limit && limit is >= 50 and <= 100)
        {
            BatteryChargeLimitPercent = limit;
            SupportsBatteryChargeLimit = true;
        }
    }

    private void ApplyDisplayFeatures(DisplayFeatureState? state)
    {
        if (state is null) return;
        SupportsDisplayColorManagement = state.SupportsColorCalibration;
        SupportsMiniLedLocalDimming = state.SupportsLocalDimming;
        if (state.ColorCalibrationEnabled is { } colorEnabled) IsDisplayColorManagementEnabled = colorEnabled;
        if (state.ColorMode is { } mode) SelectedDisplayColorMode = mode;
        if (state.LocalDimmingEnabled is { } localDimming) IsMiniLedLocalDimmingEnabled = localDimming;
        EnsureSupportedDisplayPageSelected();
    }

    private void ReassertDisplayControlBindings()
    {
        OnPropertyChanged(nameof(SelectedDisplayColorMode));
        OnPropertyChanged(nameof(IsDisplayColorDefault));
        OnPropertyChanged(nameof(IsDisplayColorSrgb));
        OnPropertyChanged(nameof(IsDisplayColorP3));
        OnPropertyChanged(nameof(IsDisplayColorAdobeRgb));
        OnPropertyChanged(nameof(IsDisplayColorManagementEnabled));
        OnPropertyChanged(nameof(IsMiniLedLocalDimmingEnabled));
    }

    private void ApplyDisplaySnapshot(DisplaySnapshot snapshot)
    {
        DisplayName = snapshot.DisplayName;
        DisplayResolutionText = $"{snapshot.Width} × {snapshot.Height}";
        CurrentDisplayRefreshRateHz = snapshot.CurrentRefreshRateHz;
        var rates = snapshot.AvailableRefreshRatesHz.Distinct().Order().ToArray();
        if (!DisplayRefreshRates.SequenceEqual(rates))
        {
            DisplayRefreshRates.Clear();
            foreach (var rate in rates) DisplayRefreshRates.Add(rate);
        }
        SelectedDisplayRefreshRateHz = snapshot.CurrentRefreshRateHz;
        // A changed mode list clears the control's selection even when the current Hz is unchanged.
        OnPropertyChanged(nameof(SelectedDisplayRefreshRateHz));
        OnPropertyChanged(nameof(CanApplyDisplayRefreshRate));
        OnPropertyChanged(nameof(SupportsDisplayRefreshRate));
        OnPropertyChanged(nameof(DisplaySettingsTabCount));
        EnsureSupportedDisplayPageSelected();
    }

    private bool IsDisplayPageSupported(DisplaySettingsPage page) => page switch
    {
        DisplaySettingsPage.ColorCalibration => SupportsDisplayColorManagement,
        DisplaySettingsPage.DisplayFeatures => SupportsMiniLedLocalDimming,
        DisplaySettingsPage.RefreshRate => SupportsDisplayRefreshRate,
        _ => false
    };

    private void EnsureSupportedDisplayPageSelected()
    {
        if (IsDisplayPageSupported(SelectedDisplaySettingsPage)) return;
        if (SupportsDisplayColorManagement) SelectedDisplaySettingsPage = DisplaySettingsPage.ColorCalibration;
        else if (SupportsMiniLedLocalDimming) SelectedDisplaySettingsPage = DisplaySettingsPage.DisplayFeatures;
        else if (SupportsDisplayRefreshRate) SelectedDisplaySettingsPage = DisplaySettingsPage.RefreshRate;
    }

    private void ApplyLiquidCooling(LiquidCoolingState? state)
    {
        if (state is null) return;
        liquidCoolingReconnect.Observe(state);
        liquidCoolingConnectionStatus = state.ConnectionStatus;
        liquidCoolingFanDutyPercent = state.FanDutyPercent;
        liquidCoolingPumpDutyPercent = state.PumpDutyPercent;
        liquidCoolingFirmwareVersion = state.FirmwareVersion;
        liquidCoolingCanControl = state.CanControl;
        IsLiquidCoolingPriming = state.IsPriming;
        liquidCoolingMeterNormal = state.MeterNormal;
        var previousSelection = SelectedLiquidCoolingDeviceId;
        applyingLiquidCoolingState = true;
        try
        {
            LiquidCoolingDevices.Clear();
            foreach (var device in state.Devices) LiquidCoolingDevices.Add(device.Id);
            if (!string.IsNullOrWhiteSpace(state.SelectedDeviceId) &&
                !LiquidCoolingDevices.Contains(state.SelectedDeviceId, StringComparer.OrdinalIgnoreCase))
                LiquidCoolingDevices.Add(state.SelectedDeviceId);
            SelectedLiquidCoolingDeviceId = liquidCoolingSelectionDraft && !string.IsNullOrWhiteSpace(previousSelection)
                ? previousSelection
                : state.SelectedDeviceId is { Length: > 0 } selected && LiquidCoolingDevices.Contains(selected)
                    ? selected
                    : previousSelection is { Length: > 0 } previous && LiquidCoolingDevices.Contains(previous)
                        ? previous
                        : LiquidCoolingDevices.FirstOrDefault();
        }
        finally { applyingLiquidCoolingState = false; }
        IsLiquidCoolingConnected = state.Connected;
        if (state.FanMode is { } fan) SelectedLiquidCoolingFanMode = fan;
        if (state.PumpMode is { } pump) SelectedLiquidCoolingPumpMode = pump;
        OnPropertyChanged(nameof(LiquidCoolingFanStatusText));
        OnPropertyChanged(nameof(LiquidCoolingPumpStatusText));
        OnPropertyChanged(nameof(LiquidCoolingConnectionStatusText));
        OnPropertyChanged(nameof(LiquidCoolingDeviceDisplayName));
        OnPropertyChanged(nameof(LiquidCoolingDeviceDetailsText));
        RefreshLiquidCoolingActions();
    }

    private static bool IsDeviceDescription(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        !string.Equals(value, "Connected", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(value, "Disconnected", StringComparison.OrdinalIgnoreCase);

    private async Task RunLiquidCoolingActionAsync(
        Func<IQuickControlsController, ValueTask<ControlResult>> action,
        string failurePrefix,
        CancellationToken cancellationToken)
    {
        if (quickControlsController is null || isShuttingDown) return;
        CancelLiquidCoolingRecovery();
        await liquidCoolingOperationGate.WaitAsync(cancellationToken);
        IsLiquidCoolingBusy = true;
        try
        {
            if (isShuttingDown) return;
            var result = await action(quickControlsController);
            LiquidCoolingStatusText = result.Reason;
            StatusMessage = result.Reason;
            ApplyLiquidCooling(await quickControlsController.ReadLiquidCoolingAsync(cancellationToken));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LiquidCoolingStatusText = $"{failurePrefix}：{exception.Message}";
        }
        finally
        {
            IsLiquidCoolingBusy = false;
            liquidCoolingOperationGate.Release();
        }
    }

    private void RefreshLiquidCoolingActions()
    {
        OnPropertyChanged(nameof(CanScanLiquidCooling));
        OnPropertyChanged(nameof(CanConnectLiquidCooling));
        OnPropertyChanged(nameof(CanDisconnectLiquidCooling));
        OnPropertyChanged(nameof(CanPrimeLiquidCooling));
        OnPropertyChanged(nameof(CanResetLiquidCooling));
        OnPropertyChanged(nameof(CanControlLiquidCoolingStrategy));
        OnPropertyChanged(nameof(CanChooseLiquidCoolingManualStrategy));
    }

    public async Task<bool> InitializeGpuOutputModeAsync(CancellationToken cancellationToken)
    {
        if (gpuOutputModeController is null)
        {
            GpuOutputStatusText = "显卡输出控制器未接入";
            return false;
        }

        try
        {
            var state = await gpuOutputModeController.ReadStateAsync(cancellationToken);
            if (state is null)
            {
                GpuOutputStatusText = "GCU 未返回可识别的显卡输出模式";
                return false;
            }
            ApplyGpuOutputState(state);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            GpuOutputStatusText = $"读取失败：{exception.Message}";
            RefreshGpuOutputProperties();
            return false;
        }
    }

    public void ApplyGpuOutputState(GpuOutputModeState state)
    {
        gpuOutputState = state;
        SelectedGpuOutputMode = state.Mode;
        GpuOutputStatusText = $"当前：{GpuModeLabel(state.Mode)} · 更改后需要重启生效";
        RefreshGpuOutputProperties();
    }

    public async Task ApplyGpuOutputModeAsync(CancellationToken cancellationToken)
    {
        if (gpuOutputModeController is null || gpuOutputState is null ||
            !gpuOutputState.Capabilities.Supports(SelectedGpuOutputMode)) return;

        if (isApplyingGpuOutputMode) return;
        var requestedMode = SelectedGpuOutputMode;
        isApplyingGpuOutputMode = true;
        RefreshGpuOutputProperties();
        try
        {
            GpuOutputStatusText = $"正在写入并回读：{GpuModeLabel(SelectedGpuOutputMode)}";
            var result = await gpuOutputModeController.SetModeAsync(requestedMode, cancellationToken);
            GpuOutputStatusText = result.Reason;
            StatusMessage = result.Reason;
            if (result.Succeeded)
                gpuOutputState = gpuOutputState with { Mode = requestedMode };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            GpuOutputStatusText = $"切换失败：{exception.Message}";
            StatusMessage = GpuOutputStatusText;
        }
        finally
        {
            isApplyingGpuOutputMode = false;
            RefreshGpuOutputProperties();
        }
    }

    public void OpenSettings()
    {
        ActiveDrawer = HomeDrawer.Settings;
        StatusMessage = "设置已打开 · 可管理启动、冲突服务与界面选项";
    }

    public void CloseDrawer() => ActiveDrawer = HomeDrawer.None;

    private void RefreshGpuOutputProperties()
    {
        OnPropertyChanged(nameof(SupportsHybridGpuMode));
        OnPropertyChanged(nameof(SupportsDiscreteGpuMode));
        OnPropertyChanged(nameof(SupportsIntegratedGpuMode));
        OnPropertyChanged(nameof(IsGpuOutputApplyEnabled));
    }

    private static string GpuModeLabel(GpuOutputMode mode) => mode switch
    {
        GpuOutputMode.Hybrid => "混合输出",
        GpuOutputMode.Discrete => "独显直连",
        GpuOutputMode.Integrated => "纯核显模式",
        _ => mode.ToString()
    };

    public void SaveWindowPosition(double left, double top)
    {
        if (!RememberWindowPosition || !double.IsFinite(left) || !double.IsFinite(top))
        {
            return;
        }

        if (TrySaveSettings(applicationSettings with { WindowLeft = left, WindowTop = top }))
        {
            OnPropertyChanged(nameof(SavedWindowLeft));
            OnPropertyChanged(nameof(SavedWindowTop));
        }
    }

    private bool TrySaveSettings(ApplicationSettings settings)
    {
        try
        {
            settingsStore.Save(settings);
            applicationSettings = settings;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            StatusMessage = "设置保存失败 · 未修改现有状态";
            return false;
        }
    }

    private void RefreshFanPoints()
    {
        FanPoints.Clear();
        var points = fanDraft is null
            ? Array.Empty<FanCurvePoint>()
            : (showingCpuFan ? fanDraft.Cpu : fanDraft.Gpu);
        // 255 is GCU's inactive tail sentinel, not a user-facing temperature.
        // Keep those records in fanDraft for exact readback/validation but hide them here.
        foreach (var point in points.Where(point => point.UpTemperatureC < 255))
        {
            FanPoints.Add(new(
                point.Id,
                point.UpTemperatureC,
                point.DownTemperatureC,
                point.DutyPercent,
                UpdateFanPoint));
        }

        OnPropertyChanged(nameof(FanCurveLimitText));
        ValidateFanDraft();
    }

    private void UpdateFanPoint(FanCurvePointViewModel edited)
    {
        if (fanDraft is null)
        {
            return;
        }

        var source = showingCpuFan ? fanDraft.Cpu : fanDraft.Gpu;
        var index = Array.FindIndex(source.ToArray(), point => point.Id == edited.Id);
        if (index >= 0)
        {
            int? previous = index > 0 ? source[index - 1].DutyPercent : null;
            int? next = index + 1 < source.Count ? source[index + 1].DutyPercent : null;
            var clamped = FanCurveChartMath.ClampDuty(edited.DutyPercent, previous, next);
            if (clamped != edited.DutyPercent)
            {
                edited.DutyPercent = clamped;
                return;
            }
        }

        var updated = source
            .Select(point => point.Id == edited.Id
                ? point with { DutyPercent = edited.DutyPercent }
                : point)
            .ToArray();
        fanDraft = showingCpuFan
            ? fanDraft with { Cpu = updated }
            : fanDraft with { Gpu = updated };
        fanDraftsByProfile[CurrentFanCurveKey] = fanDraft;
        ValidateFanDraft();
    }

    private string CurrentFanCurveKey => SelectedProfileId switch
    {
        "turbo" => $"turbo:{SelectedTurboStrategy}",
        "custom" => $"custom:{SelectedCustomProfileIndex}",
        _ => SelectedProfileId
    };

    private async Task RunProfileEditAsync(Func<Task> edit, CancellationToken token)
    {
        if (isShuttingDown) return;
        var target = CurrentPlatformSelection();
        await performanceTransitionGate.WaitAsync(token);
        try
        {
            if (isShuttingDown || target != CurrentPlatformSelection()) return;
            await edit();
        }
        finally { performanceTransitionGate.Release(); }
    }

    public void BeginShutdown()
    {
        isShuttingDown = true;
        CancelLiquidCoolingRecovery();
        liquidCoolingLifetime.Cancel();
        OnPropertyChanged(nameof(CanRecheckCapabilities));
    }

    private string CurrentPowerLimitKey => SelectedProfileId switch
    {
        "turbo" => $"turbo:{SelectedTurboStrategy}",
        "custom" => $"custom:{SelectedCustomProfileIndex}",
        _ => $"{SelectedProfileId}:1"
    };

    private PerformanceSchedule CreateSchedule(PlatformModeSelection selection)
    {
        var key = selection.Mode switch
        {
            PlatformMode.Office => "office:1",
            PlatformMode.Balanced => "balanced:1",
            PlatformMode.TurboQuiet => "turbo:Quiet",
            PlatformMode.TurboHigh => "turbo:HighEfficiency",
            PlatformMode.Custom => $"custom:{Math.Clamp(selection.ProfileIndex, 1, 3)}",
            _ => throw new ArgumentOutOfRangeException(nameof(selection))
        };
        var draft = GetPowerDraft(key);
        var boost = boostDraftsByProfile.TryGetValue(key, out var savedBoost)
            ? savedBoost
            : ProcessorBoostMode.Aggressive;
        return new(
            selection,
            CanEditPowerLimitsFor(selection)
                ? new CpuPolicy(draft.Pl1Watts, draft.Pl2Watts, null)
                : null,
            new ProcessorBoostPolicy(boost, boost),
            CreateFrequencyPolicy(key));
    }

    private ProcessorFrequencyLimitPolicy CreateFrequencyPolicy(PlatformModeSelection selection)
    {
        var key = selection.Mode switch
        {
            PlatformMode.Office => "office:1",
            PlatformMode.Balanced => "balanced:1",
            PlatformMode.TurboQuiet => "turbo:Quiet",
            PlatformMode.TurboHigh => "turbo:HighEfficiency",
            PlatformMode.Custom => $"custom:{Math.Clamp(selection.ProfileIndex, 1, 3)}",
            _ => throw new ArgumentOutOfRangeException(nameof(selection))
        };
        return CreateFrequencyPolicy(key);
    }

    private ProcessorFrequencyLimitPolicy CreateFrequencyPolicy(string key) =>
        ProcessorFrequencyLimitPolicy.Uniform((uint)(
            frequencyConfiguredByProfile.GetValueOrDefault(key)
                ? Math.Max(0, frequencyDraftsByProfile.GetValueOrDefault(key))
                : 0));

    private PlatformModeSelection CurrentPlatformSelection() => SelectedProfileId switch
    {
        "office" => new(PlatformMode.Office),
        "balanced" => new(PlatformMode.Balanced),
        "turbo" => new(SelectedTurboStrategy is TurboStrategy.HighEfficiency
            ? PlatformMode.TurboHigh
            : PlatformMode.TurboQuiet),
        "custom" => new(PlatformMode.Custom, SelectedCustomProfileIndex),
        _ => throw new InvalidOperationException($"未知性能档位：{SelectedProfileId}")
    };

    private void UpdatePerformanceReadback(PerformanceSchedule? readback)
    {
        if (readback is null)
        {
            return;
        }

        livePerformanceReadbackText = CanEditPowerLimits && readback.Cpu is not null
            ? FormatLivePerformanceReadback(readback.Cpu, readback.Boost)
            : null;
        ProcessorFrequencyLimitStatusText = FormatFrequencyReadback(readback.Frequency);
        RefreshPerformanceProperties();
    }

    private void SwitchFrequencyDraftForCurrentProfile()
    {
        var value = Math.Max(0, frequencyDraftsByProfile.GetValueOrDefault(CurrentPowerLimitKey));
        isProcessorFrequencyLimitEnabled = value > 0;
        if (value is >= 1000 and <= 6000)
        {
            selectedMaximumProcessorFrequencyMhz = value;
        }
        OnPropertyChanged(nameof(IsProcessorFrequencyLimitEnabled));
        OnPropertyChanged(nameof(SelectedMaximumProcessorFrequencyMhz));
    }

    private async Task<string> VerifyPerformancePolicyStabilityAsync(
        PerformanceSchedule expected,
        CancellationToken cancellationToken)
    {
        await verificationDelay(TimeSpan.FromSeconds(1), cancellationToken);
        var cpu = await cpuPolicyController!.ReadCpuPolicyAsync(cancellationToken);
        var boost = await boostController!.ReadAsync(cancellationToken);
        var frequency = frequencyLimitController is null
            ? expected.Frequency
            : await frequencyLimitController.ReadAsync(cancellationToken);
        var cpuChanged = expected.Cpu is not null && cpu != expected.Cpu;
        if (cpuChanged || boost != expected.Boost || frequency != expected.Frequency)
        {
            var cpuText = cpu is null
                ? "PL1 / PL2 回读不可用"
                : $"PL1 {cpu.Pl1Watts} W · PL2 {cpu.Pl2Watts} W";
            var powerText = expected.Cpu is null ? "OEM 预设 PL 未写入" : cpuText;
            return $"写入后被覆盖 · {powerText} · Boost {BoostModeLabel(boost.AcMode)} · {FormatFrequencyReadback(frequency)}";
        }
        return expected.Cpu is null
            ? "Boost 与 Windows 频率策略已应用、回读，并在 1 秒复查后保持不变；OEM 预设 PL 未写入。"
            : "整套性能策略已应用、回读，并在 1 秒复查后保持不变。";
    }

    private async Task<ControlResult> SettleFrequencyAfterPlatformSwitchAsync(
        ProcessorFrequencyLimitPolicy expected,
        long requestRevision,
        CancellationToken cancellationToken)
    {
        if (frequencyLimitController is null)
        {
            return new(true, "整机档位已应用并回读。");
        }

        await verificationDelay(TimeSpan.FromSeconds(2), cancellationToken);
        if (requestRevision != Volatile.Read(ref platformRequestRevision))
            return new(true, "已有更新的档位请求，跳过旧档位复查。");
        var settled = await frequencyLimitController.ReadAsync(cancellationToken);
        if (requestRevision != Volatile.Read(ref platformRequestRevision))
            return new(true, "已有更新的档位请求，跳过旧档位复查。");
        ProcessorFrequencyLimitStatusText = FormatFrequencyReadback(settled);
        if (settled == expected)
        {
            return new(true, "整机档位与 Windows 频率策略已应用，并在延迟复查后保持一致。");
        }

        var reapplied = await frequencyLimitController.ApplyAsync(expected, cancellationToken);
        if (!reapplied.Succeeded)
        {
            return ControlResult.Rejected($"检测到 OEM 延迟覆盖，但恢复失败：{reapplied.Reason}");
        }

        await verificationDelay(TimeSpan.FromSeconds(1), cancellationToken);
        if (requestRevision != Volatile.Read(ref platformRequestRevision))
            return new(true, "已有更新的档位请求，跳过旧档位复查。");
        var confirmed = await frequencyLimitController.ReadAsync(cancellationToken);
        if (requestRevision != Volatile.Read(ref platformRequestRevision))
            return new(true, "已有更新的档位请求，跳过旧档位复查。");
        ProcessorFrequencyLimitStatusText = FormatFrequencyReadback(confirmed);
        return confirmed == expected
            ? new(true, "检测到 OEM 延迟覆盖，已重新应用 Windows 频率策略并回读确认。")
            : ControlResult.Rejected(
                $"OEM 延迟覆盖仍在发生：期望 {FormatFrequencyReadback(expected)}，实际 {FormatFrequencyReadback(confirmed)}");
    }

    private static string FormatFrequencyReadback(ProcessorFrequencyLimitPolicy frequency) =>
        $"频率回读 · C0 {frequency.AcMaximumMhz} · C1 {frequency.AcClass1MaximumMhz} · C2 {frequency.AcClass2MaximumMhz} MHz";

    private void LoadSavedPerformanceProfiles(
        IReadOnlyDictionary<string, PerformanceProfileSettings>? profiles)
    {
        if (profiles is null) return;
        foreach (var (key, profile) in profiles)
        {
            if (profile.Pl1Watts is < 5 or > 250 ||
                profile.Pl2Watts is < 5 or > 250 ||
                profile.Pl1Watts > profile.Pl2Watts ||
                !Enum.IsDefined(profile.BoostMode))
            {
                continue;
            }
            powerDraftsByProfile[key] = new(null, profile.Pl1Watts, profile.Pl2Watts);
            boostDraftsByProfile[key] = profile.BoostMode;
            frequencyConfiguredByProfile[key] = profile.MaximumProcessorFrequencyConfigured;
            frequencyDraftsByProfile[key] = profile.MaximumProcessorFrequencyConfigured
                ? Math.Max(0, profile.MaximumProcessorFrequencyMhz)
                : 0;
        }
    }

    private void SavePerformanceProfile(PlatformModeSelection selection)
    {
        var key = selection.Mode switch
        {
            PlatformMode.Office => "office:1",
            PlatformMode.Balanced => "balanced:1",
            PlatformMode.TurboQuiet => "turbo:Quiet",
            PlatformMode.TurboHigh => "turbo:HighEfficiency",
            PlatformMode.Custom => $"custom:{Math.Clamp(selection.ProfileIndex, 1, 3)}",
            _ => CurrentPowerLimitKey
        };
        var draft = GetPowerDraft(key);
        var profiles = applicationSettings.PerformanceProfiles is null
            ? new Dictionary<string, PerformanceProfileSettings>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, PerformanceProfileSettings>(
                applicationSettings.PerformanceProfiles, StringComparer.OrdinalIgnoreCase);
        var boost = boostDraftsByProfile.TryGetValue(key, out var savedBoost)
            ? savedBoost
            : ProcessorBoostMode.Aggressive;
        profiles[key] = new(
            draft.Pl1Watts,
            draft.Pl2Watts,
            boost,
            Math.Max(0, frequencyDraftsByProfile.GetValueOrDefault(key)),
            frequencyConfiguredByProfile.GetValueOrDefault(key));
        TrySaveSettings(applicationSettings with { PerformanceProfiles = profiles });
    }

    private CpuPowerLimitDraft GetCurrentPowerDraft()
        => GetPowerDraft(CurrentPowerLimitKey);

    private CpuPowerLimitDraft GetPowerDraft(string key)
    {
        if (powerDraftsByProfile.TryGetValue(key, out var draft))
        {
            return draft;
        }

        var readback = ResolvePowerReadback(key);
        draft = readback is null
            ? new(null, 45, 65)
            : CreatePowerDraft(readback);
        powerDraftsByProfile[key] = draft;
        return draft;
    }

    private CpuPowerLimitProfile? ResolvePowerReadback(string key)
    {
        if (powerReadbacksByProfile.TryGetValue(key, out var exact))
        {
            return exact;
        }

        if (key.StartsWith("turbo:", StringComparison.OrdinalIgnoreCase) &&
            powerReadbacksByProfile.TryGetValue("turbo:1", out var turbo))
        {
            return turbo;
        }

        return null;
    }

    private static CpuPowerLimitDraft CreatePowerDraft(CpuPowerLimitProfile readback)
    {
        var pl2 = Math.Clamp(readback.Pl2Watts, 5, 250);
        var pl1 = Math.Clamp(readback.Pl1Watts, 5, pl2);
        return new(readback, pl1, pl2);
    }

    private void RefreshPerformanceProperties()
    {
        OnPropertyChanged(nameof(SelectedPl1Watts));
        OnPropertyChanged(nameof(SelectedPl2Watts));
        OnPropertyChanged(nameof(IsPerformanceDraftValid));
        OnPropertyChanged(nameof(IsPerformanceApplyEnabled));
        OnPropertyChanged(nameof(PerformanceReadbackText));
        OnPropertyChanged(nameof(PerformanceValidationText));
        OnPropertyChanged(nameof(SelectedProcessorBoostMode));
    }

    private string FormatLivePerformanceReadback(CpuPolicy cpu, ProcessorBoostPolicy boost) =>
        $"GCU 配置回读 · PL1 {cpu.Pl1Watts} W · PL2 {cpu.Pl2Watts} W · Boost {BoostModeLabel(boost.AcMode)}";

    private string BoostModeLabel(ProcessorBoostMode mode) =>
        ProcessorBoostModes.First(option => option.Mode == mode).Label;

    private void SwitchFanDraftForCurrentProfile()
    {
        var key = CurrentFanCurveKey;
        if (fanDraftsByProfile.TryGetValue(key, out var draft))
        {
            fanDraft = draft;
            fanReadback = fanReadbacksByProfile.TryGetValue(key, out var readback)
                ? readback
                : null;
            return;
        }

        if (!IsCustomProfileSelected)
        {
            fanReadback = null;
            fanDraft = CreateOemFanReference(key);
            fanDraftsByProfile[key] = fanDraft;
            FanStatusText = $"{CurrentFanProfileLabel.Replace("当前：", string.Empty)} · 风扇曲线仅供参考";
            return;
        }

        // A custom slot without its own readback has no safe editable baseline.
        fanDraft = null;
        fanReadback = null;
        FanStatusText = "当前方案的风扇设置待读取，请重新打开风扇调整。";
        OnPropertyChanged(nameof(CanEditFanSettings));
        OnPropertyChanged(nameof(IsFanApplyEnabled));
    }

    private static FanCurveTable CreateOemFanReference(string key)
    {
        var visibleTemperatures = new[] { 40, 48, 52, 56, 60, 64, 68, 72, 76, 80, 85 };
        var duties = key.ToLowerInvariant() switch
        {
            "office" => new[] { 0, 25, 30, 35, 40, 45, 50, 60, 70, 80, 100 },
            "balanced" => new[] { 0, 30, 35, 40, 50, 55, 60, 70, 80, 90, 100 },
            "turbo:quiet" => new[] { 0, 31, 36, 41, 51, 56, 61, 71, 81, 91, 100 },
            "turbo:highefficiency" => new[] { 0, 35, 40, 45, 55, 60, 65, 75, 85, 94, 100 },
            _ => new[] { 0, 30, 35, 40, 50, 55, 60, 70, 80, 90, 100 }
        };

        var points = visibleTemperatures
            .Select((temperature, index) => new FanCurvePoint(
                index,
                temperature,
                index == 0 ? temperature : Math.Max(0, temperature - 3),
                duties[index]))
            .Concat(Enumerable.Range(visibleTemperatures.Length, 5)
                .Select(index => new FanCurvePoint(index, 255, 255, 100)))
            .ToArray();
        return new($"{key} reference", true, points, points.ToArray());
    }

    private void ValidateFanDraft()
    {
        var validation = fanDraft is null
            ? new FanCurveValidationResult(false, ["No curve loaded."])
            : FanCurveValidator.Validate(fanDraft);
        IsFanCurveValid = validation.IsValid;
        if (!validation.IsValid && fanDraft is not null)
        {
            FanStatusText = "曲线校验未通过 · 请检查递增关系与末端 100%";
        }
    }

    private static FanCurveTable CloneTable(FanCurveTable table) => table with
    {
        Cpu = table.Cpu.ToArray(),
        Gpu = table.Gpu.ToArray()
    };

    private bool IsLightingZoneAvailable(LightingZone zone) =>
        lightingZoneStates.GetValueOrDefault(zone)?.Available == true;

    private bool IsKeyboardLightingPowerOnly =>
        lightingZoneStates.GetValueOrDefault(LightingZone.Keyboard) is { Available: true, HasDetailedConfiguration: false } state &&
        state.SupportedEffects.All(effect => effect == LightingEffect.OemDefault);

    private void NotifyLightingCapabilitiesChanged()
    {
        OnPropertyChanged(nameof(IsKeyboardLightingZoneAvailable));
        OnPropertyChanged(nameof(IsCoastLightingZoneAvailable));
        OnPropertyChanged(nameof(IsLogoLightingZoneAvailable));
        OnPropertyChanged(nameof(IsWaterLightingZoneAvailable));
        OnPropertyChanged(nameof(CanApplyLighting));
        OnPropertyChanged(nameof(CanToggleSelectedLightingZonePower));
        OnPropertyChanged(nameof(CanApplyLightingSleepTimer));
    }

    private void LoadLightingEditor(LightingZone zone)
    {
        var state = lightingZoneStates.GetValueOrDefault(zone);
        if (lightingEditorDrafts.GetValueOrDefault(zone) is { } draft)
        {
            LoadLightingConfiguration(draft, state?.SupportedEffects ?? [draft.Effect]);
        }
        else if (state?.Configuration is { } configuration)
        {
            LoadLightingConfiguration(configuration, state.SupportedEffects);
        }
        else
        {
            LoadLightingConfiguration(
                new(zone, true, LightingEffect.Static, new(0, 176, 255), 3, 2, LightingDirection.Left, 120),
                state?.SupportedEffects ?? [LightingEffect.Static]);
        }
        OnPropertyChanged(nameof(CanApplyLighting));
        OnPropertyChanged(nameof(CanToggleSelectedLightingZonePower));
    }

    private void LoadLightingConfiguration(
        LightingZoneConfiguration configuration,
        IReadOnlyList<LightingEffect> supportedEffects)
    {
        IsSynchronizingLightingEditor = true;
        try
        {
            var effects = supportedEffects.Distinct().ToList();
            if (!effects.Contains(configuration.Effect)) effects.Add(configuration.Effect);
            var optionsChanged = !LightingEffectOptions.Select(option => option.Value).SequenceEqual(effects);
            if (optionsChanged)
            {
                LightingEffectOptions.Clear();
                foreach (var effect in effects) LightingEffectOptions.Add(LightingEffectOption.From(effect));
            }
            SelectedLightingEffect = configuration.Effect;
            // Resetting the options collection clears WPF's selected item even when the enum value did not change.
            if (optionsChanged) OnPropertyChanged(nameof(SelectedLightingEffect));
            IsSelectedLightingZoneEnabled = configuration.Enabled;
            LightingColorHex = configuration.Color.Hex;
            LightingBrightness = configuration.Brightness;
            LightingSpeed = configuration.Speed;
            SelectedLightingDirection = configuration.Direction;
        }
        finally
        {
            IsSynchronizingLightingEditor = false;
        }
    }

    private LightingProfile BuildLightingDraft(string id, string name)
    {
        var keyboard = lightingEditorDrafts.GetValueOrDefault(LightingZone.Keyboard) ??
                       lightingZoneStates.GetValueOrDefault(LightingZone.Keyboard)?.Configuration ??
                       new LightingZoneConfiguration(
                           LightingZone.Keyboard, true, LightingEffect.Static, new(0, 176, 255),
                            3, 2, LightingDirection.Left, LightingSleepSeconds);
        if (SelectedLightingZone == LightingZone.Keyboard)
        {
            if (!TryParseLightingColor(LightingColorHex, out var editorColor)) editorColor = new(0, 176, 255);
            keyboard = new(
                LightingZone.Keyboard,
                IsSelectedLightingZoneEnabled,
                SelectedLightingEffect,
                editorColor,
                LightingBrightness,
                LightingSpeed,
                SelectedLightingDirection,
                LightingSleepSeconds);
        }
        return new(LightingProfileCatalog.CurrentSchemaVersion, id, name, [keyboard]);
    }

    private void RestoreSavedLightingProfile()
    {
        try
        {
            var profile = lightingProfileStore.Load();
            if (profile is null) return;
            if (!LightingProfileCatalog.Validate(profile, out var reason))
            {
                LightingStatusText = $"已保存的灯光方案无效：{reason}";
                return;
            }

            importedLightingProfile = profile;
            LightingStatusText = $"已恢复 {profile.Name}；打开灯光页后可显式应用。";
        }
        catch (Exception exception)
        {
            LightingStatusText = $"恢复已保存灯光方案失败：{exception.Message}";
        }
    }

    private static bool TryParseLightingColor(string? text, out LightingRgb color)
    {
        color = new(0, 0, 0);
        if (string.IsNullOrWhiteSpace(text)) return false;
        var value = text.Trim().TrimStart('#');
        if (value.Length != 6 || !uint.TryParse(value, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var rgb)) return false;
        color = new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return true;
    }

    private static string LightingZoneLabel(LightingZone zone) => zone switch
    {
        LightingZone.Keyboard => "键盘背光",
        LightingZone.CoastLightbar => "海岸灯带",
        LightingZone.Logo => "LOGO 灯",
        LightingZone.LiquidCooler => "水冷灯",
        _ => zone.ToString()
    };

    private static string FormatTemperature(double temperature) =>
        $"{Math.Round(temperature):0}°C";

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private sealed record LightingLocalTarget(
        LightingZoneConfiguration Configuration,
        DateTimeOffset ExpiresAt);

    private sealed class NullSettingsStore : IApplicationSettingsStore
    {
        public static NullSettingsStore Instance { get; } = new();

        public ApplicationSettings Load() => new();

        public void Save(ApplicationSettings settings)
        {
        }
    }

    private sealed class NullStartupRegistration : IStartupRegistration
    {
        public static NullStartupRegistration Instance { get; } = new();

        public bool IsEnabled() => false;

        public void SetEnabled(bool enabled)
        {
        }
    }
}
