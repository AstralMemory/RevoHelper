using System.Text.Json;
using RevoHelper.Core.Control;

namespace RevoHelper.Hardware.Windows.OemControl;

public static class GcuQuickControlsCommand
{
    public const string GetStatus = "{\"Action\":\"GETSTATUS\"}";
    public const string GetBatteryProtection = "{\"Report\":\"GET\"}";

    public static string Shortcut(ShortcutControl control, bool enabled) => control switch
    {
        ShortcutControl.WinKey => Action(enabled ? "WINKEY_UNLOCK" : "WINKEY_LOCK"),
        ShortcutControl.FnKey => Action(enabled ? "FNKEY_UNLOCK" : "FNKEY_LOCK"),
        ShortcutControl.Osd => Action(enabled ? "OSD_HIDDEN_OFF" : "OSD_HIDDEN_ON"),
        _ => throw new ArgumentOutOfRangeException(nameof(control))
    };

    public static string SystemToggle(SystemToggleControl control, bool enabled) => Action(control switch
    {
        SystemToggleControl.Wifi => enabled ? "WIFI_ON" : "WIFI_OFF",
        SystemToggleControl.Bluetooth => enabled ? "BT_ON" : "BT_OFF",
        SystemToggleControl.Touchpad => enabled ? "TOUCHPAD_ON" : "TOUCHPAD_OFF",
        SystemToggleControl.Webcam => enabled ? "WEBCAM_ON" : "WEBCAM_OFF",
        SystemToggleControl.CopilotKey => enabled ? "COPILOTKEY_UNLOCK" : "COPILOTKEY_LOCK",
        _ => throw new ArgumentOutOfRangeException(nameof(control))
    });

    public static string PowerToggle(PowerToggleControl control, bool enabled) => Action(control switch
    {
        PowerToggleControl.UsbChargingWhileOff => enabled ? "USB_CHARGER_ON" : "USB_CHARGER_OFF",
        PowerToggleControl.AcAutoBoot => enabled ? "ACRECOVERY_TOGGLE_ON" : "ACRECOVERY_TOGGLE_OFF",
        PowerToggleControl.HighPerformancePowerMode => enabled ? "HIGHPERFORMANCEPOWERMODE_ON" : "HIGHPERFORMANCEPOWERMODE_OFF",
        PowerToggleControl.DeepSleep => enabled ? "DEEPSLEEP_ON" : "DEEPSLEEP_OFF",
        _ => throw new ArgumentOutOfRangeException(nameof(control))
    });

    public static string DeepSleep(bool enabled, int minutes) =>
        JsonSerializer.Serialize(new DeepSleepCommand(enabled ? "DEEPSLEEP_ON" : "DEEPSLEEP_OFF", minutes));

    public static string BatteryChargeLimit(int percent) =>
        JsonSerializer.Serialize(new BatteryChargeLimitCommand(
            "BATTERY_CHARGINGLIMIT_SETTING", percent.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    public static string KeyboardBacklight(bool enabled) =>
        JsonSerializer.Serialize(new KeyboardPowerCommand("SetPower", enabled ? 1 : 0));

    public static string LightingEffect(LightingZoneConfiguration configuration, string? hardwareType = null)
    {
        if (!configuration.Enabled)
        {
            return configuration.Zone == LightingZone.LiquidCooler
                ? JsonSerializer.Serialize(new WaterRgbCommand("LEDControl", "0", "0", "0"))
                : KeyboardBacklight(false);
        }

        if (configuration.Effect == RevoHelper.Core.Control.LightingEffect.OemDefault)
        {
            if (configuration.Zone != LightingZone.Keyboard)
                throw new ArgumentException("机器初始默认背光只适用于键盘分区。", nameof(configuration));
            return KeyboardBacklight(true);
        }

        if (configuration.Zone == LightingZone.LiquidCooler)
        {
            return configuration.Effect switch
            {
                RevoHelper.Core.Control.LightingEffect.Static => JsonSerializer.Serialize(new WaterRgbCommand(
                    "LEDControl", configuration.Color.R.ToString(), configuration.Color.G.ToString(), configuration.Color.B.ToString())),
                RevoHelper.Core.Control.LightingEffect.Breathing => JsonSerializer.Serialize(new WaterModeCommand("LEDBreathing", 1)),
                RevoHelper.Core.Control.LightingEffect.Colorful => JsonSerializer.Serialize(new WaterModeCommand("LEDColorful", 2)),
                RevoHelper.Core.Control.LightingEffect.Rotate => JsonSerializer.Serialize(new WaterModeCommand("FanLEDEffect", 4)),
                RevoHelper.Core.Control.LightingEffect.Rainbow => JsonSerializer.Serialize(new WaterModeCommand("FanLEDEffect", 5)),
                _ => throw new ArgumentException("水冷灯不支持所选灯效。", nameof(configuration))
            };
        }

        var colorBuffer = KeyboardPalette(configuration).Select((entry, index) => new KeyboardColorEntry(
            (uint)index,
            entry.R,
            entry.G,
            entry.B,
            $"{entry.R:X2}{entry.G:X2}{entry.B:X2}",
            0,
            100,
            100)).ToArray();
        var color = new KeyboardColor(true, colorBuffer.Length, colorBuffer);
        return JsonSerializer.Serialize(new KeyboardEffectCommand(
            "SetEffectALL", "Lighting", EffectName(configuration.Effect),
            configuration.Brightness.ToString(System.Globalization.CultureInfo.InvariantCulture),
            SerializeSpeed(configuration.Zone, configuration.Speed, hardwareType),
            DirectionName(configuration), configuration.Zone == LightingZone.Keyboard ? "1" : "0", color, string.Empty));
    }

    public static string WaterEffectOff(RevoHelper.Core.Control.LightingEffect effect) => effect switch
    {
        RevoHelper.Core.Control.LightingEffect.Breathing => JsonSerializer.Serialize(new WaterModeCommand("LEDBreathing", 0)),
        RevoHelper.Core.Control.LightingEffect.Colorful => JsonSerializer.Serialize(new WaterModeCommand("LEDColorful", 0)),
        RevoHelper.Core.Control.LightingEffect.Rotate or RevoHelper.Core.Control.LightingEffect.Rainbow =>
            JsonSerializer.Serialize(new WaterModeCommand("FanLEDEffect", 0)),
        _ => JsonSerializer.Serialize(new WaterRgbCommand("LEDControl", "0", "0", "0"))
    };

    public static string LightingSleepTimer(int seconds) => seconds == 0
        ? Action("KEYBOARD_LIGHTBAR_TIMER_OFF")
        : JsonSerializer.Serialize(new LightingTimerCommand("KEYBOARD_LIGHTBAR_TIMER_ON", seconds));

    public static string BatteryProtection(BatteryProtectionMode mode) => Action(mode switch
    {
        BatteryProtectionMode.Performance => "PERFORMANCEDMODE",
        BatteryProtectionMode.Balanced => "BALANCEDMODE",
        BatteryProtectionMode.Healthy => "HEALTHYMODE",
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    });

    public static string DisplayColor(DisplayColorMode mode) => Action(mode switch
    {
        DisplayColorMode.Default => "COLOR_CALIBRATION_ON_DEFAULT",
        DisplayColorMode.Srgb => "COLOR_CALIBRATION_ON_SRGB",
        DisplayColorMode.P3 => "COLOR_CALIBRATION_ON_P3",
        DisplayColorMode.AdobeRgb => "COLOR_CALIBRATION_ON_ADOBERGB",
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    });

    public static string DisplayColorEnabled(bool enabled, DisplayColorMode mode) =>
        enabled ? DisplayColor(mode) : Action("COLOR_CALIBRATION_OFF");

    public static string LocalDimming(bool enabled) => Action(enabled ? "LOCALDIMMING_ON" : "LOCALDIMMING_OFF");

    public static string LiquidCoolingFan(LiquidCoolingFanMode mode) =>
        JsonSerializer.Serialize(new LiquidFanCommand("LC_FanCtrl", ((int)mode).ToString()));

    public static string LiquidCoolingPump(LiquidCoolingPumpMode mode) =>
        JsonSerializer.Serialize(new LiquidPumpCommand("LC_PumpCtrl", ((int)mode).ToString()));

    public static string LiquidCoolingDevice(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) throw new ArgumentException("水冷设备标识不能为空。", nameof(deviceId));
        return JsonSerializer.Serialize(new LiquidDeviceCommand("DeviceMacSetting", deviceId));
    }

    public static string LiquidCoolingAction(LiquidCoolingDeviceAction action) => Action(action switch
    {
        LiquidCoolingDeviceAction.Connect => "Connect",
        LiquidCoolingDeviceAction.Disconnect => "Disconnect",
        LiquidCoolingDeviceAction.InputWater => "InputWater",
        LiquidCoolingDeviceAction.Reset => "ClearDevMAC",
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    });

    private static string Action(string action) => JsonSerializer.Serialize(new ActionCommand(action));

    private static string EffectName(RevoHelper.Core.Control.LightingEffect effect) => effect switch
    {
        RevoHelper.Core.Control.LightingEffect.Static => "Single",
        RevoHelper.Core.Control.LightingEffect.ColorMarquee => "Marquee",
        RevoHelper.Core.Control.LightingEffect.Fireworks => "Spark",
        RevoHelper.Core.Control.LightingEffect.Game => "Gaming",
        RevoHelper.Core.Control.LightingEffect.Colorful => "Mix",
        _ => effect.ToString()
    };

    private static string DirectionName(LightingZoneConfiguration configuration)
    {
        if (configuration.Zone != LightingZone.Keyboard ||
            configuration.Effect != RevoHelper.Core.Control.LightingEffect.Wave) return "None";

        return configuration.Direction switch
        {
            LightingDirection.None => "None",
            LightingDirection.Left => "LeftRight",
            LightingDirection.Right => "RightLeft",
            LightingDirection.Up => "DownUp",
            LightingDirection.Down => "UpDown",
            _ => "None"
        };
    }

    private static string SerializeSpeed(LightingZone zone, int uiSpeed, string? hardwareType)
    {
        // OEM Lightbar/Logo pages send their slider's 1–4 value directly. Only the
        // ITE keyboard protocol uses 7/5/3/1, despite all three reporting MEZone_*.
        var value = zone == LightingZone.Keyboard &&
                    hardwareType?.StartsWith("MEZone_", StringComparison.OrdinalIgnoreCase) == true
            ? uiSpeed switch
            {
                1 => 7,
                2 => 5,
                3 => 3,
                4 => 1,
                _ => uiSpeed
            }
            : uiSpeed;
        return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static IReadOnlyList<LightingRgb> KeyboardPalette(LightingZoneConfiguration configuration)
    {
        if (configuration.Effect is not (
            RevoHelper.Core.Control.LightingEffect.Rainbow or
            RevoHelper.Core.Control.LightingEffect.Colorful or
            RevoHelper.Core.Control.LightingEffect.ColorMarquee))
            return Enumerable.Repeat(configuration.Color, 7).ToArray();

        return
        [
            new(255, 0, 0),
            new(255, 165, 0),
            new(255, 255, 0),
            new(0, 255, 0),
            new(0, 255, 255),
            new(0, 0, 255),
            new(139, 0, 255)
        ];
    }

    private sealed record ActionCommand(string Action);
    private sealed record KeyboardPowerCommand(string function, int powerstatus);
    private sealed record KeyboardEffectCommand(string function, string mode, string effect, string light, string speed,
        string direction, string nv_save, KeyboardColor color, string alphabet);
    private sealed record KeyboardColor(bool isCircular, int ColorBlocks,
        IReadOnlyList<KeyboardColorEntry> ColorBuffer);
    private sealed record KeyboardColorEntry(
        uint ID,
        int R,
        int G,
        int B,
        string SolidColorEnd,
        int R_position,
        int W_position,
        int B_position);
    private sealed record LightingTimerCommand(string Action, int Mins);
    private sealed record WaterModeCommand(string Action, int LedMode);
    private sealed record WaterRgbCommand(string Action, string LCLED_R, string LCLED_G, string LCLED_B);
    private sealed record DeepSleepCommand(string Action, int DeepSleepTime);
    private sealed record BatteryChargeLimitCommand(string Action, string BatteryChargingLimit);
    private sealed record LiquidFanCommand(string Action, string FanCtrl);
    private sealed record LiquidPumpCommand(string Action, string PumpCtrl);
    private sealed record LiquidDeviceCommand(string Action, string DeviceMac);
}
