using RevoHelper.Core.Control;
using RevoHelper.Hardware.Windows.OemControl;

namespace RevoHelper.Hardware.Windows.Tests.OemControl;

public sealed class GcuQuickControlsControllerTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task IndependentPowerChannels_PreserveTheSuccessfulChannel(bool settingFails)
    {
        var channel = new FakeChannel
        {
            SettingStatus = "{\"UsbCharger\":true,\"DeepSleepSwitch\":true}",
            BatteryChargeLimitPercent = 80,
            SettingReadException = settingFails ? new TimeoutException("setting timeout") : null,
            ChargeLimitReadException = settingFails ? null : new TimeoutException("charge limit timeout")
        };

        var state = await new GcuQuickControlsController(channel).ReadPowerSettingsAsync(default);

        Assert.NotNull(state);
        Assert.Single(state.ReadWarnings);
        Assert.Contains("timeout", state.ReadWarnings[0], StringComparison.Ordinal);
        Assert.Equal(!settingFails, state.SettingStatusReadSucceeded);
        if (settingFails) Assert.Equal(80, state.BatteryChargeLimitPercent);
        else
        {
            Assert.True(state.UsbChargingWhileOffEnabled);
            Assert.True(state.DeepSleepEnabled);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task IndependentDeviceChannels_PreserveTheSuccessfulChannel(bool settingFails)
    {
        var channel = new FakeChannel
        {
            DeviceSwitchStatus = "{\"WIFIEnable\":true}",
            SettingStatus = "{\"CopilotKey\":\"COPILOTKEY_UNLOCK\"}",
            SettingReadException = settingFails ? new TimeoutException("setting timeout") : null,
            DeviceReadException = settingFails ? null : new TimeoutException("device timeout")
        };

        var state = await new GcuQuickControlsController(channel).ReadSystemTogglesAsync(default);

        Assert.NotNull(state);
        Assert.Single(state.ReadWarnings);
        if (settingFails) Assert.True(state.WifiEnabled);
        else Assert.True(state.CopilotKeyEnabled);
    }

    [Fact]
    public async Task IndependentChannels_AllFailedDoNotReturnASuccessfulEmptyState()
    {
        var channel = new FakeChannel
        {
            SettingReadException = new TimeoutException("setting timeout"),
            DeviceReadException = new TimeoutException("device timeout"),
            ChargeLimitReadException = new TimeoutException("charge limit timeout")
        };
        var controller = new GcuQuickControlsController(channel);

        var power = await Assert.ThrowsAsync<InvalidOperationException>(() => controller.ReadPowerSettingsAsync(default).AsTask());
        var system = await Assert.ThrowsAsync<InvalidOperationException>(() => controller.ReadSystemTogglesAsync(default).AsTask());

        Assert.Contains("setting timeout", power.Message, StringComparison.Ordinal);
        Assert.Contains("charge limit timeout", power.Message, StringComparison.Ordinal);
        Assert.Contains("device timeout", system.Message, StringComparison.Ordinal);
        Assert.Contains("setting timeout", system.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task IndependentPowerChannels_CancellationStillPropagates(bool settingCancels)
    {
        var channel = new FakeChannel
        {
            SettingReadException = settingCancels ? new OperationCanceledException() : null,
            ChargeLimitReadException = settingCancels ? null : new OperationCanceledException()
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new GcuQuickControlsController(channel).ReadPowerSettingsAsync(default).AsTask());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task IndependentDeviceChannels_CancellationStillPropagates(bool settingCancels)
    {
        var channel = new FakeChannel
        {
            SettingReadException = settingCancels ? new OperationCanceledException() : null,
            DeviceReadException = settingCancels ? null : new OperationCanceledException()
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new GcuQuickControlsController(channel).ReadSystemTogglesAsync(default).AsTask());
    }

    [Fact]
    public async Task IndependentWriteReadback_ConfirmsOnlyTheRequestedPowerField()
    {
        var channel = new FakeChannel
        {
            SettingStatus = "{\"UsbCharger\":true}",
            ChargeLimitReadException = new TimeoutException("charge limit timeout")
        };
        var controller = new GcuQuickControlsController(channel, (_, _) => Task.CompletedTask);
        var confirmed = await controller.SetPowerToggleAsync(PowerToggleControl.UsbChargingWhileOff, true, default);
        Assert.True(confirmed.Succeeded);
        Assert.Contains("charge limit timeout", confirmed.Reason, StringComparison.Ordinal);
        channel.SettingStatus = "{}";
        Assert.False((await controller.SetPowerToggleAsync(PowerToggleControl.UsbChargingWhileOff, true, default)).Succeeded);
    }

    [Fact]
    public async Task IndependentWriteReadback_ConfirmsOnlyTheRequestedDeviceField()
    {
        var channel = new FakeChannel
        {
            DeviceSwitchStatus = "{\"WIFIEnable\":true}",
            SettingReadException = new TimeoutException("setting timeout")
        };
        var controller = new GcuQuickControlsController(channel, (_, _) => Task.CompletedTask);
        var confirmed = await controller.SetSystemToggleAsync(SystemToggleControl.Wifi, true, default);
        Assert.True(confirmed.Succeeded);
        Assert.Contains("setting timeout", confirmed.Reason, StringComparison.Ordinal);
        channel.DeviceSwitchStatus = "{}";
        Assert.False((await controller.SetSystemToggleAsync(SystemToggleControl.Wifi, true, default)).Succeeded);
    }

    [Fact]
    public async Task Shortcut_UnrelatedPartialPacketDoesNotConfirmMissingTarget()
    {
        var channel = new FakeChannel { SettingStatus = "{\"WinKey\":\"WINKEY_UNLOCK\"}" };
        var result = await new GcuQuickControlsController(channel).SetShortcutAsync(ShortcutControl.FnKey, true, default);
        Assert.False(result.Succeeded);
    }

    [Theory]
    [InlineData("On", 2, false)]
    [InlineData("Off", 1, false)]
    [InlineData(null, 1, false)]
    [InlineData("On", 2, true)]
    [InlineData("Off", 1, true)]
    [InlineData(null, 1, true)]
    public async Task LogoOff_RestoresOnlyExplicitlyEnabledCoast(string? coastPower, int writes, bool throughEffect)
    {
        var channel = new FakeChannel();
        channel.LightingStatuses[LightingZone.Logo] = """{"type":"MEZone_Lighbar4","powerStatus":"Off"}""";
        channel.LightingStatuses[LightingZone.CoastLightbar] = coastPower is null ? "{}" :
            $$"""{"type":"MEZone_Lighbar4","powerStatus":"{{coastPower}}"}""";
        var controller = new GcuQuickControlsController(channel);
        var result = throughEffect
            ? await controller.ApplyLightingZoneAsync(new(LightingZone.Logo, false, LightingEffect.Static,
                new(255, 112, 0), 3, 2, LightingDirection.None, 120), CancellationToken.None)
            : await controller.SetLightingZonePowerAsync(LightingZone.Logo, false, CancellationToken.None);
        Assert.True(result.Succeeded, result.Reason);
        Assert.Equal(writes, channel.LightingWrites.Count);
        Assert.Equal(LightingZone.Logo, channel.LightingWrites[0].Zone);
        if (writes == 2)
        {
            Assert.Equal(LightingZone.CoastLightbar, channel.LightingWrites[1].Zone);
            Assert.Equal("{\"function\":\"SetPower\",\"powerstatus\":1}", channel.LightingWrites[1].Payload);
        }
    }

    [Fact]
    public async Task KeyboardEffect_EnableExplicitlyPowersOnBeforeApplyingDraft()
    {
        var channel = new FakeChannel
        {
            KeyboardStatus = """{"type":"MEZone_3p1nd_101","powerStatus":"Off","brightNess":"3"}""",
            SimulateKeyboardPower = true
        };
        var controller = new GcuQuickControlsController(channel);
        var result = await controller.ApplyLightingZoneAsync(new(LightingZone.Keyboard, true,
            LightingEffect.Static, new(255, 154, 36), 3, 2, LightingDirection.None, 120), CancellationToken.None);
        Assert.True(result.Succeeded, result.Reason);
        Assert.Contains("SetPower", channel.LightingWrites[0].Payload);
        Assert.Contains("SetEffectALL", channel.LightingWrites[1].Payload);
        Assert.Equal(1, channel.AtomicLightingWrites);
    }

    [Theory]
    [InlineData(3, true)]
    [InlineData(1, false)]
    public async Task LogoPowerAndBrightnessReadback_DoesNotInventAnEffectMismatch(int actualBrightness, bool accepted)
    {
        var channel = new FakeChannel();
        channel.LightingStatuses[LightingZone.Logo] = """{"type":"MEZone_Lighbar4","powerStatus":"On","brightNess":"3"}""";
        var controller = new GcuQuickControlsController(channel, (_, _) => Task.CompletedTask);
        await controller.ReadLightingZoneAsync(LightingZone.Logo, CancellationToken.None);
        channel.LightingStatuses[LightingZone.Logo] = $$"""{"type":"MEZone_Lighbar4","powerStatus":"On","brightNess":"{{actualBrightness}}"}""";

        var result = await controller.ApplyLightingZoneAsync(new(LightingZone.Logo, true,
            LightingEffect.Breathing, new(255, 0, 0), 3, 2, LightingDirection.None, 120), CancellationToken.None);

        Assert.Equal(accepted, result.Succeeded);
        if (accepted) Assert.Contains("灯效与颜色未回报", result.Reason, StringComparison.Ordinal);
        using var json = System.Text.Json.JsonDocument.Parse(channel.LightingWrites[1].Payload);
        Assert.Equal("2", json.RootElement.GetProperty("speed").GetString());
        Assert.Equal("Breathing", json.RootElement.GetProperty("effect").GetString());
    }

    [Fact]
    public async Task ApplyLightingZone_UsesOneAtomicWriteReadbackAfterCapabilityRead()
    {
        const string status = """
            {"powerStatus":"On","type":"PerKey","effect":"Single","light":3,"speed":2,
             "direction":"0","R":16,"G":32,"B":48}
            """;
        var channel = new FakeChannel { KeyboardStatus = status };
        var controller = new GcuQuickControlsController(channel, (_, _) => Task.CompletedTask);
        var configuration = new LightingZoneConfiguration(
            LightingZone.Keyboard, true, LightingEffect.Static,
            new LightingRgb(0x10, 0x20, 0x30), 3, 2, LightingDirection.None, 0);

        var result = await controller.ApplyLightingZoneAsync(configuration, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1, channel.AtomicLightingWrites);
        Assert.Equal(1, channel.LightingReads);
    }

    [Fact]
    public async Task ApplyLightingZone_ReusesPreviouslyReadZoneCapabilityWithoutAnotherGet()
    {
        const string status = """
            {"powerStatus":"On","type":"PerKey","effect":"Single","light":3,"speed":2,
             "direction":"0","R":16,"G":32,"B":48}
            """;
        var channel = new FakeChannel { KeyboardStatus = status };
        var controller = new GcuQuickControlsController(channel, (_, _) => Task.CompletedTask);
        await controller.ReadLightingZoneAsync(LightingZone.Keyboard, CancellationToken.None);
        var configuration = new LightingZoneConfiguration(
            LightingZone.Keyboard, true, LightingEffect.Static,
            new LightingRgb(0x10, 0x20, 0x30), 3, 2, LightingDirection.None, 0);

        var result = await controller.ApplyLightingZoneAsync(configuration, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1, channel.LightingReads);
        Assert.Equal(1, channel.AtomicLightingWrites);
    }

    [Fact]
    public async Task SetShortcut_WritesApprovedActionAndVerifiesFreshStatus()
    {
        var channel = new FakeChannel
        {
            SettingStatus = "{ \"WinKey\": \"WINKEY_UNLOCK\", \"FnKey\": \"FNKEY_UNLOCK\", \"OSD\": \"OSD_HIDDEN_OFF\" }"
        };
        var controller = new GcuQuickControlsController(channel);

        var result = await controller.SetShortcutAsync(ShortcutControl.WinKey, true, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("{\"Action\":\"WINKEY_UNLOCK\"}", channel.LastSettingCommand);
        Assert.Equal(1, channel.SettingReads);
    }

    [Fact]
    public async Task SetKeyboardLighting_RejectsNonConvergingReadback()
    {
        var channel = new FakeChannel { KeyboardStatus = "{ \"powerStatus\": \"Off\" }" };
        var controller = new GcuQuickControlsController(channel);

        var result = await controller.SetKeyboardLightingAsync(true, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("{\"function\":\"SetPower\",\"powerstatus\":1}", channel.LastKeyboardCommand);
    }

    [Fact]
    public async Task ApplyLightingZone_WhenReadbackNeverConverges_RejectsUnconfirmedWrite()
    {
        var channel = new FakeChannel();
        channel.LightingStatuses[LightingZone.Keyboard] = "{\"powerStatus\":\"On\",\"type\":\"PerKey\",\"effect\":\"Single\"}";
        var controller = new GcuQuickControlsController(channel, (_, _) => Task.CompletedTask);
        var configuration = new LightingZoneConfiguration(
            LightingZone.Keyboard, true, LightingEffect.Breathing, new(0, 180, 255), 3, 2, LightingDirection.Left, 2);

        var result = await controller.ApplyLightingZoneAsync(configuration, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(LightingZone.Keyboard, channel.LastLightingZone);
        Assert.Contains("\"effect\":\"Breathing\"", channel.LastLightingCommand, StringComparison.Ordinal);
        Assert.Contains("未确认生效", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyLightingZone_WhenEffectMatchesButParametersDoNot_RejectsUnconfirmedWrite()
    {
        var channel = new FakeChannel();
        channel.LightingStatuses[LightingZone.Keyboard] =
            "{\"powerStatus\":\"On\",\"type\":\"PerKey\",\"effect\":\"Breathing\",\"light\":1,\"speed\":1,\"R\":255,\"G\":0,\"B\":0}";
        var controller = new GcuQuickControlsController(channel, (_, _) => Task.CompletedTask);
        var configuration = new LightingZoneConfiguration(
            LightingZone.Keyboard, true, LightingEffect.Breathing, new(0, 180, 255), 3, 2,
            LightingDirection.None, 120);

        var result = await controller.ApplyLightingZoneAsync(configuration, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("未确认生效", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyLightingZone_RejectsZoneNotDiscoveredFromStatus()
    {
        var controller = new GcuQuickControlsController(new FakeChannel());
        var configuration = new LightingZoneConfiguration(
            LightingZone.CoastLightbar, true, LightingEffect.Wave, new(0, 180, 255), 3, 2, LightingDirection.Left, 2);

        var result = await controller.ApplyLightingZoneAsync(configuration, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("未从官方状态主题发现", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyLightingZone_EnablingCoastLightbar_ReassertsPowerAfterApplyingEffect()
    {
        const string status =
            "{\"powerStatus\":\"On\",\"type\":\"MEZone_Lighbar4\",\"effect\":\"Single\",\"light\":3,\"speed\":3,\"direction\":\"0\",\"R\":0,\"G\":176,\"B\":255}";
        var channel = new FakeChannel();
        channel.LightingStatuses[LightingZone.CoastLightbar] = status;
        var controller = new GcuQuickControlsController(channel, (_, _) => Task.CompletedTask);
        var configuration = new LightingZoneConfiguration(
            LightingZone.CoastLightbar, true, LightingEffect.Static, new(0, 176, 255), 3, 3,
            LightingDirection.None, 120);

        var result = await controller.ApplyLightingZoneAsync(configuration, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(3, channel.LightingWrites.Count);
        Assert.Equal(1, channel.AtomicLightingWrites);
        Assert.Equal((LightingZone.CoastLightbar, "{\"function\":\"SetPower\",\"powerstatus\":1}"),
            channel.LightingWrites[0]);
        Assert.Contains("\"function\":\"SetEffectALL\"", channel.LightingWrites[1].Payload,
            StringComparison.Ordinal);
        Assert.Equal((LightingZone.CoastLightbar, "{\"function\":\"SetPower\",\"powerstatus\":1}"),
            channel.LightingWrites[2]);
    }

    [Fact]
    public async Task ApplyLightingZone_EnablingLogo_ReassertsPowerAfterApplyingEffect()
    {
        const string status =
            "{\"powerStatus\":\"On\",\"type\":\"MEZone_Lighbar4\",\"effect\":\"Single\",\"light\":3,\"speed\":3,\"direction\":\"0\",\"R\":0,\"G\":176,\"B\":255}";
        var channel = new FakeChannel();
        channel.LightingStatuses[LightingZone.Logo] = status;
        var controller = new GcuQuickControlsController(channel, (_, _) => Task.CompletedTask);
        var configuration = new LightingZoneConfiguration(
            LightingZone.Logo, true, LightingEffect.Static, new(0, 176, 255), 3, 3,
            LightingDirection.None, 120);

        var result = await controller.ApplyLightingZoneAsync(configuration, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(3, channel.LightingWrites.Count);
        Assert.Equal(1, channel.AtomicLightingWrites);
        Assert.Equal((LightingZone.Logo, "{\"function\":\"SetPower\",\"powerstatus\":1}"),
            channel.LightingWrites[0]);
        Assert.Contains("\"function\":\"SetEffectALL\"", channel.LightingWrites[1].Payload,
            StringComparison.Ordinal);
        Assert.Equal((LightingZone.Logo, "{\"function\":\"SetPower\",\"powerstatus\":1}"),
            channel.LightingWrites[2]);
    }

    [Fact]
    public async Task ApplyLightingZone_EnabledBlackCoastLightbar_NormalizesToVisibleOutput()
    {
        const string status =
            "{\"powerStatus\":\"On\",\"type\":\"MEZone_Lighbar4\",\"effect\":\"Single\",\"light\":1,\"speed\":2,\"direction\":\"0\",\"R\":0,\"G\":176,\"B\":255}";
        var channel = new FakeChannel();
        channel.LightingStatuses[LightingZone.CoastLightbar] = status;
        var controller = new GcuQuickControlsController(channel, (_, _) => Task.CompletedTask);
        var configuration = new LightingZoneConfiguration(
            LightingZone.CoastLightbar, true, LightingEffect.Static, new(0, 0, 0), 0, 2,
            LightingDirection.None, 120);

        var result = await controller.ApplyLightingZoneAsync(configuration, CancellationToken.None);

        Assert.True(result.Succeeded, result.Reason);
        Assert.Contains("00B0FF", channel.LightingWrites[1].Payload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ApplyLightingZone_EnabledBlackStaticWaterLight_NormalizesToVisibleOutput()
    {
        var channel = new FakeChannel
        {
            LiquidStatus =
                "{\"connected\":true,\"LCLED_Mode\":0,\"LCFanLED_Mode\":0,\"LCLED_R\":0,\"LCLED_G\":176,\"LCLED_B\":255}"
        };
        var controller = new GcuQuickControlsController(channel, (_, _) => Task.CompletedTask);
        var configuration = new LightingZoneConfiguration(
            LightingZone.LiquidCooler, true, LightingEffect.Static, new(0, 0, 0), 3, 2,
            LightingDirection.None, 120);

        var result = await controller.ApplyLightingZoneAsync(configuration, CancellationToken.None);

        Assert.True(result.Succeeded, result.Reason);
        Assert.Equal(
            "{\"Action\":\"LEDControl\",\"LCLED_R\":\"0\",\"LCLED_G\":\"176\",\"LCLED_B\":\"255\"}",
            channel.LightingWrites[^1].Payload);
    }

    [Fact]
    public async Task SetLightingZonePower_EnablingSparseCoastLightbar_LoadsVisibleFallbackAndEndsWithPowerOn()
    {
        var channel = new FakeChannel();
        channel.LightingStatuses[LightingZone.CoastLightbar] =
            "{\"powerStatus\":\"Off\",\"type\":\"MEZone_Lighbar4\"}";
        var controller = new GcuQuickControlsController(channel, (_, _) => Task.CompletedTask);
        await controller.ReadLightingZoneAsync(LightingZone.CoastLightbar, CancellationToken.None);
        channel.LightingStatuses[LightingZone.CoastLightbar] =
            "{\"powerStatus\":\"On\",\"type\":\"MEZone_Lighbar4\"}";

        var result = await controller.SetLightingZonePowerAsync(
            LightingZone.CoastLightbar, true, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Contains("仅确认电源", result.Reason, StringComparison.Ordinal);
        Assert.Equal(3, channel.LightingWrites.Count);
        Assert.Equal((LightingZone.CoastLightbar, "{\"function\":\"SetPower\",\"powerstatus\":1}"),
            channel.LightingWrites[0]);
        Assert.Contains("\"function\":\"SetEffectALL\"", channel.LightingWrites[1].Payload,
            StringComparison.Ordinal);
        Assert.Contains("00B0FF", channel.LightingWrites[1].Payload, StringComparison.OrdinalIgnoreCase);
        Assert.Equal((LightingZone.CoastLightbar, "{\"function\":\"SetPower\",\"powerstatus\":1}"),
            channel.LightingWrites[2]);
        Assert.Equal(1, channel.AtomicLightingWrites);
    }

    [Fact]
    public async Task SetLightingZonePower_DisablingCoastLightbar_SendsOnlyPowerOff()
    {
        var channel = new FakeChannel();
        channel.LightingStatuses[LightingZone.CoastLightbar] =
            "{\"powerStatus\":\"On\",\"type\":\"MEZone_Lighbar4\",\"effect\":\"Single\",\"light\":3,\"speed\":2,\"R\":0,\"G\":176,\"B\":255}";
        var controller = new GcuQuickControlsController(channel, (_, _) => Task.CompletedTask);
        await controller.ReadLightingZoneAsync(LightingZone.CoastLightbar, CancellationToken.None);
        channel.LightingStatuses[LightingZone.CoastLightbar] =
            "{\"powerStatus\":\"Off\",\"type\":\"MEZone_Lighbar4\"}";

        var result = await controller.SetLightingZonePowerAsync(
            LightingZone.CoastLightbar, false, CancellationToken.None);

        Assert.True(result.Succeeded);
        var write = Assert.Single(channel.LightingWrites);
        Assert.Equal((LightingZone.CoastLightbar, "{\"function\":\"SetPower\",\"powerstatus\":0}"), write);
        Assert.Equal(1, channel.AtomicLightingWrites);
    }

    [Fact]
    public async Task ReadLightingZone_LogoSparsePowerStatus_PreservesCachedEffectParameters()
    {
        var channel = new FakeChannel();
        channel.LightingStatuses[LightingZone.Logo] =
            "{\"powerStatus\":\"On\",\"type\":\"MEZone_Lighbar4\",\"effect\":\"Breathing\",\"light\":4,\"speed\":3,\"R\":0,\"G\":176,\"B\":255}";
        var controller = new GcuQuickControlsController(channel, (_, _) => Task.CompletedTask);
        await controller.ReadLightingZoneAsync(LightingZone.Logo, CancellationToken.None);
        channel.LightingStatuses[LightingZone.Logo] =
            "{\"powerStatus\":\"Off\",\"type\":\"MEZone_Lighbar4\"}";

        var state = await controller.ReadLightingZoneAsync(LightingZone.Logo, CancellationToken.None);

        Assert.NotNull(state?.Configuration);
        Assert.False(state.Configuration.Enabled);
        Assert.Equal(LightingEffect.Breathing, state.Configuration.Effect);
        Assert.Equal(new LightingRgb(0, 176, 255), state.Configuration.Color);
        Assert.Equal(4, state.Configuration.Brightness);
        Assert.Equal(3, state.Configuration.Speed);
    }

    [Fact]
    public async Task ApplyLightingZone_DisablingWaterStopsEveryDynamicChannelBeforeClearingRgb()
    {
        var channel = new FakeChannel
        {
            LiquidStatus = "{\"connected\":true,\"LCLED_Mode\":0,\"LCFanLED_Mode\":5,\"LCLED_R\":0,\"LCLED_G\":255,\"LCLED_B\":255}"
        };
        var controller = new GcuQuickControlsController(channel, (_, _) => Task.CompletedTask);
        var configuration = new LightingZoneConfiguration(
            LightingZone.LiquidCooler, false, LightingEffect.Rainbow, new(0, 0, 0), 3, 2, LightingDirection.Left, 0);

        var result = await controller.ApplyLightingZoneAsync(configuration, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("未确认生效", result.Reason, StringComparison.Ordinal);
        Assert.Equal(4, channel.LightingWrites.Count);
        Assert.Equal((LightingZone.LiquidCooler, "{\"Action\":\"LEDBreathing\",\"LedMode\":0}"), channel.LightingWrites[0]);
        Assert.Equal((LightingZone.LiquidCooler, "{\"Action\":\"LEDColorful\",\"LedMode\":0}"), channel.LightingWrites[1]);
        Assert.Equal((LightingZone.LiquidCooler, "{\"Action\":\"FanLEDEffect\",\"LedMode\":0}"), channel.LightingWrites[2]);
        Assert.Equal((LightingZone.LiquidCooler, "{\"Action\":\"LEDControl\",\"LCLED_R\":\"0\",\"LCLED_G\":\"0\",\"LCLED_B\":\"0\"}"), channel.LightingWrites[3]);
    }

    [Fact]
    public async Task ApplyLightingZone_ChangingWaterEffect_StopsEveryDynamicChannelBeforeTargetEffect()
    {
        var channel = new FakeChannel
        {
            LiquidStatus = "{\"connected\":true,\"LCLED_Mode\":1,\"LCFanLED_Mode\":5,\"LCLED_R\":16,\"LCLED_G\":72,\"LCLED_B\":112}"
        };
        var controller = new GcuQuickControlsController(channel, (_, _) => Task.CompletedTask);
        var configuration = new LightingZoneConfiguration(
            LightingZone.LiquidCooler, true, LightingEffect.Rotate, new(16, 72, 112), 3, 2,
            LightingDirection.None, 120);

        await controller.ApplyLightingZoneAsync(configuration, CancellationToken.None);

        Assert.Equal(4, channel.LightingWrites.Count);
        Assert.Equal("{\"Action\":\"LEDBreathing\",\"LedMode\":0}", channel.LightingWrites[0].Payload);
        Assert.Equal("{\"Action\":\"LEDColorful\",\"LedMode\":0}", channel.LightingWrites[1].Payload);
        Assert.Equal("{\"Action\":\"FanLEDEffect\",\"LedMode\":0}", channel.LightingWrites[2].Payload);
        Assert.Equal("{\"Action\":\"FanLEDEffect\",\"LedMode\":4}", channel.LightingWrites[3].Payload);
    }

    [Fact]
    public async Task SetLightingSleepTimer_UsesOfficialValuesAndRequiresReadback()
    {
        var channel = new FakeChannel { SettingStatus = "{\"CloseTimer\":7200}" };
        var controller = new GcuQuickControlsController(channel, (_, _) => Task.CompletedTask);

        var result = await controller.SetLightingSleepTimerAsync(7200, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("{\"Action\":\"KEYBOARD_LIGHTBAR_TIMER_ON\",\"Mins\":7200}", channel.LastSettingCommand);
        Assert.Equal(1, channel.SettingReads);
    }

    [Fact]
    public async Task SetLightingSleepTimer_RejectsUnofficialTwoMinuteValueWithoutWriting()
    {
        var channel = new FakeChannel();
        var controller = new GcuQuickControlsController(channel, (_, _) => Task.CompletedTask);

        var result = await controller.SetLightingSleepTimerAsync(2, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(channel.LastSettingCommand);
    }

    [Fact]
    public async Task SetBatteryProtection_WritesAndVerifiesMode()
    {
        var channel = new FakeChannel { BatteryStatus = "{ \"HealthProtectionStatus\": 2, \"BatteryPercent\": 75 }" };
        var controller = new GcuQuickControlsController(channel);

        var result = await controller.SetBatteryProtectionAsync(BatteryProtectionMode.Healthy, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("{\"Action\":\"HEALTHYMODE\"}", channel.LastBatteryCommand);
        Assert.Equal(1, channel.BatteryReads);
    }

    [Fact]
    public async Task SetSystemToggle_WritesOfficialActionAndVerifiesAvailableReadback()
    {
        var channel = new FakeChannel { DeviceSwitchStatus = "{\"TochpadEnable\":false}" };
        var controller = new GcuQuickControlsController(channel, (_, _) => Task.CompletedTask);

        var result = await controller.SetSystemToggleAsync(SystemToggleControl.Touchpad, false, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("{\"Action\":\"TOUCHPAD_OFF\"}", channel.LastSettingCommand);
    }

    [Fact]
    public async Task SetSystemToggle_MissingAuthoritativeReadbackRejectsInsteadOfPretendingSuccess()
    {
        var channel = new FakeChannel { DeviceSwitchStatus = "{}", SettingStatus = "{}" };
        var controller = new GcuQuickControlsController(channel, (_, _) => Task.CompletedTask);

        var result = await controller.SetSystemToggleAsync(SystemToggleControl.Wifi, true, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("无法确认", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BluetoothToggleWaitsForTheKnownSlowOemRadioTransition()
    {
        var channel = new FakeChannel();
        for (var attempt = 0; attempt < 5; attempt++)
            channel.DeviceSwitchStatuses.Enqueue("{\"BTEnable\":false}");
        channel.DeviceSwitchStatuses.Enqueue("{\"BTEnable\":true}");
        var delays = new List<TimeSpan>();
        var controller = new GcuQuickControlsController(channel, (delay, _) =>
        {
            delays.Add(delay);
            return Task.CompletedTask;
        });

        var result = await controller.SetSystemToggleAsync(
            SystemToggleControl.Bluetooth, true, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("{\"Action\":\"BT_ON\"}", channel.LastSettingCommand);
        Assert.Equal(5, delays.Count);
        Assert.All(delays, delay => Assert.True(delay >= TimeSpan.FromMilliseconds(350)));
    }

    [Fact]
    public async Task SetPowerToggle_WritesOfficialActionAndVerifiesReadback()
    {
        var channel = new FakeChannel { SettingStatus = "{\"UsbCharger\":\"USB_CHARGER_ON\"}" };
        var controller = new GcuQuickControlsController(channel, (_, _) => Task.CompletedTask);

        var result = await controller.SetPowerToggleAsync(PowerToggleControl.UsbChargingWhileOff, true, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("{\"Action\":\"USB_CHARGER_ON\"}", channel.LastSettingCommand);
    }

    [Fact]
    public async Task SetPowerToggle_MissingStatusRejectsInsteadOfPretendingSuccess()
    {
        var channel = new FakeChannel { SettingStatus = "{}" };
        var controller = new GcuQuickControlsController(channel, (_, _) => Task.CompletedTask);

        var result = await controller.SetPowerToggleAsync(
            PowerToggleControl.HighPerformancePowerMode, true, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("无法确认", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SetDeepSleep_WritesDurationAndVerifiesBothFields()
    {
        var channel = new FakeChannel { SettingStatus = "{\"DeepSleepSwitch\":\"DEEPSLEEP_ON\",\"DeepSleepTime\":30}" };
        var controller = new GcuQuickControlsController(channel, (_, _) => Task.CompletedTask);

        var result = await controller.SetDeepSleepAsync(true, 30, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("{\"Action\":\"DEEPSLEEP_ON\",\"DeepSleepTime\":30}", channel.LastSettingCommand);
    }

    [Theory]
    [InlineData(49)]
    [InlineData(101)]
    public async Task SetBatteryChargeLimit_RejectsUnsafeOrMeaninglessRange(int percent)
    {
        var channel = new FakeChannel();
        var controller = new GcuQuickControlsController(channel);

        var result = await controller.SetBatteryChargeLimitAsync(percent, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(channel.LastBatteryChargeLimitCommand);
    }

    [Fact]
    public async Task SetBatteryChargeLimit_RequiresFanStatusReadback()
    {
        var channel = new FakeChannel { BatteryChargeLimitPercent = 80 };
        var controller = new GcuQuickControlsController(channel, (_, _) => Task.CompletedTask);

        var result = await controller.SetBatteryChargeLimitAsync(80, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("{\"Action\":\"BATTERY_CHARGINGLIMIT_SETTING\",\"BatteryChargingLimit\":\"80\"}",
            channel.LastBatteryChargeLimitCommand);
    }

    [Fact]
    public async Task SetLiquidCoolingFan_RejectsDisconnectedDevice()
    {
        var channel = new FakeChannel
        {
            LiquidStatus = "{ \"connected\": false, \"ConnectString\": \"Disconnected\", \"LC_FanCtrl\": \"3\" }"
        };
        var controller = new GcuQuickControlsController(channel);

        var result = await controller.SetLiquidCoolingFanAsync(LiquidCoolingFanMode.Maximum, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(channel.LastLiquidCommand);
    }

    [Fact]
    public async Task ScanLiquidCoolingDevices_ReturnsGcuDiscoveredDevicesWithoutModelHardcoding()
    {
        var channel = new FakeChannel();
        channel.LiquidStatuses.Enqueue("{\"connected\":false,\"ConnectString\":\"Scanning\",\"DeviceMacList\":[]}");
        channel.LiquidStatuses.Enqueue("{\"connected\":false,\"ConnectString\":\"IsConnectable\",\"DeviceMacList\":[\"CoolingSystem LCT22002 (59:05)\"]}");
        var controller = new GcuQuickControlsController(channel, (_, _) => Task.CompletedTask);

        var state = await controller.ScanLiquidCoolingDevicesAsync(CancellationToken.None);

        Assert.Equal("CoolingSystem LCT22002 (59:05)", Assert.Single(state!.Devices).Id);
        Assert.Null(channel.LastLiquidCommand);
    }

    [Fact]
    public async Task SelectLiquidCoolingDevice_AcceptsRememberedDeviceWithoutDiscoveryOrRewrite()
    {
        var channel = new FakeChannel { LiquidStatus = "{\"connected\":false,\"LC_action\":true,\"DevMACString\":\"remembered-cooler\",\"DeviceMacList\":[\"\"]}" };
        var controller = new GcuQuickControlsController(channel);
        Assert.True((await controller.SelectLiquidCoolingDeviceAsync("remembered-cooler", CancellationToken.None)).Succeeded);
        Assert.Null(channel.LastLiquidCommand);
        Assert.False((await controller.SelectLiquidCoolingDeviceAsync("unknown-cooler", CancellationToken.None)).Succeeded);
        Assert.Null(channel.LastLiquidCommand);
    }

    [Fact]
    public async Task SelectLiquidCoolingDevice_WritesOnlyDeviceReturnedByGcu()
    {
        const string status = "{\"connected\":false,\"DeviceMacList\":[\"CoolingSystem LCT22002 (59:05)\"],\"DevMACString\":\"CoolingSystem LCT22002 (59:05)\"}";
        var channel = new FakeChannel { LiquidStatus = status };
        var controller = new GcuQuickControlsController(channel);

        var result = await controller.SelectLiquidCoolingDeviceAsync("CoolingSystem LCT22002 (59:05)", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("{\"Action\":\"DeviceMacSetting\",\"DeviceMac\":\"CoolingSystem LCT22002 (59:05)\"}", channel.LastLiquidCommand);
    }

    [Fact]
    public async Task ConnectLiquidCooling_WritesConnectAndRequiresConnectedReadback()
    {
        var channel = new FakeChannel();
        channel.LiquidStatuses.Enqueue("{\"connected\":false,\"LC_action\":true,\"DevMACString\":\"CoolingSystem LCT22002 (59:05)\"}");
        channel.LiquidStatuses.Enqueue("{\"connected\":false,\"ConnectString\":\"Connecting\",\"LC_action\":true,\"DevMACString\":\"CoolingSystem LCT22002 (59:05)\"}");
        channel.LiquidStatuses.Enqueue("{\"connected\":true,\"ConnectString\":\"Connected\",\"LC_action\":true,\"DevMACString\":\"CoolingSystem LCT22002 (59:05)\"}");
        var controller = new GcuQuickControlsController(channel, (_, _) => Task.CompletedTask);

        var result = await controller.ConnectLiquidCoolingAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("{\"Action\":\"Connect\"}", channel.LastLiquidCommand);
    }

    [Fact]
    public async Task Priming_WhenConnectedAndIdle_SendsOfficialCommand()
    {
        var channel = new FakeChannel { LiquidStatus = "{\"connected\":true,\"LC_action\":true,\"LC_InputWater\":false}" };
        var controller = new GcuQuickControlsController(channel);

        var result = await controller.StartLiquidCoolingPrimingAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("{\"Action\":\"InputWater\"}", channel.LastLiquidCommand);
    }

    [Fact]
    public async Task SetDisplayColorEnabled_WaitsForRequestedModeToConverge()
    {
        var channel = new FakeChannel();
        channel.SettingStatuses.Enqueue("{\"ColorCalibrationStatus\":\"COLOR_CALIBRATION_OFF\"}");
        channel.SettingStatuses.Enqueue("{\"ColorCalibrationStatus\":\"COLOR_CALIBRATION_ON_P3\"}");
        var controller = new GcuQuickControlsController(channel, (_, _) => Task.CompletedTask);

        var result = await controller.SetDisplayColorEnabledAsync(true, DisplayColorMode.P3, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("{\"Action\":\"COLOR_CALIBRATION_ON_P3\"}", channel.LastSettingCommand);
        Assert.Equal(2, channel.SettingReads);
    }

    [Fact]
    public async Task SetDisplayColor_WaitsForRequestedPresetToConverge()
    {
        var channel = new FakeChannel();
        channel.SettingStatuses.Enqueue("{\"ColorCalibrationStatus\":\"COLOR_CALIBRATION_ON_DEFAULT\"}");
        channel.SettingStatuses.Enqueue("{\"ColorCalibrationStatus\":\"COLOR_CALIBRATION_ON_ADOBERGB\"}");
        var controller = new GcuQuickControlsController(channel, (_, _) => Task.CompletedTask);

        var result = await controller.SetDisplayColorAsync(DisplayColorMode.AdobeRgb, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("{\"Action\":\"COLOR_CALIBRATION_ON_ADOBERGB\"}", channel.LastSettingCommand);
        Assert.Equal(2, channel.SettingReads);
    }

    [Fact]
    public async Task SetLocalDimming_WaitsForTargetStateToConverge()
    {
        var channel = new FakeChannel();
        channel.SettingStatuses.Enqueue("{\"sLocalDimming_Status\":\"LOCALDIMMING_OFF\"}");
        channel.SettingStatuses.Enqueue("{\"sLocalDimming_Status\":\"LOCALDIMMING_ON\"}");
        var controller = new GcuQuickControlsController(channel, (_, _) => Task.CompletedTask);

        var result = await controller.SetLocalDimmingAsync(true, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("{\"Action\":\"LOCALDIMMING_ON\"}", channel.LastSettingCommand);
        Assert.Equal(2, channel.SettingReads);
    }

    [Fact]
    public async Task SetDisplayColor_WhenStatusIsStillStale_KeepsAcceptedCommandPendingInsteadOfRejecting()
    {
        var channel = new FakeChannel
        {
            SettingStatus = "{\"ColorCalibrationSwitch\":0,\"CurrentColorCalibration\":0,\"ColorCalibrationResultCode\":\"Ready\"}"
        };
        var controller = new GcuQuickControlsController(channel, (_, _) => Task.CompletedTask);

        var result = await controller.SetDisplayColorEnabledAsync(true, DisplayColorMode.P3, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Contains("已提交", result.Reason, StringComparison.Ordinal);
        Assert.Equal("{\"Action\":\"COLOR_CALIBRATION_ON_P3\"}", channel.LastSettingCommand);
    }

    [Fact]
    public async Task SetDisplayColor_WhenOfficialResultReportsFailure_RejectsCommand()
    {
        var channel = new FakeChannel
        {
            SettingStatus = "{\"ColorCalibrationSwitch\":0,\"CurrentColorCalibration\":0,\"ColorCalibrationResultCode\":\"ColorCalibrationFail\"}"
        };
        var controller = new GcuQuickControlsController(channel, (_, _) => Task.CompletedTask);

        var result = await controller.SetDisplayColorEnabledAsync(true, DisplayColorMode.P3, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("ColorCalibrationFail", result.Reason, StringComparison.Ordinal);
    }

    private sealed class FakeChannel : IGcuQuickControlsChannel
    {
        public Exception? SettingReadException { get; set; }
        public Exception? DeviceReadException { get; set; }
        public Exception? ChargeLimitReadException { get; set; }
        public string SettingStatus { get; set; } = "{}";
        public string DeviceSwitchStatus { get; set; } = "{}";
        public string KeyboardStatus { get; set; } = "{}";
        public string BatteryStatus { get; set; } = "{}";
        public string LiquidStatus { get; set; } = "{}";
        public string? LastSettingCommand { get; private set; }
        public string? LastKeyboardCommand { get; private set; }
        public string? LastBatteryCommand { get; private set; }
        public string? LastLiquidCommand { get; private set; }
        public string? LastBatteryChargeLimitCommand { get; private set; }
        public int? BatteryChargeLimitPercent { get; set; }
        public int SettingReads { get; private set; }
        public int BatteryReads { get; private set; }
        public int LightingReads { get; private set; }
        public int AtomicLightingWrites { get; private set; }
        public Queue<string> SettingStatuses { get; } = new();
        public Queue<string> DeviceSwitchStatuses { get; } = new();
        public Queue<string> LiquidStatuses { get; } = new();
        public Dictionary<LightingZone, string> LightingStatuses { get; } = [];
        public LightingZone? LastLightingZone { get; private set; }
        public string? LastLightingCommand { get; private set; }
        public List<(LightingZone Zone, string Payload)> LightingWrites { get; } = [];

        public ValueTask<string> ReadSettingStatusAsync(CancellationToken cancellationToken)
        {
            SettingReads++;
            if (SettingReadException is { } exception) return ValueTask.FromException<string>(exception);
            return ValueTask.FromResult(SettingStatuses.Count > 0 ? SettingStatuses.Dequeue() : SettingStatus);
        }

        public ValueTask<string> ReadDeviceSwitchStatusAsync(CancellationToken cancellationToken) =>
            DeviceReadException is { } exception ? ValueTask.FromException<string>(exception) :
                ValueTask.FromResult(DeviceSwitchStatuses.Count > 0 ? DeviceSwitchStatuses.Dequeue() : DeviceSwitchStatus);

        public ValueTask<string> ReadKeyboardStatusAsync(CancellationToken cancellationToken) => ValueTask.FromResult(KeyboardStatus);
        public ValueTask<string> ReadLightingStatusAsync(LightingZone zone, CancellationToken cancellationToken)
        {
            LightingReads++;
            return ValueTask.FromResult(LightingStatuses.GetValueOrDefault(zone, zone switch
            {
                LightingZone.Keyboard => KeyboardStatus,
                LightingZone.LiquidCooler => LiquidStatus,
                _ => "{}"
            }));
        }
        public ValueTask<string> ReadBatteryProtectionAsync(CancellationToken cancellationToken)
        {
            BatteryReads++;
            return ValueTask.FromResult(BatteryStatus);
        }
        public ValueTask<string> ReadLiquidCoolingAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(LiquidStatuses.Count > 0 ? LiquidStatuses.Dequeue() : LiquidStatus);
        public ValueTask<int?> ReadBatteryChargeLimitAsync(CancellationToken cancellationToken) =>
            ChargeLimitReadException is { } exception ? ValueTask.FromException<int?>(exception) :
                ValueTask.FromResult(BatteryChargeLimitPercent);
        public ValueTask WriteSettingAsync(string payload, CancellationToken cancellationToken) { LastSettingCommand = payload; return ValueTask.CompletedTask; }
        public ValueTask WriteKeyboardAsync(string payload, CancellationToken cancellationToken) { LastKeyboardCommand = payload; return ValueTask.CompletedTask; }
        public ValueTask WriteLightingAsync(LightingZone zone, string payload, CancellationToken cancellationToken)
        {
            if (SimulateKeyboardPower && zone == LightingZone.Keyboard && payload.Contains("SetPower"))
                KeyboardStatus = KeyboardStatus.Replace("\"Off\"", "\"On\"");
            LastLightingZone = zone;
            LastLightingCommand = payload;
            LightingWrites.Add((zone, payload));
            return ValueTask.CompletedTask;
        }
        public async ValueTask<string> WriteLightingAndReadStatusAsync(
            LightingZone zone, string payload, CancellationToken cancellationToken)
        {
            AtomicLightingWrites++;
            await WriteLightingAsync(zone, payload, cancellationToken);
            return LightingStatuses.GetValueOrDefault(zone, zone switch
            {
                LightingZone.Keyboard => KeyboardStatus,
                LightingZone.LiquidCooler => LiquidStatus,
                _ => "{}"
            });
        }
        public bool SimulateKeyboardPower { get; init; }
        public ValueTask WriteBatteryProtectionAsync(string payload, CancellationToken cancellationToken) { LastBatteryCommand = payload; return ValueTask.CompletedTask; }
        public ValueTask WriteLiquidCoolingAsync(string payload, CancellationToken cancellationToken) { LastLiquidCommand = payload; return ValueTask.CompletedTask; }
        public ValueTask WriteBatteryChargeLimitAsync(string payload, CancellationToken cancellationToken) { LastBatteryChargeLimitCommand = payload; return ValueTask.CompletedTask; }
    }
}
