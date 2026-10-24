using RevoHelper.Core.Control;
using RevoHelper.Hardware.Windows.OemControl;
using System.Text.Json;

namespace RevoHelper.Hardware.Windows.Tests.OemControl;

public sealed class GcuQuickControlsCommandTests
{
    [Theory]
    [InlineData(LightingZone.CoastLightbar, 1)]
    [InlineData(LightingZone.CoastLightbar, 2)]
    [InlineData(LightingZone.CoastLightbar, 3)]
    [InlineData(LightingZone.CoastLightbar, 4)]
    [InlineData(LightingZone.Logo, 1)]
    [InlineData(LightingZone.Logo, 2)]
    [InlineData(LightingZone.Logo, 3)]
    [InlineData(LightingZone.Logo, 4)]
    public void LightbarAndLogoSpeed_UseOfficialDirectLevelsNotKeyboardIteEncoding(LightingZone zone, int speed)
    {
        var configuration = new LightingZoneConfiguration(zone, true, LightingEffect.Breathing,
            new(255, 0, 0), 3, speed, LightingDirection.None, 120);
        foreach (var type in new[] { "MEZone_Lighbar4", "MEZone_Lightbar4", "MEZone_Lightbar4_Logo" })
        {
            using var json = JsonDocument.Parse(GcuQuickControlsCommand.LightingEffect(configuration, type));
            Assert.Equal(speed.ToString(), json.RootElement.GetProperty("speed").GetString());
        }
    }

    [Theory]
    [InlineData(ShortcutControl.WinKey, true, "{\"Action\":\"WINKEY_UNLOCK\"}")]
    [InlineData(ShortcutControl.WinKey, false, "{\"Action\":\"WINKEY_LOCK\"}")]
    [InlineData(ShortcutControl.FnKey, true, "{\"Action\":\"FNKEY_UNLOCK\"}")]
    [InlineData(ShortcutControl.FnKey, false, "{\"Action\":\"FNKEY_LOCK\"}")]
    [InlineData(ShortcutControl.Osd, true, "{\"Action\":\"OSD_HIDDEN_OFF\"}")]
    [InlineData(ShortcutControl.Osd, false, "{\"Action\":\"OSD_HIDDEN_ON\"}")]
    public void Shortcut_UsesOfficialSettingAction(ShortcutControl control, bool enabled, string expected) =>
        Assert.Equal(expected, GcuQuickControlsCommand.Shortcut(control, enabled));

    [Theory]
    [InlineData(SystemToggleControl.Wifi, true, "{\"Action\":\"WIFI_ON\"}")]
    [InlineData(SystemToggleControl.Wifi, false, "{\"Action\":\"WIFI_OFF\"}")]
    [InlineData(SystemToggleControl.Bluetooth, true, "{\"Action\":\"BT_ON\"}")]
    [InlineData(SystemToggleControl.Bluetooth, false, "{\"Action\":\"BT_OFF\"}")]
    [InlineData(SystemToggleControl.Touchpad, true, "{\"Action\":\"TOUCHPAD_ON\"}")]
    [InlineData(SystemToggleControl.Webcam, false, "{\"Action\":\"WEBCAM_OFF\"}")]
    [InlineData(SystemToggleControl.CopilotKey, true, "{\"Action\":\"COPILOTKEY_UNLOCK\"}")]
    [InlineData(SystemToggleControl.CopilotKey, false, "{\"Action\":\"COPILOTKEY_LOCK\"}")]
    public void SystemToggle_UsesOfficialSettingAction(SystemToggleControl control, bool enabled, string expected) =>
        Assert.Equal(expected, GcuQuickControlsCommand.SystemToggle(control, enabled));

    [Theory]
    [InlineData(PowerToggleControl.UsbChargingWhileOff, true, "{\"Action\":\"USB_CHARGER_ON\"}")]
    [InlineData(PowerToggleControl.AcAutoBoot, false, "{\"Action\":\"ACRECOVERY_TOGGLE_OFF\"}")]
    [InlineData(PowerToggleControl.HighPerformancePowerMode, true, "{\"Action\":\"HIGHPERFORMANCEPOWERMODE_ON\"}")]
    [InlineData(PowerToggleControl.DeepSleep, false, "{\"Action\":\"DEEPSLEEP_OFF\"}")]
    public void PowerToggle_UsesOfficialSettingAction(PowerToggleControl control, bool enabled, string expected) =>
        Assert.Equal(expected, GcuQuickControlsCommand.PowerToggle(control, enabled));

    [Fact]
    public void DeepSleep_UsesOfficialActionAndDuration() =>
        Assert.Equal("{\"Action\":\"DEEPSLEEP_ON\",\"DeepSleepTime\":30}", GcuQuickControlsCommand.DeepSleep(true, 30));

    [Fact]
    public void BatteryChargeLimit_UsesHiddenOfficialFanContract() =>
        Assert.Equal("{\"Action\":\"BATTERY_CHARGINGLIMIT_SETTING\",\"BatteryChargingLimit\":\"80\"}",
            GcuQuickControlsCommand.BatteryChargeLimit(80));

    [Theory]
    [InlineData(true, "{\"function\":\"SetPower\",\"powerstatus\":1}")]
    [InlineData(false, "{\"function\":\"SetPower\",\"powerstatus\":0}")]
    public void KeyboardBacklight_UsesOfficialSetPowerPayload(bool enabled, string expected) =>
        Assert.Equal(expected, GcuQuickControlsCommand.KeyboardBacklight(enabled));

    [Fact]
    public void KeyboardEffect_UsesOfficialSetEffectAllContract()
    {
        var payload = GcuQuickControlsCommand.LightingEffect(new(
            LightingZone.Keyboard, true, RevoHelper.Core.Control.LightingEffect.Breathing,
            new(0, 180, 255), 3, 2, LightingDirection.Left, 2));

        using var json = JsonDocument.Parse(payload);
        var root = json.RootElement;
        var color = root.GetProperty("color");

        Assert.Equal("SetEffectALL", root.GetProperty("function").GetString());
        Assert.Equal("Breathing", root.GetProperty("effect").GetString());
        Assert.Equal("3", root.GetProperty("light").GetString());
        Assert.Equal("2", root.GetProperty("speed").GetString());
        Assert.Equal("None", root.GetProperty("direction").GetString());
        Assert.Equal("1", root.GetProperty("nv_save").GetString());
        Assert.Equal(string.Empty, root.GetProperty("alphabet").GetString());
        Assert.Equal(7, color.GetProperty("ColorBlocks").GetInt32());
        Assert.Equal(7, color.GetProperty("ColorBuffer").GetArrayLength());
        var first = color.GetProperty("ColorBuffer")[0];
        Assert.Equal(0u, first.GetProperty("ID").GetUInt32());
        Assert.Equal(0, first.GetProperty("R").GetInt32());
        Assert.Equal(180, first.GetProperty("G").GetInt32());
        Assert.Equal(255, first.GetProperty("B").GetInt32());
        Assert.Equal("00B4FF", first.GetProperty("SolidColorEnd").GetString());
        Assert.Equal(100, first.GetProperty("W_position").GetInt32());
        Assert.Equal(100, first.GetProperty("B_position").GetInt32());
    }

    [Fact]
    public void CoastLightbarEffect_RemainsVolatileAndDoesNotWriteKeyboardFirmwareStorage()
    {
        var payload = GcuQuickControlsCommand.LightingEffect(new(
            LightingZone.CoastLightbar, true, RevoHelper.Core.Control.LightingEffect.Breathing,
            new(0, 180, 255), 3, 2, LightingDirection.None, 120));

        using var json = JsonDocument.Parse(payload);

        Assert.Equal("0", json.RootElement.GetProperty("nv_save").GetString());
    }

    [Theory]
    [InlineData(1, "7")]
    [InlineData(2, "5")]
    [InlineData(3, "3")]
    [InlineData(4, "1")]
    public void IteKeyboardEffect_TranslatesUiSpeedToOfficialRawValue(int uiSpeed, string expectedRawSpeed)
    {
        var payload = GcuQuickControlsCommand.LightingEffect(new(
            LightingZone.Keyboard, true, RevoHelper.Core.Control.LightingEffect.Breathing,
            new(0, 180, 255), 3, uiSpeed, LightingDirection.None, 120),
            "MEZone_3p1nd_101");

        using var json = JsonDocument.Parse(payload);

        Assert.Equal(expectedRawSpeed, json.RootElement.GetProperty("speed").GetString());
    }

    [Fact]
    public void KeyboardMarquee_UsesOfficialMarqueeEffectName()
    {
        var payload = GcuQuickControlsCommand.LightingEffect(new(
            LightingZone.Keyboard, true, RevoHelper.Core.Control.LightingEffect.ColorMarquee,
            new(255, 0, 0), 3, 2, LightingDirection.Left, 2));

        using var json = JsonDocument.Parse(payload);
        Assert.Equal("Marquee", json.RootElement.GetProperty("effect").GetString());
    }

    [Fact]
    public void OemDefault_UsesTheProvenKeyboardPowerCommand() =>
        Assert.Equal("{\"function\":\"SetPower\",\"powerstatus\":1}",
            GcuQuickControlsCommand.LightingEffect(new(
                LightingZone.Keyboard, true, RevoHelper.Core.Control.LightingEffect.OemDefault,
                new(0, 0, 0), 3, 2, LightingDirection.Left, 2)));

    [Fact]
    public void WaterRainbow_UsesOfficialFanLedEffectContract() =>
        Assert.Equal("{\"Action\":\"FanLEDEffect\",\"LedMode\":5}",
            GcuQuickControlsCommand.LightingEffect(new(
                LightingZone.LiquidCooler, true, RevoHelper.Core.Control.LightingEffect.Rainbow,
                new(0, 255, 255), 3, 2, LightingDirection.Left, 2)));

    [Fact]
    public void WaterStatic_UsesOfficialRgbContract() =>
        Assert.Equal("{\"Action\":\"LEDControl\",\"LCLED_R\":\"12\",\"LCLED_G\":\"34\",\"LCLED_B\":\"56\"}",
            GcuQuickControlsCommand.LightingEffect(new(
                LightingZone.LiquidCooler, true, RevoHelper.Core.Control.LightingEffect.Static,
                new(12, 34, 56), 3, 2, LightingDirection.Left, 2)));

    [Theory]
    [InlineData(0, "{\"Action\":\"KEYBOARD_LIGHTBAR_TIMER_OFF\"}")]
    [InlineData(10, "{\"Action\":\"KEYBOARD_LIGHTBAR_TIMER_ON\",\"Mins\":10}")]
    [InlineData(7200, "{\"Action\":\"KEYBOARD_LIGHTBAR_TIMER_ON\",\"Mins\":7200}")]
    public void LightingSleepTimer_UsesSharedOfficialSettingContract(int minutes, string expected) =>
        Assert.Equal(expected, GcuQuickControlsCommand.LightingSleepTimer(minutes));

    [Theory]
    [InlineData(BatteryProtectionMode.Performance, "{\"Action\":\"PERFORMANCEDMODE\"}")]
    [InlineData(BatteryProtectionMode.Balanced, "{\"Action\":\"BALANCEDMODE\"}")]
    [InlineData(BatteryProtectionMode.Healthy, "{\"Action\":\"HEALTHYMODE\"}")]
    public void BatteryProtection_UsesOfficialAction(BatteryProtectionMode mode, string expected) =>
        Assert.Equal(expected, GcuQuickControlsCommand.BatteryProtection(mode));

    [Theory]
    [InlineData(DisplayColorMode.Default, "{\"Action\":\"COLOR_CALIBRATION_ON_DEFAULT\"}")]
    [InlineData(DisplayColorMode.Srgb, "{\"Action\":\"COLOR_CALIBRATION_ON_SRGB\"}")]
    [InlineData(DisplayColorMode.P3, "{\"Action\":\"COLOR_CALIBRATION_ON_P3\"}")]
    [InlineData(DisplayColorMode.AdobeRgb, "{\"Action\":\"COLOR_CALIBRATION_ON_ADOBERGB\"}")]
    public void DisplayColor_UsesOfficialAction(DisplayColorMode mode, string expected) =>
        Assert.Equal(expected, GcuQuickControlsCommand.DisplayColor(mode));

    [Fact]
    public void DisplayColorDisabled_UsesOfficialOffAction() =>
        Assert.Equal("{\"Action\":\"COLOR_CALIBRATION_OFF\"}", GcuQuickControlsCommand.DisplayColorEnabled(false, DisplayColorMode.Default));

    [Theory]
    [InlineData(true, "{\"Action\":\"LOCALDIMMING_ON\"}")]
    [InlineData(false, "{\"Action\":\"LOCALDIMMING_OFF\"}")]
    public void LocalDimming_UsesOfficialAction(bool enabled, string expected) =>
        Assert.Equal(expected, GcuQuickControlsCommand.LocalDimming(enabled));

    [Theory]
    [InlineData(LiquidCoolingFanMode.Quieter, "{\"Action\":\"LC_FanCtrl\",\"FanCtrl\":\"0\"}")]
    [InlineData(LiquidCoolingFanMode.Maximum, "{\"Action\":\"LC_FanCtrl\",\"FanCtrl\":\"3\"}")]
    public void LiquidCoolingFan_UsesOfficialAction(LiquidCoolingFanMode mode, string expected) =>
        Assert.Equal(expected, GcuQuickControlsCommand.LiquidCoolingFan(mode));

    [Theory]
    [InlineData(LiquidCoolingPumpMode.Quiet, "{\"Action\":\"LC_PumpCtrl\",\"PumpCtrl\":\"0\"}")]
    [InlineData(LiquidCoolingPumpMode.Maximum, "{\"Action\":\"LC_PumpCtrl\",\"PumpCtrl\":\"2\"}")]
    public void LiquidCoolingPump_UsesOfficialAction(LiquidCoolingPumpMode mode, string expected) =>
        Assert.Equal(expected, GcuQuickControlsCommand.LiquidCoolingPump(mode));

    [Fact]
    public void LiquidCoolingDeviceSelection_UsesOfficialMacSettingAction() =>
        Assert.Equal("{\"Action\":\"DeviceMacSetting\",\"DeviceMac\":\"CoolingSystem LCT22002 (59:05)\"}",
            GcuQuickControlsCommand.LiquidCoolingDevice("CoolingSystem LCT22002 (59:05)"));

    [Theory]
    [InlineData(LiquidCoolingDeviceAction.Connect, "{\"Action\":\"Connect\"}")]
    [InlineData(LiquidCoolingDeviceAction.Disconnect, "{\"Action\":\"Disconnect\"}")]
    [InlineData(LiquidCoolingDeviceAction.InputWater, "{\"Action\":\"InputWater\"}")]
    [InlineData(LiquidCoolingDeviceAction.Reset, "{\"Action\":\"ClearDevMAC\"}")]
    public void LiquidCoolingDeviceAction_UsesOfficialAction(LiquidCoolingDeviceAction action, string expected) =>
        Assert.Equal(expected, GcuQuickControlsCommand.LiquidCoolingAction(action));
}
