namespace RevoHelper.Core.Control;

public enum ShortcutControl { WinKey, FnKey, Osd }
public enum SystemToggleControl { Wifi, Bluetooth, Touchpad, Webcam, CopilotKey }
public enum PowerToggleControl { UsbChargingWhileOff, AcAutoBoot, HighPerformancePowerMode, DeepSleep }
public enum BatteryProtectionMode { Performance, Balanced, Healthy }
public enum DisplayColorMode { Default, Srgb, P3, AdobeRgb }
public enum LiquidCoolingFanMode { Quieter, Quiet, Balanced, Maximum, Auto }
public enum LiquidCoolingPumpMode { Quiet, Balanced, Maximum }
public enum LiquidCoolingDeviceAction { Connect, Disconnect, InputWater, Reset }

public sealed record ShortcutState(bool WinKeyEnabled, bool FnKeyEnabled, bool OsdEnabled);
public sealed record SystemToggleState(
    bool? WifiEnabled,
    bool? BluetoothEnabled,
    bool? TouchpadEnabled,
    bool? WebcamEnabled,
    bool? CopilotKeyEnabled);
public sealed record PowerSettingsState(
    bool? UsbChargingWhileOffEnabled,
    bool SupportsAcAutoBoot,
    bool? AcAutoBootEnabled,
    bool? HighPerformancePowerModeEnabled,
    bool? DeepSleepEnabled,
    int? DeepSleepMinutes,
    int? BatteryChargeLimitPercent);
public sealed record KeyboardLightingState(bool Enabled, string? Type);
public sealed record BatteryProtectionState(BatteryProtectionMode Mode, int? BatteryPercent);
public sealed record DisplayFeatureState(
    bool? ColorCalibrationEnabled,
    DisplayColorMode? ColorMode,
    bool? LocalDimmingEnabled,
    bool SupportsColorCalibration,
    bool SupportsLocalDimming,
    string? ColorCalibrationResultCode = null);
public sealed record LiquidCoolingDevice(string Id);
public sealed record LiquidCoolingState(
    bool Connected,
    string? ConnectionStatus,
    LiquidCoolingFanMode? FanMode,
    LiquidCoolingPumpMode? PumpMode,
    int? FanDutyPercent,
    int? PumpDutyPercent,
    IReadOnlyList<LiquidCoolingDevice> Devices,
    string? SelectedDeviceId,
    string? FirmwareVersion,
    bool CanControl,
    bool IsPriming,
    bool? MeterNormal);

public interface IQuickControlsController
{
    ValueTask<ShortcutState?> ReadShortcutsAsync(CancellationToken cancellationToken);
    ValueTask<SystemToggleState?> ReadSystemTogglesAsync(CancellationToken cancellationToken);
    ValueTask<PowerSettingsState?> ReadPowerSettingsAsync(CancellationToken cancellationToken);
    ValueTask<KeyboardLightingState?> ReadKeyboardLightingAsync(CancellationToken cancellationToken);
    ValueTask<LightingZoneState?> ReadLightingZoneAsync(LightingZone zone, CancellationToken cancellationToken);
    ValueTask<BatteryProtectionState?> ReadBatteryProtectionAsync(CancellationToken cancellationToken);
    ValueTask<DisplayFeatureState?> ReadDisplayFeaturesAsync(CancellationToken cancellationToken);
    ValueTask<LiquidCoolingState?> ReadLiquidCoolingAsync(CancellationToken cancellationToken);
    ValueTask<LiquidCoolingState?> ScanLiquidCoolingDevicesAsync(CancellationToken cancellationToken);
    ValueTask<ControlResult> SetShortcutAsync(ShortcutControl control, bool enabled, CancellationToken cancellationToken);
    ValueTask<ControlResult> SetSystemToggleAsync(SystemToggleControl control, bool enabled, CancellationToken cancellationToken);
    ValueTask<ControlResult> SetPowerToggleAsync(PowerToggleControl control, bool enabled, CancellationToken cancellationToken);
    ValueTask<ControlResult> SetDeepSleepAsync(bool enabled, int minutes, CancellationToken cancellationToken);
    ValueTask<ControlResult> SetBatteryChargeLimitAsync(int percent, CancellationToken cancellationToken);
    ValueTask<ControlResult> SetKeyboardLightingAsync(bool enabled, CancellationToken cancellationToken);
    ValueTask<ControlResult> SetLightingZonePowerAsync(LightingZone zone, bool enabled, CancellationToken cancellationToken);
    ValueTask<ControlResult> ApplyLightingZoneAsync(LightingZoneConfiguration configuration, CancellationToken cancellationToken);
    ValueTask<ControlResult> SetLightingSleepTimerAsync(int seconds, CancellationToken cancellationToken);
    ValueTask<ControlResult> SetBatteryProtectionAsync(BatteryProtectionMode mode, CancellationToken cancellationToken);
    ValueTask<ControlResult> SetDisplayColorAsync(DisplayColorMode mode, CancellationToken cancellationToken);
    ValueTask<ControlResult> SetDisplayColorEnabledAsync(bool enabled, DisplayColorMode mode, CancellationToken cancellationToken);
    ValueTask<ControlResult> SetLocalDimmingAsync(bool enabled, CancellationToken cancellationToken);
    ValueTask<ControlResult> SetLiquidCoolingFanAsync(LiquidCoolingFanMode mode, CancellationToken cancellationToken);
    ValueTask<ControlResult> SetLiquidCoolingPumpAsync(LiquidCoolingPumpMode mode, CancellationToken cancellationToken);
    ValueTask<ControlResult> SelectLiquidCoolingDeviceAsync(string deviceId, CancellationToken cancellationToken);
    ValueTask<ControlResult> ConnectLiquidCoolingAsync(CancellationToken cancellationToken);
    ValueTask<ControlResult> DisconnectLiquidCoolingAsync(CancellationToken cancellationToken);
    ValueTask<ControlResult> StartLiquidCoolingPrimingAsync(CancellationToken cancellationToken);
    ValueTask<ControlResult> ResetLiquidCoolingDeviceAsync(CancellationToken cancellationToken);
}
