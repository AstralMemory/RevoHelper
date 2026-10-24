using System.Globalization;
using System.Text.Json;
using RevoHelper.Core.Control;

namespace RevoHelper.Hardware.Windows.OemControl;

public static class GcuQuickControlsStatusParser
{
    public static ShortcutState? ParseShortcuts(string payload)
    {
        using var document = TryParse(payload);
        if (document is null) return null;
        var winKey = ReadShortcut(document.RootElement, ShortcutControl.WinKey);
        var fnKey = ReadShortcut(document.RootElement, ShortcutControl.FnKey);
        var osd = ReadShortcut(document.RootElement, ShortcutControl.Osd);
        // The public state has non-nullable booleans. A sparse OEM packet cannot
        // populate it without inventing the values of unrelated switches.
        return winKey is { } win && fnKey is { } fn && osd is { } visible
            ? new(win, fn, visible) : null;
    }

    public static bool? ParseShortcut(string payload, ShortcutControl control)
    {
        using var document = TryParse(payload);
        return document is null ? null : ReadShortcut(document.RootElement, control);
    }

    private static bool? ReadShortcut(JsonElement root, ShortcutControl control)
    {
        var (field, enabled, disabled) = control switch
        {
            ShortcutControl.WinKey => ("WinKey", "WINKEY_UNLOCK", "WINKEY_LOCK"),
            ShortcutControl.FnKey => ("FnKey", "FNKEY_UNLOCK", "FNKEY_LOCK"),
            ShortcutControl.Osd => ("OSD", "OSD_HIDDEN_OFF", "OSD_HIDDEN_ON"),
            _ => ("", "", "")
        };
        if (field.Length == 0) return null;
        var value = FindString(root, field);
        return string.Equals(value, enabled, StringComparison.OrdinalIgnoreCase) ? true
            : string.Equals(value, disabled, StringComparison.OrdinalIgnoreCase) ? false : null;
    }

    public static SystemToggleState? ParseSystemToggles(string payload)
    {
        using var document = TryParse(payload);
        if (document is null) return null;
        var wifi = ParseSwitch(FindString(document.RootElement, "WIFIEnable"));
        var bluetooth = ParseSwitch(FindString(document.RootElement, "BTEnable"));
        var touchpad = ParseSwitch(FindString(document.RootElement, "TochpadEnable"));
        var webcam = ParseSwitch(FindString(document.RootElement, "WebCamEnable"));
        var copilot = ParseUnlockedSwitch(FindString(document.RootElement, "CopilotKey"));
        return wifi is null && bluetooth is null && touchpad is null && webcam is null && copilot is null
            ? null
            : new(wifi, bluetooth, touchpad, webcam, copilot);
    }

    public static SystemToggleState? MergeSystemToggles(SystemToggleState? deviceState, SystemToggleState? settingState)
    {
        if (deviceState is null) return settingState;
        if (settingState is null) return deviceState;
        return new(
            deviceState.WifiEnabled ?? settingState.WifiEnabled,
            deviceState.BluetoothEnabled ?? settingState.BluetoothEnabled,
            deviceState.TouchpadEnabled ?? settingState.TouchpadEnabled,
            deviceState.WebcamEnabled ?? settingState.WebcamEnabled,
            settingState.CopilotKeyEnabled ?? deviceState.CopilotKeyEnabled);
    }

    public static PowerSettingsState? ParsePowerSettings(string payload, int? batteryChargeLimitPercent = null)
    {
        using var document = TryParse(payload);
        if (document is null) return batteryChargeLimitPercent is null ? null : new(null, false, null, null, null, null, batteryChargeLimitPercent);
        var usb = ParseSwitch(FindString(document.RootElement, "UsbCharger"));
        var acSupport = ParseSwitch(FindString(document.RootElement, "AcRecoverySwitch_Support")) ?? false;
        var ac = ParseSwitch(FindString(document.RootElement, "AcRecoverySwitch_Status"));
        var performance = ParseSwitch(FindString(document.RootElement, "HighPerformancePowerModeSwitch"));
        var deepSleep = ParseSwitch(FindString(document.RootElement, "DeepSleepSwitch"));
        var deepSleepMinutes = FindInt(document.RootElement, "DeepSleepTime");
        if (usb is null && !acSupport && ac is null && performance is null && deepSleep is null &&
            deepSleepMinutes is null && batteryChargeLimitPercent is null) return null;
        return new(usb, acSupport, ac, performance, deepSleep, deepSleepMinutes, batteryChargeLimitPercent);
    }

    public static KeyboardLightingState? ParseKeyboardLighting(string payload)
    {
        using var document = TryParse(payload);
        if (document is null) return null;
        var power = FindString(document.RootElement, "powerStatus");
        var type = FindString(document.RootElement, "type");
        return power is null ? null : new(string.Equals(power, "On", StringComparison.OrdinalIgnoreCase), type);
    }

    public static LightingZoneState? ParseLightingZone(LightingZone zone, string payload)
    {
        using var document = TryParse(payload);
        if (document is null) return null;

        if (zone == LightingZone.LiquidCooler)
        {
            var connected = FindBool(document.RootElement, "connected") ?? FindBool(document.RootElement, "Connected");
            var ledMode = FindInt(document.RootElement, "LCLED_Mode");
            var fanLedMode = FindInt(document.RootElement, "LCFanLED_Mode");
            var red = FindInt(document.RootElement, "LCLED_R");
            var green = FindInt(document.RootElement, "LCLED_G");
            var blue = FindInt(document.RootElement, "LCLED_B");
            if (ledMode is null && fanLedMode is null && red is null && green is null && blue is null) return null;
            var waterEffect = fanLedMode switch
            {
                4 => RevoHelper.Core.Control.LightingEffect.Rotate,
                5 => RevoHelper.Core.Control.LightingEffect.Rainbow,
                _ => ledMode switch
                {
                    1 => RevoHelper.Core.Control.LightingEffect.Breathing,
                    2 => RevoHelper.Core.Control.LightingEffect.Colorful,
                    _ => RevoHelper.Core.Control.LightingEffect.Static
                }
            };
            var hasColor = red is not null && green is not null && blue is not null;
            var hasModes = ledMode is not null && fanLedMode is not null;
            var hasActiveOutput = (ledMode ?? 0) != 0 || (fanLedMode ?? 0) != 0 ||
                                  (red ?? 0) != 0 || (green ?? 0) != 0 || (blue ?? 0) != 0;
            var waterEnabled = connected != false && hasActiveOutput;
            var waterFields = LightingReportedFields.None;
            // A single zero channel/mode does not prove the entire lamp is off.
            if (connected == false || hasActiveOutput || (hasModes && hasColor)) waterFields |= LightingReportedFields.Power;
            if (hasModes || fanLedMode is 4 or 5 || ledMode is 1 or 2) waterFields |= LightingReportedFields.Effect;
            if (hasColor) waterFields |= LightingReportedFields.Color;
            return new(
                connected ?? false,
                FindString(document.RootElement, "DevFWVersion"),
                new(zone, waterEnabled, waterEffect, new(ClampByte(red), ClampByte(green), ClampByte(blue)),
                    3, 2, LightingDirection.None, 120),
                EffectsFor(zone, null),
                hasModes && hasColor,
                waterFields,
                HasAvailability: connected is not null);
        }

        var type = FindString(document.RootElement, "type");
        var power = FindString(document.RootElement, "powerStatus") ?? FindString(document.RootElement, "powerstatus");
        var effectText = FindString(document.RootElement, "effect");
        var light = FindInt(document.RootElement, "light") ?? FindInt(document.RootElement, "brightNess");
        var directionText = FindString(document.RootElement, "direction");
        var fields = LightingReportedFields.None;
        if (power is not null) fields |= LightingReportedFields.Power;
        if (effectText is not null) fields |= LightingReportedFields.Effect;
        if (light is not null) fields |= LightingReportedFields.Brightness;
        if (directionText is not null) fields |= LightingReportedFields.Direction;
        if (FindInt(document.RootElement, "speed") is not null) fields |= LightingReportedFields.Speed;
        if (TryParseLightingColor(document.RootElement, out var color)) fields |= LightingReportedFields.Color;
        if (type is null && fields == LightingReportedFields.None) return null;
        var hasDetailedConfiguration = fields == LightingReportedFields.All;
        var enabled = ParseSwitch(power) ?? !string.Equals(power, "Off", StringComparison.OrdinalIgnoreCase);
        var parsedEffect = ParseLightingEffect(effectText) ?? RevoHelper.Core.Control.LightingEffect.Static;
        var direction = ParseLightingDirection(directionText);
        var rawSpeed = FindInt(document.RootElement, "speed");
        var speed = zone == LightingZone.Keyboard && HardwareTypeIsIte(type)
            ? TranslateIteSpeed(rawSpeed)
            : Math.Clamp(rawSpeed ?? 2, 1, 4);
        return new(
            true,
            type,
            new(zone, enabled, parsedEffect,
                color,
                Math.Clamp(light ?? 3, 0, 4),
                speed,
                direction,
                120),
            type is null ? [] : EffectsFor(zone, type),
            hasDetailedConfiguration,
            fields,
            RawSpeed: rawSpeed);
    }

    private static bool TryParseLightingColor(JsonElement root, out LightingRgb color)
    {
        var red = FindInt(root, "R");
        var green = FindInt(root, "G");
        var blue = FindInt(root, "B");
        if (red is not null && green is not null && blue is not null)
        {
            color = new(ClampByte(red), ClampByte(green), ClampByte(blue));
            return true;
        }

        var encoded = FindString(root, "rkgcolor");
        if (!string.IsNullOrWhiteSpace(encoded))
        {
            using var colorDocument = TryParse(encoded);
            if (colorDocument is not null)
            {
                return TryParseLightingColor(colorDocument.RootElement, out color);
            }
        }

        color = new(0, 0, 0);
        return false;
    }

    private static bool HardwareTypeIsIte(string? hardwareType) =>
        hardwareType?.StartsWith("MEZone_", StringComparison.OrdinalIgnoreCase) == true;

    private static int TranslateIteSpeed(int? rawSpeed) => rawSpeed switch
    {
        7 => 1,
        5 => 2,
        3 => 3,
        1 => 4,
        _ => 1
    };

    public static int? ParseLightingSleepTimer(string payload)
    {
        using var document = TryParse(payload);
        return document is null ? null : FindInt(document.RootElement, "CloseTimer");
    }

    public static BatteryProtectionState? ParseBatteryProtection(string payload)
    {
        using var document = TryParse(payload);
        if (document is null) return null;
        var mode = FindInt(document.RootElement, "HealthProtectionStatus");
        if (mode is not 0 and not 1 and not 2) return null;
        return new((BatteryProtectionMode)mode.Value, FindInt(document.RootElement, "BatteryPercent"));
    }

    public static DisplayFeatureState? ParseDisplayFeatures(string payload)
    {
        using var document = TryParse(payload);
        if (document is null) return null;
        var colorStatus = FindString(document.RootElement, "ColorCalibrationStatus") ??
                          FindString(document.RootElement, "sColorCalibration_Status") ??
                          FindString(document.RootElement, "ColorCalibration");
        var colorSwitch = FindString(document.RootElement, "ColorCalibrationSwitch") ??
                          FindString(document.RootElement, "sColorCalibrationSwitch");
        var colorCurrent = FindString(document.RootElement, "CurrentColorCalibration") ??
                           FindString(document.RootElement, "sCurrentColorCalibration");
        var colorSupport = FindString(document.RootElement, "ColorCalibrationSupport") ??
                           FindString(document.RootElement, "SupportColorCalibration") ??
                           FindString(document.RootElement, "isSupportColorCalibration");
        var colorResultCode = FindString(document.RootElement, "ColorCalibrationResultCode") ??
                              FindString(document.RootElement, "sColorCalibrationResultCode");
        var local = FindString(document.RootElement, "LocalDimmingStatus") ??
                    FindString(document.RootElement, "sLocalDimming_Status") ??
                    FindString(document.RootElement, "LocalDimmingSwitch") ??
                    FindString(document.RootElement, "LocalDimming");
        if (colorStatus is null && colorSwitch is null && colorCurrent is null && colorSupport is null &&
            colorResultCode is null && local is null) return null;
        var colorEnabled = ParseSwitch(colorSwitch ?? colorStatus);
        var localEnabled = ParseSwitch(local);
        return new(
            colorEnabled,
            ParseColorMode(colorCurrent ?? colorStatus),
            localEnabled,
            ParseSwitch(colorSupport) ?? (colorStatus is not null || colorSwitch is not null || colorCurrent is not null),
            local is not null,
            colorResultCode);
    }

    public static LiquidCoolingState? ParseLiquidCooling(string payload)
    {
        using var document = TryParse(payload);
        if (document is null) return null;
        var connected = FindBool(document.RootElement, "connected") ?? FindBool(document.RootElement, "Connected");
        var connection = FindString(document.RootElement, "ConnectString");
        var fanMode = FindInt(document.RootElement, "LC_FanCtrl");
        var pumpMode = FindInt(document.RootElement, "LC_PumpCtrl");
        var devices = FindStringArray(document.RootElement, "DeviceMacList")
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(value => new LiquidCoolingDevice(value))
            .ToArray();
        var selectedDevice = FindString(document.RootElement, "DevMACString");
        var firmwareVersion = FindString(document.RootElement, "DevFWVersion");
        var canControl = FindBool(document.RootElement, "LC_action");
        var isPriming = FindBool(document.RootElement, "LC_InputWater");
        var meterNormal = FindBool(document.RootElement, "LC_MeterNormal");
        if (connected is null && connection is null && fanMode is null && pumpMode is null && devices.Length == 0 &&
            string.IsNullOrWhiteSpace(selectedDevice) && string.IsNullOrWhiteSpace(firmwareVersion) &&
            canControl is null && isPriming is null && meterNormal is null) return null;
        return new(
            connected ?? string.Equals(connection, "Connected", StringComparison.OrdinalIgnoreCase),
            connection,
            fanMode is >= 0 and <= 4 ? (LiquidCoolingFanMode)fanMode.Value : null,
            pumpMode is >= 0 and <= 2 ? (LiquidCoolingPumpMode)pumpMode.Value : null,
            FindInt(document.RootElement, "FanDuty"),
            FindInt(document.RootElement, "PumpDuty"),
            devices,
            selectedDevice,
            firmwareVersion,
            canControl ?? false,
            isPriming ?? false,
            meterNormal);
    }

    private static JsonDocument? TryParse(string payload)
    {
        try { return JsonDocument.Parse(payload); }
        catch (JsonException) { return null; }
    }

    private static bool Contains(string? value, string token) => value?.Contains(token, StringComparison.OrdinalIgnoreCase) == true;

    private static bool? ParseSwitch(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (bool.TryParse(value, out var boolean)) return boolean;
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)) return number != 0;
        if (Contains(value, "_OFF") || string.Equals(value, "OFF", StringComparison.OrdinalIgnoreCase)) return false;
        if (Contains(value, "_ON") || string.Equals(value, "ON", StringComparison.OrdinalIgnoreCase)) return true;
        return null;
    }

    private static bool? ParseUnlockedSwitch(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (Contains(value, "UNLOCK")) return true;
        if (Contains(value, "LOCK")) return false;
        return ParseSwitch(value);
    }

    private static RevoHelper.Core.Control.LightingEffect? ParseLightingEffect(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric))
        {
            return numeric switch
            {
                1 => RevoHelper.Core.Control.LightingEffect.Static,
                2 => RevoHelper.Core.Control.LightingEffect.Breathing,
                3 => RevoHelper.Core.Control.LightingEffect.Wave,
                4 => RevoHelper.Core.Control.LightingEffect.Reactive,
                5 => RevoHelper.Core.Control.LightingEffect.Rainbow,
                6 => RevoHelper.Core.Control.LightingEffect.Ripple,
                9 => RevoHelper.Core.Control.LightingEffect.ColorMarquee,
                10 => RevoHelper.Core.Control.LightingEffect.Raindrop,
                14 => RevoHelper.Core.Control.LightingEffect.Aurora,
                17 => RevoHelper.Core.Control.LightingEffect.Fireworks,
                19 => RevoHelper.Core.Control.LightingEffect.Colorful,
                21 => RevoHelper.Core.Control.LightingEffect.Game,
                _ => null
            };
        }
        if (string.Equals(value, "Single", StringComparison.OrdinalIgnoreCase)) return RevoHelper.Core.Control.LightingEffect.Static;
        if (string.Equals(value, "Marquee", StringComparison.OrdinalIgnoreCase)) return RevoHelper.Core.Control.LightingEffect.ColorMarquee;
        if (string.Equals(value, "Spark", StringComparison.OrdinalIgnoreCase)) return RevoHelper.Core.Control.LightingEffect.Fireworks;
        if (string.Equals(value, "Gaming", StringComparison.OrdinalIgnoreCase)) return RevoHelper.Core.Control.LightingEffect.Game;
        if (string.Equals(value, "Mix", StringComparison.OrdinalIgnoreCase)) return RevoHelper.Core.Control.LightingEffect.Colorful;
        return Enum.TryParse<RevoHelper.Core.Control.LightingEffect>(value, true, out var effect) ? effect : null;
    }

    private static LightingDirection ParseLightingDirection(string? value) => value?.ToUpperInvariant() switch
    {
        "0" or "NONE" => LightingDirection.None,
        "1" => LightingDirection.Left,
        "2" => LightingDirection.Right,
        "3" => LightingDirection.Up,
        "4" => LightingDirection.Down,
        "RIGHT" or "RIGHTLEFT" => LightingDirection.Right,
        "UP" or "DOWNUP" => LightingDirection.Up,
        "DOWN" or "UPDOWN" => LightingDirection.Down,
        _ => LightingDirection.None
    };

    private static IReadOnlyList<RevoHelper.Core.Control.LightingEffect> EffectsFor(
        LightingZone zone,
        string? hardwareType) => zone switch
        {
            LightingZone.Keyboard => KeyboardEffectsFor(hardwareType),
            LightingZone.CoastLightbar => CoastLightbarEffectsFor(hardwareType),
            LightingZone.Logo => LogoEffectsFor(hardwareType),
            LightingZone.LiquidCooler =>
            [
                RevoHelper.Core.Control.LightingEffect.Static, RevoHelper.Core.Control.LightingEffect.Breathing,
            RevoHelper.Core.Control.LightingEffect.Colorful, RevoHelper.Core.Control.LightingEffect.Rotate,
            RevoHelper.Core.Control.LightingEffect.Rainbow
            ],
            _ => []
        };

    private static IReadOnlyList<RevoHelper.Core.Control.LightingEffect> KeyboardEffectsFor(string? hardwareType)
    {
        if (string.Equals(hardwareType, "FourZone", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                RevoHelper.Core.Control.LightingEffect.OemDefault,
                RevoHelper.Core.Control.LightingEffect.Static,
                RevoHelper.Core.Control.LightingEffect.Breathing,
                RevoHelper.Core.Control.LightingEffect.Wave,
                RevoHelper.Core.Control.LightingEffect.Rainbow,
                RevoHelper.Core.Control.LightingEffect.Colorful
            ];
        }

        if (string.Equals(hardwareType, "PerKey", StringComparison.OrdinalIgnoreCase) ||
            hardwareType?.StartsWith("MEZone_", StringComparison.OrdinalIgnoreCase) == true &&
            !hardwareType.Contains("Lightbar", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                RevoHelper.Core.Control.LightingEffect.OemDefault,
                RevoHelper.Core.Control.LightingEffect.Static,
                RevoHelper.Core.Control.LightingEffect.Breathing,
                RevoHelper.Core.Control.LightingEffect.Wave,
                RevoHelper.Core.Control.LightingEffect.Reactive,
                RevoHelper.Core.Control.LightingEffect.Rainbow,
                RevoHelper.Core.Control.LightingEffect.Ripple,
                RevoHelper.Core.Control.LightingEffect.Raindrop,
                RevoHelper.Core.Control.LightingEffect.ColorMarquee,
                RevoHelper.Core.Control.LightingEffect.Fireworks,
                RevoHelper.Core.Control.LightingEffect.Aurora,
                RevoHelper.Core.Control.LightingEffect.Game
            ];
        }

        return [RevoHelper.Core.Control.LightingEffect.OemDefault];
    }

    private static IReadOnlyList<RevoHelper.Core.Control.LightingEffect> CoastLightbarEffectsFor(string? hardwareType)
    {
        if (string.Equals(hardwareType, "MEZone_Lightbar4", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(hardwareType, "MEZone_Lighbar4", StringComparison.OrdinalIgnoreCase))
            return [RevoHelper.Core.Control.LightingEffect.Static, RevoHelper.Core.Control.LightingEffect.Breathing,
                RevoHelper.Core.Control.LightingEffect.Wave, RevoHelper.Core.Control.LightingEffect.Impact,
                RevoHelper.Core.Control.LightingEffect.Raindrop];
        if (string.Equals(hardwareType, "MEZone_Lightbar3", StringComparison.OrdinalIgnoreCase))
            return [RevoHelper.Core.Control.LightingEffect.Static, RevoHelper.Core.Control.LightingEffect.Breathing];
        if (string.Equals(hardwareType, "MEZone_Lightbar", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(hardwareType, "MEZone_Lightbar2", StringComparison.OrdinalIgnoreCase))
            return [RevoHelper.Core.Control.LightingEffect.Static, RevoHelper.Core.Control.LightingEffect.Breathing,
                RevoHelper.Core.Control.LightingEffect.Wave, RevoHelper.Core.Control.LightingEffect.Rainbow,
                RevoHelper.Core.Control.LightingEffect.Colorful, RevoHelper.Core.Control.LightingEffect.Impact,
                RevoHelper.Core.Control.LightingEffect.Raindrop];
        return [RevoHelper.Core.Control.LightingEffect.Static];
    }

    private static IReadOnlyList<RevoHelper.Core.Control.LightingEffect> LogoEffectsFor(string? hardwareType) =>
        hardwareType?.Contains("Logo", StringComparison.OrdinalIgnoreCase) == true
            ? [RevoHelper.Core.Control.LightingEffect.Static, RevoHelper.Core.Control.LightingEffect.Breathing,
                RevoHelper.Core.Control.LightingEffect.Colorful]
            : string.Equals(hardwareType, "MEZone_Lightbar4", StringComparison.OrdinalIgnoreCase) ||
              string.Equals(hardwareType, "MEZone_Lighbar4", StringComparison.OrdinalIgnoreCase)
                ? [RevoHelper.Core.Control.LightingEffect.Static, RevoHelper.Core.Control.LightingEffect.Breathing]
            : [RevoHelper.Core.Control.LightingEffect.Static];

    private static byte ClampByte(int? value) => (byte)Math.Clamp(value ?? 0, byte.MinValue, byte.MaxValue);

    private static DisplayColorMode? ParseColorMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric))
        {
            return numeric is >= 0 and <= 3 ? (DisplayColorMode)numeric : null;
        }
        if (ParseSwitch(value) == false) return null;
        if (Contains(value, "ADOBERGB")) return DisplayColorMode.AdobeRgb;
        if (Contains(value, "SRGB")) return DisplayColorMode.Srgb;
        if (Contains(value, "P3")) return DisplayColorMode.P3;
        return DisplayColorMode.Default;
    }

    private static string? FindString(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) return property.Value.ToString();
                if (FindString(property.Value, name) is { } nested) return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) if (FindString(item, name) is { } nested) return nested;
        }
        return null;
    }

    private static int? FindInt(JsonElement element, string name)
    {
        var value = FindString(element, name);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;
    }

    private static bool? FindBool(JsonElement element, string name)
    {
        var value = FindString(element, name);
        return bool.TryParse(value, out var result) ? result : null;
    }

    private static IReadOnlyList<string> FindStringArray(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.Array)
                {
                    return property.Value.EnumerateArray()
                        .Where(item => item.ValueKind == JsonValueKind.String)
                        .Select(item => item.GetString() ?? string.Empty)
                        .ToArray();
                }
                var nested = FindStringArray(property.Value, name);
                if (nested.Count > 0) return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = FindStringArray(item, name);
                if (nested.Count > 0) return nested;
            }
        }
        return [];
    }
}
