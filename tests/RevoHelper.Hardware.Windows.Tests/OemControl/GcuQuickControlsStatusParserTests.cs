using RevoHelper.Core.Control;
using RevoHelper.Hardware.Windows.OemControl;

namespace RevoHelper.Hardware.Windows.Tests.OemControl;

public sealed class GcuQuickControlsStatusParserTests
{
    [Theory]
    [InlineData("{\"WinKey\":\"WINKEY_UNLOCK\"}")]
    [InlineData("{\"WinKey\":\"WINKEY_UNLOCK\",\"FnKey\":\"FNKEY_UNLOCK\",\"OSD\":\"unknown\"}")]
    public void ParseShortcuts_DoesNotInventMissingOrUnrecognizedStates(string payload)
    {
        Assert.Null(GcuQuickControlsStatusParser.ParseShortcuts(payload));
    }

    [Theory]
    [InlineData(LightingZone.CoastLightbar, 1)]
    [InlineData(LightingZone.CoastLightbar, 2)]
    [InlineData(LightingZone.CoastLightbar, 3)]
    [InlineData(LightingZone.CoastLightbar, 4)]
    [InlineData(LightingZone.Logo, 1)]
    [InlineData(LightingZone.Logo, 2)]
    [InlineData(LightingZone.Logo, 3)]
    [InlineData(LightingZone.Logo, 4)]
    public void LightbarAndLogoSpeed_FullAndPartialReadbackKeepDirectLevel(LightingZone zone, int speed)
    {
        var complete = GcuQuickControlsStatusParser.ParseLightingZone(zone,
            $$"""{"type":"MEZone_Lighbar4","powerStatus":"On","effect":"Breathing","speed":{{speed}}}""");
        Assert.Equal(speed, complete!.Configuration!.Speed);
        var partial = GcuQuickControlsStatusParser.ParseLightingZone(zone, $$"""{"speed":{{speed}}}""");
        Assert.Equal(speed, LightingStateAccumulator.Merge(complete, partial)!.Configuration!.Speed);
    }

    [Theory]
    [InlineData(7, 1)]
    [InlineData(5, 2)]
    [InlineData(3, 3)]
    [InlineData(1, 4)]
    public void ParseItePartialSpeed_UsesRetainedHardwareType(int raw, int expected)
    {
        var baseline = GcuQuickControlsStatusParser.ParseLightingZone(LightingZone.Keyboard,
            "{\"type\":\"MEZone_3p1nd_101\",\"powerStatus\":\"On\",\"effect\":\"Single\"}");
        var partial = GcuQuickControlsStatusParser.ParseLightingZone(LightingZone.Keyboard,
            $$"""{"speed":{{raw}}}""");
        Assert.Equal(expected, LightingStateAccumulator.Merge(baseline, partial)!.Configuration!.Speed);
    }

    [Theory]
    [InlineData("{\"LCLED_Mode\":0}")]
    [InlineData("{\"LCLED_R\":0}")]
    public void ParseWaterPartialStatus_DoesNotInventBlackPowerOffOrDisconnect(string payload)
    {
        var config = new LightingZoneConfiguration(LightingZone.LiquidCooler, true, LightingEffect.Static,
            new(36, 214, 106), 3, 2, LightingDirection.None, 120);
        var baseline = new LightingZoneState(true, "water", config, [LightingEffect.Static]);
        var partial = GcuQuickControlsStatusParser.ParseLightingZone(LightingZone.LiquidCooler, payload);
        var merged = LightingStateAccumulator.Merge(baseline, partial);

        Assert.NotNull(partial);
        Assert.True(merged!.Available);
        Assert.Equal(config, merged.Configuration);
    }

    [Fact]
    public void ParseLightingZone_LogoPowerOnlyStatus_IsMarkedAsSparse()
    {
        var state = GcuQuickControlsStatusParser.ParseLightingZone(
            LightingZone.Logo,
            "{\"powerStatus\":\"On\",\"type\":\"MEZone_Lighbar4\"}");

        Assert.NotNull(state);
        Assert.False(state.HasDetailedConfiguration);
        Assert.True(state.Configuration?.Enabled);
    }
    [Fact]
    public void ParseLightingZone_ReadsKeyboardEffectAndCapability()
    {
        var state = GcuQuickControlsStatusParser.ParseLightingZone(LightingZone.Keyboard, """
            { "powerStatus":"On", "type":"PerKey", "effect":"Wave", "light":4,
              "speed":3, "direction":"Right", "color":{"R":18,"G":52,"B":86} }
            """);

        Assert.True(state!.Available);
        Assert.Equal("PerKey", state.HardwareType);
        Assert.Equal(RevoHelper.Core.Control.LightingEffect.Wave, state.Configuration!.Effect);
        Assert.Equal("#123456", state.Configuration.Color.Hex);
        Assert.Contains(RevoHelper.Core.Control.LightingEffect.Rainbow, state.SupportedEffects);
    }

    [Fact]
    public void ParseLightingZone_ReadsIteRkgColorAndBrightnessFallbackFromRealMachineStatus()
    {
        var state = GcuQuickControlsStatusParser.ParseLightingZone(LightingZone.Keyboard, """
            {
              "powerStatus":"On",
              "type":"MEZone_3p1nd_101",
              "brightNess":"4",
              "effect":"5",
              "speed":"3",
              "direction":"0",
              "rkgcolor":"{\"isCircular\":true,\"ColorBlocks\":2,\"ColorBuffer\":[{\"ID\":0,\"R\":255,\"G\":0,\"B\":0},{\"ID\":1,\"R\":0,\"G\":255,\"B\":0}]}"
            }
            """);

        Assert.Equal(4, state!.Configuration!.Brightness);
        Assert.Equal("#FF0000", state.Configuration.Color.Hex);
        Assert.Equal(RevoHelper.Core.Control.LightingEffect.Rainbow, state.Configuration.Effect);
    }

    [Fact]
    public void ParseLightingZone_ReadsWaterLightModes()
    {
        var state = GcuQuickControlsStatusParser.ParseLightingZone(LightingZone.LiquidCooler, """
            { "connected":true, "LCLED_Mode":1, "LCFanLED_Mode":0,
              "LCLED_R":"0", "LCLED_G":"176", "LCLED_B":"255" }
            """);

        Assert.True(state!.Available);
        Assert.Equal(RevoHelper.Core.Control.LightingEffect.Breathing, state.Configuration!.Effect);
        Assert.Equal("#00B0FF", state.Configuration.Color.Hex);
        Assert.Contains(RevoHelper.Core.Control.LightingEffect.Rotate, state.SupportedEffects);
    }

    [Fact]
    public void ParseLightingZone_BlackWaterWithNoActiveModeIsOffEvenWhenCoolerIsConnected()
    {
        var state = GcuQuickControlsStatusParser.ParseLightingZone(LightingZone.LiquidCooler, """
            { "connected":true, "LCLED_Mode":0, "LCFanLED_Mode":0,
              "LCLED_R":0, "LCLED_G":0, "LCLED_B":0 }
            """);

        Assert.True(state!.Available);
        Assert.False(state.Configuration!.Enabled);
    }

    [Fact]
    public void ParseLightingZone_UnknownKeyboardTypeOffersOnlySafeOemDefault()
    {
        var state = GcuQuickControlsStatusParser.ParseLightingZone(LightingZone.Keyboard,
            "{\"powerStatus\":\"On\",\"type\":\"UnknownFutureKeyboard\",\"effect\":\"Single\"}");

        Assert.Equal([RevoHelper.Core.Control.LightingEffect.OemDefault], state!.SupportedEffects);
    }

    [Fact]
    public void ParseLightingZone_FourZoneKeyboardUsesItsSmallerOfficialEffectSet()
    {
        var state = GcuQuickControlsStatusParser.ParseLightingZone(LightingZone.Keyboard,
            "{\"powerStatus\":\"On\",\"type\":\"FourZone\",\"effect\":\"Single\"}");

        Assert.Contains(RevoHelper.Core.Control.LightingEffect.Colorful, state!.SupportedEffects);
        Assert.DoesNotContain(RevoHelper.Core.Control.LightingEffect.Reactive, state.SupportedEffects);
    }

    [Theory]
    [InlineData("1", RevoHelper.Core.Control.LightingEffect.Static)]
    [InlineData("2", RevoHelper.Core.Control.LightingEffect.Breathing)]
    [InlineData("5", RevoHelper.Core.Control.LightingEffect.Rainbow)]
    [InlineData("9", RevoHelper.Core.Control.LightingEffect.ColorMarquee)]
    [InlineData("10", RevoHelper.Core.Control.LightingEffect.Raindrop)]
    [InlineData("14", RevoHelper.Core.Control.LightingEffect.Aurora)]
    [InlineData("17", RevoHelper.Core.Control.LightingEffect.Fireworks)]
    [InlineData("21", RevoHelper.Core.Control.LightingEffect.Game)]
    public void ParseLightingZone_MapsOfficialIteNumericEffectReadback(
        string rawEffect,
        RevoHelper.Core.Control.LightingEffect expected)
    {
        var state = GcuQuickControlsStatusParser.ParseLightingZone(LightingZone.Keyboard,
            $$"""{"powerStatus":"On","type":"MEZone_3p1nd_101","effect":"{{rawEffect}}"}""");

        Assert.Equal(expected, state!.Configuration!.Effect);
    }

    [Fact]
    public void ParseLightingZone_RecognizesOfficialLightbar4StatusTypo()
    {
        var state = GcuQuickControlsStatusParser.ParseLightingZone(LightingZone.CoastLightbar,
            "{\"powerStatus\":\"On\",\"type\":\"MEZone_Lighbar4\"}");

        Assert.Contains(RevoHelper.Core.Control.LightingEffect.Wave, state!.SupportedEffects);
        Assert.Contains(RevoHelper.Core.Control.LightingEffect.Impact, state.SupportedEffects);
    }

    [Fact]
    public void ParseLightingZone_RecognizesLightbar4BackedLogoCapability()
    {
        var state = GcuQuickControlsStatusParser.ParseLightingZone(LightingZone.Logo,
            "{\"powerStatus\":\"Off\",\"type\":\"MEZone_Lighbar4\"}");

        Assert.Contains(RevoHelper.Core.Control.LightingEffect.Breathing, state!.SupportedEffects);
    }

    [Fact]
    public void ParseLightingZone_TranslatesIteRawSpeedAndDirection()
    {
        var state = GcuQuickControlsStatusParser.ParseLightingZone(LightingZone.Keyboard,
            "{\"powerStatus\":\"On\",\"type\":\"MEZone_3p1nd_101\",\"effect\":\"3\",\"speed\":\"5\",\"direction\":\"2\"}");

        Assert.Equal(2, state!.Configuration!.Speed);
        Assert.Equal(LightingDirection.Right, state.Configuration.Direction);
        Assert.Equal(120, state.Configuration.SleepSeconds);
    }

    [Fact]
    public void ParseLightingZone_PreservesOfficialNoneDirection()
    {
        var state = GcuQuickControlsStatusParser.ParseLightingZone(LightingZone.Keyboard,
            "{\"powerStatus\":\"On\",\"type\":\"MEZone_3p1nd_101\",\"effect\":\"5\",\"direction\":\"0\"}");

        Assert.Equal(LightingDirection.None, state!.Configuration!.Direction);
    }

    [Theory]
    [InlineData("{\"CloseTimer\":30}", 30)]
    [InlineData("{\"CloseTimer\":\"7200\"}", 7200)]
    public void ParseLightingSleepTimer_ReadsOfficialSettingStatus(string payload, int expected) =>
        Assert.Equal(expected, GcuQuickControlsStatusParser.ParseLightingSleepTimer(payload));
    [Fact]
    public void ParseShortcuts_MapsOfficialSettingStatus()
    {
        var state = GcuQuickControlsStatusParser.ParseShortcuts("""
            { "WinKey":"WINKEY_UNLOCK", "FnKey":"FNKEY_LOCK", "OSD":"OSD_HIDDEN_OFF" }
            """);

        Assert.Equal(new ShortcutState(true, false, true), state);
    }

    [Fact]
    public void ParseKeyboardLighting_ReadsPowerStatus()
    {
        var state = GcuQuickControlsStatusParser.ParseKeyboardLighting("""
            { "type":"SingleZone", "powerStatus":"On" }
            """);

        Assert.Equal(new KeyboardLightingState(true, "SingleZone"), state);
    }

    [Fact]
    public void ParseBatteryProtection_MapsHealthModeAndPercent()
    {
        var state = GcuQuickControlsStatusParser.ParseBatteryProtection("""
            { "HealthProtectionStatus":2, "BatteryPercent":84 }
            """);

        Assert.Equal(BatteryProtectionMode.Healthy, state!.Mode);
        Assert.Equal(84, state.BatteryPercent);
    }

    [Fact]
    public void ParseSystemToggles_MapsOfficialDeviceSwitchStatusAndCopilotState()
    {
        var state = GcuQuickControlsStatusParser.ParseSystemToggles("""
            {
              "WIFIEnable":true,
              "BTEnable":true,
              "TochpadEnable":false,
              "WebCamEnable":false,
              "CopilotKey":"COPILOTKEY_UNLOCK"
            }
            """);

        Assert.Equal(new SystemToggleState(true, true, false, false, true), state);
    }

    [Fact]
    public void ParsePowerSettings_MapsSupportSwitchesAndDeepSleepTime()
    {
        var state = GcuQuickControlsStatusParser.ParsePowerSettings("""
            {
              "UsbCharger":"USB_CHARGER_ON",
              "AcRecoverySwitch_Support":1,
              "AcRecoverySwitch_Status":"ACRECOVERY_TOGGLE_OFF",
              "HighPerformancePowerModeSwitch":true,
              "DeepSleepSwitch":"DEEPSLEEP_ON",
              "DeepSleepTime":"30"
            }
            """);

        Assert.NotNull(state);
        Assert.True(state.UsbChargingWhileOffEnabled);
        Assert.True(state.SupportsAcAutoBoot);
        Assert.False(state.AcAutoBootEnabled);
        Assert.True(state.HighPerformancePowerModeEnabled);
        Assert.True(state.DeepSleepEnabled);
        Assert.Equal(30, state.DeepSleepMinutes);
    }

    [Fact]
    public void ParseLiquidCooling_MapsConnectionAndModes()
    {
        var state = GcuQuickControlsStatusParser.ParseLiquidCooling("""
            {
              "connected":true,
              "ConnectString":"Connected",
              "FanDuty":"60",
              "PumpDuty":"90",
              "LC_FanCtrl":"2",
              "LC_PumpCtrl":"2",
              "DeviceMacList":["CoolingSystem LCT22002 (59:05)","CoolingSystem LCT21001 (31:09)"],
              "DevMACString":"CoolingSystem LCT22002 (59:05)",
              "DevFWVersion":"CoolingSystem LCT22002 v2.0.0.4",
              "LC_action":true,
              "LC_InputWater":true,
              "LC_MeterNormal":false
            }
            """);

        Assert.True(state!.Connected);
        Assert.Equal(LiquidCoolingFanMode.Balanced, state.FanMode);
        Assert.Equal(LiquidCoolingPumpMode.Maximum, state.PumpMode);
        Assert.Equal(60, state.FanDutyPercent);
        Assert.Equal(90, state.PumpDutyPercent);
        Assert.Equal(new[] { "CoolingSystem LCT22002 (59:05)", "CoolingSystem LCT21001 (31:09)" },
            state.Devices.Select(device => device.Id));
        Assert.Equal("CoolingSystem LCT22002 (59:05)", state.SelectedDeviceId);
        Assert.Equal("CoolingSystem LCT22002 v2.0.0.4", state.FirmwareVersion);
        Assert.True(state.CanControl);
        Assert.True(state.IsPriming);
        Assert.False(state.MeterNormal);
    }

    [Fact]
    public void ParseLiquidCooling_InputWaterFalseMeansIdleNotUnsupported()
    {
        var state = GcuQuickControlsStatusParser.ParseLiquidCooling("""
            { "connected":true, "LC_action":true, "LC_InputWater":false }
            """);

        Assert.NotNull(state);
        Assert.False(state.IsPriming);
        Assert.True(state.CanControl);
    }

    [Theory]
    [InlineData("{\"ColorCalibrationStatus\":\"COLOR_CALIBRATION_ON_P3\",\"LocalDimming\":\"LOCALDIMMING_ON\"}", true, DisplayColorMode.P3, true)]
    [InlineData("{\"ColorCalibrationStatus\":\"COLOR_CALIBRATION_OFF\",\"LocalDimmingStatus\":0}", false, null, false)]
    public void ParseDisplayFeatures_ToleratesOfficialStatusVariants(
        string payload,
        bool colorEnabled,
        DisplayColorMode? colorMode,
        bool localDimming)
    {
        var state = GcuQuickControlsStatusParser.ParseDisplayFeatures(payload);

        Assert.NotNull(state);
        Assert.Equal(colorEnabled, state.ColorCalibrationEnabled);
        Assert.Equal(colorMode, state.ColorMode);
        Assert.Equal(localDimming, state.LocalDimmingEnabled);
    }

    [Fact]
    public void ParseDisplayFeatures_MapsGcuSwitchAndCurrentFields()
    {
        var state = GcuQuickControlsStatusParser.ParseDisplayFeatures("""
            {
              "ColorCalibrationSupport":1,
              "ColorCalibrationSwitch":1,
              "CurrentColorCalibration":"P3",
              "LocalDimmingSwitch":1
            }
            """);

        Assert.NotNull(state);
        Assert.True(state.SupportsColorCalibration);
        Assert.True(state.ColorCalibrationEnabled);
        Assert.Equal(DisplayColorMode.P3, state.ColorMode);
        Assert.True(state.SupportsLocalDimming);
        Assert.True(state.LocalDimmingEnabled);
    }

    [Theory]
    [InlineData(0, DisplayColorMode.Default)]
    [InlineData(1, DisplayColorMode.Srgb)]
    [InlineData(2, DisplayColorMode.P3)]
    [InlineData(3, DisplayColorMode.AdobeRgb)]
    public void ParseDisplayFeatures_MapsNumericCurrentColorCalibration(int rawMode, DisplayColorMode expected)
    {
        var state = GcuQuickControlsStatusParser.ParseDisplayFeatures(
            $$"""{"ColorCalibrationSwitch":1,"CurrentColorCalibration":{{rawMode}}}""");

        Assert.NotNull(state);
        Assert.True(state.ColorCalibrationEnabled);
        Assert.Equal(expected, state.ColorMode);
    }

    [Fact]
    public void ParseDisplayFeatures_ExposesOfficialCalibrationResultCode()
    {
        var state = GcuQuickControlsStatusParser.ParseDisplayFeatures("""
            {"ColorCalibrationSwitch":1,"CurrentColorCalibration":2,"ColorCalibrationResultCode":"Finish"}
            """);

        Assert.Equal("Finish", state!.ColorCalibrationResultCode);
    }
}
