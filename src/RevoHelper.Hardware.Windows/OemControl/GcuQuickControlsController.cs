using System.Collections.Concurrent;
using RevoHelper.Core.Control;

namespace RevoHelper.Hardware.Windows.OemControl;

public sealed class GcuQuickControlsController : IQuickControlsController
{
    private readonly IGcuQuickControlsChannel channel;
    private readonly Func<TimeSpan, CancellationToken, Task> verificationDelay;
    private readonly SemaphoreSlim liquidCoolingOperationLock = new(1, 1);
    private readonly SemaphoreSlim lightingOperationLock = new(1, 1);
    private readonly ConcurrentDictionary<LightingZone, LightingZoneState> lightingZoneCache = new();

    public GcuQuickControlsController(
        IGcuQuickControlsChannel channel,
        Func<TimeSpan, CancellationToken, Task>? verificationDelay = null)
    {
        this.channel = channel;
        this.verificationDelay = verificationDelay ?? Task.Delay;
    }

    public async ValueTask<ShortcutState?> ReadShortcutsAsync(CancellationToken cancellationToken) =>
        GcuQuickControlsStatusParser.ParseShortcuts(await channel.ReadSettingStatusAsync(cancellationToken).ConfigureAwait(false));

    public async ValueTask<SystemToggleState?> ReadSystemTogglesAsync(CancellationToken cancellationToken)
    {
        var device = await ReadIndependentChannelAsync("设备开关状态",
            () => channel.ReadDeviceSwitchStatusAsync(cancellationToken), cancellationToken).ConfigureAwait(false);
        var setting = await ReadIndependentChannelAsync("快捷设置状态",
            () => channel.ReadSettingStatusAsync(cancellationToken), cancellationToken).ConfigureAwait(false);
        var warnings = ReadWarnings(device.Failure, setting.Failure);
        if (device.Failure is not null && setting.Failure is not null)
            throw new InvalidOperationException(string.Join("；", warnings));

        var state = GcuQuickControlsStatusParser.MergeSystemToggles(
            GcuQuickControlsStatusParser.ParseSystemToggles(device.Value ?? "{}"),
            GcuQuickControlsStatusParser.ParseSystemToggles(setting.Value ?? "{}"));
        if (state is null && warnings.Length == 0) return null;
        return (state ?? new(null, null, null, null, null)) with { ReadWarnings = warnings };
    }

    public async ValueTask<PowerSettingsState?> ReadPowerSettingsAsync(CancellationToken cancellationToken)
    {
        var setting = await ReadIndependentChannelAsync("电源设置状态",
            () => channel.ReadSettingStatusAsync(cancellationToken), cancellationToken).ConfigureAwait(false);
        var chargeLimit = await ReadIndependentChannelAsync("充电上限状态",
            () => channel.ReadBatteryChargeLimitAsync(cancellationToken), cancellationToken).ConfigureAwait(false);
        var warnings = ReadWarnings(setting.Failure, chargeLimit.Failure);
        if (setting.Failure is not null && chargeLimit.Failure is not null)
            throw new InvalidOperationException(string.Join("；", warnings));

        var state = GcuQuickControlsStatusParser.ParsePowerSettings(setting.Value ?? "{}", chargeLimit.Value);
        if (state is null && warnings.Length == 0) return null;
        return (state ?? new(null, false, null, null, null, null, null)) with
        {
            ReadWarnings = warnings,
            SettingStatusReadSucceeded = setting.Failure is null
        };
    }

    private static async ValueTask<(T? Value, string? Failure)> ReadIndependentChannelAsync<T>(
        string label, Func<ValueTask<T>> read, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try { return (await read().ConfigureAwait(false), null); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return (default, $"{label}读取失败：{exception.Message}");
        }
    }

    private static string[] ReadWarnings(params string?[] failures) =>
        failures.OfType<string>().ToArray();

    private static ControlResult WithReadWarnings(ControlResult result, IReadOnlyList<string>? warnings) =>
        warnings is { Count: > 0 }
            ? result with { Reason = $"{result.Reason} 部分状态未读取：{string.Join("；", warnings)}" }
            : result;

    public async ValueTask<KeyboardLightingState?> ReadKeyboardLightingAsync(CancellationToken cancellationToken) =>
        GcuQuickControlsStatusParser.ParseKeyboardLighting(await channel.ReadKeyboardStatusAsync(cancellationToken).ConfigureAwait(false));

    public async ValueTask<LightingZoneState?> ReadLightingZoneAsync(
        LightingZone zone,
        CancellationToken cancellationToken)
    {
        try
        {
            var state = MergeLightingState(
                lightingZoneCache.GetValueOrDefault(zone),
                GcuQuickControlsStatusParser.ParseLightingZone(
                zone,
                await channel.ReadLightingStatusAsync(zone, cancellationToken).ConfigureAwait(false)));
            if (state is not null) lightingZoneCache[zone] = state;
            return state;
        }
        catch (TimeoutException)
        {
            return new(false, null, null, []);
        }
    }

    public async ValueTask<BatteryProtectionState?> ReadBatteryProtectionAsync(CancellationToken cancellationToken) =>
        GcuQuickControlsStatusParser.ParseBatteryProtection(await channel.ReadBatteryProtectionAsync(cancellationToken).ConfigureAwait(false));

    public async ValueTask<DisplayFeatureState?> ReadDisplayFeaturesAsync(CancellationToken cancellationToken) =>
        GcuQuickControlsStatusParser.ParseDisplayFeatures(await channel.ReadSettingStatusAsync(cancellationToken).ConfigureAwait(false));

    public async ValueTask<LiquidCoolingState?> ReadLiquidCoolingAsync(CancellationToken cancellationToken) =>
        GcuQuickControlsStatusParser.ParseLiquidCooling(await channel.ReadLiquidCoolingAsync(cancellationToken).ConfigureAwait(false));

    public ValueTask<LiquidCoolingState?> ScanLiquidCoolingDevicesAsync(CancellationToken cancellationToken) =>
        WaitForLiquidCoolingStateAsync(
            state => state?.Connected == true || state?.Devices.Count > 0 ||
                !string.IsNullOrWhiteSpace(state?.SelectedDeviceId),
            cancellationToken);

    public async ValueTask<ControlResult> SetShortcutAsync(ShortcutControl control, bool enabled, CancellationToken cancellationToken)
    {
        await channel.WriteSettingAsync(GcuQuickControlsCommand.Shortcut(control, enabled), cancellationToken).ConfigureAwait(false);
        var payload = await channel.ReadSettingStatusAsync(cancellationToken).ConfigureAwait(false);
        var actual = GcuQuickControlsStatusParser.ParseShortcut(payload, control);
        return actual == enabled
            ? new(true, $"{ShortcutLabel(control)}已{(enabled ? "启用" : "禁用")}并回读确认。")
            : ControlResult.Rejected($"{ShortcutLabel(control)}写入后未得到目标状态回读。");
    }

    public async ValueTask<ControlResult> SetSystemToggleAsync(
        SystemToggleControl control, bool enabled, CancellationToken cancellationToken)
    {
        await channel.WriteSettingAsync(GcuQuickControlsCommand.SystemToggle(control, enabled), cancellationToken).ConfigureAwait(false);
        var slowRadioTransition = control == SystemToggleControl.Bluetooth;
        var state = await WaitForSystemTogglesAsync(
                candidate => ReadSystemToggle(candidate, control) == enabled,
                slowRadioTransition ? 15 : 5,
                slowRadioTransition ? TimeSpan.FromMilliseconds(400) : TimeSpan.FromMilliseconds(180),
                cancellationToken)
            .ConfigureAwait(false);
        var actual = ReadSystemToggle(state, control);
        return WithReadWarnings(actual == enabled
            ? new(true, $"{SystemToggleLabel(control)}已{(enabled ? "开启" : "关闭")}并回读确认。")
            : actual is null
                ? ControlResult.Rejected($"{SystemToggleLabel(control)}命令已发送，但设备状态接口未返回该项，无法确认实际状态。")
                : ControlResult.Rejected($"{SystemToggleLabel(control)}写入后未得到目标状态回读。"), state?.ReadWarnings);
    }

    public async ValueTask<ControlResult> SetPowerToggleAsync(
        PowerToggleControl control, bool enabled, CancellationToken cancellationToken)
    {
        await channel.WriteSettingAsync(GcuQuickControlsCommand.PowerToggle(control, enabled), cancellationToken).ConfigureAwait(false);
        var state = await WaitForPowerSettingsAsync(candidate => ReadPowerToggle(candidate, control) == enabled, cancellationToken)
            .ConfigureAwait(false);
        var actual = ReadPowerToggle(state, control);
        return WithReadWarnings(actual == enabled
            ? new(true, $"{PowerToggleLabel(control)}已{(enabled ? "开启" : "关闭")}并回读确认。")
            : actual is null
                ? ControlResult.Rejected($"{PowerToggleLabel(control)}命令已发送，但 OEM 状态未返回该项，无法确认实际状态。")
                : ControlResult.Rejected($"{PowerToggleLabel(control)}写入后未得到目标状态回读。"), state?.ReadWarnings);
    }

    public async ValueTask<ControlResult> SetDeepSleepAsync(bool enabled, int minutes, CancellationToken cancellationToken)
    {
        if (minutes is < 5 or > 120) return ControlResult.Rejected("深度睡眠等待时间必须在 5–120 分钟之间。");
        await channel.WriteSettingAsync(GcuQuickControlsCommand.DeepSleep(enabled, minutes), cancellationToken).ConfigureAwait(false);
        var state = await WaitForPowerSettingsAsync(candidate =>
            candidate?.DeepSleepEnabled == enabled && (!enabled || candidate.DeepSleepMinutes == minutes), cancellationToken)
            .ConfigureAwait(false);
        return WithReadWarnings(state?.DeepSleepEnabled == enabled && (!enabled || state.DeepSleepMinutes == minutes)
            ? new(true, $"深度睡眠已{(enabled ? $"开启（{minutes} 分钟）" : "关闭")}并回读确认。")
            : ControlResult.Rejected("深度睡眠写入后未得到目标状态回读。"), state?.ReadWarnings);
    }

    public async ValueTask<ControlResult> SetBatteryChargeLimitAsync(int percent, CancellationToken cancellationToken)
    {
        if (percent is < 50 or > 100) return ControlResult.Rejected("充电上限必须在 50%–100% 之间。");
        await channel.WriteBatteryChargeLimitAsync(GcuQuickControlsCommand.BatteryChargeLimit(percent), cancellationToken)
            .ConfigureAwait(false);
        int? actual = null;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            actual = await channel.ReadBatteryChargeLimitAsync(cancellationToken).ConfigureAwait(false);
            if (actual == percent) return new(true, $"充电上限已设为 {percent}% 并从 OEM 状态回读确认。");
            if (attempt < 5) await verificationDelay(TimeSpan.FromMilliseconds(220), cancellationToken).ConfigureAwait(false);
        }
        return actual is null
            ? ControlResult.Rejected("此机型未返回 BatteryChargingLimit，无法确认充电上限。")
            : ControlResult.Rejected($"充电上限写入后回读为 {actual}%，未达到 {percent}%。");
    }

    public async ValueTask<ControlResult> SetKeyboardLightingAsync(bool enabled, CancellationToken cancellationToken)
    {
        await channel.WriteKeyboardAsync(GcuQuickControlsCommand.KeyboardBacklight(enabled), cancellationToken).ConfigureAwait(false);
        var state = await ReadKeyboardLightingAsync(cancellationToken).ConfigureAwait(false);
        return state?.Enabled == enabled
            ? new(true, $"键盘背光已{(enabled ? "开启" : "关闭")}并回读确认。")
            : ControlResult.Rejected("键盘背光写入后未得到目标状态回读。");
    }

    public async ValueTask<ControlResult> SetLightingZonePowerAsync(
        LightingZone zone, bool enabled, CancellationToken cancellationToken)
    {
        await lightingOperationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await SetLightingZonePowerCoreAsync(zone, enabled, cancellationToken).ConfigureAwait(false); }
        finally { lightingOperationLock.Release(); }
    }

    private async ValueTask<ControlResult> SetLightingZonePowerCoreAsync(
        LightingZone zone,
        bool enabled,
        CancellationToken cancellationToken)
    {
        var baseline = lightingZoneCache.GetValueOrDefault(zone);
        if (baseline?.Available != true)
            baseline = await ReadLightingZoneAsync(zone, cancellationToken).ConfigureAwait(false);
        if (baseline?.Available != true)
            return ControlResult.Rejected($"未从官方状态主题发现{LightingZoneLabel(zone)}，未发送命令。");

        if (zone == LightingZone.LiquidCooler)
        {
            if (baseline.Configuration is not { } waterConfiguration)
                return ControlResult.Rejected("水冷灯未返回当前配置，无法安全切换总开关。");
            return await ApplyLightingZoneCoreAsync(
                waterConfiguration with { Enabled = enabled }, cancellationToken).ConfigureAwait(false);
        }

        if (enabled && zone is LightingZone.CoastLightbar or LightingZone.Logo)
        {
            if (baseline.Configuration is not { } currentConfiguration)
                return ControlResult.Rejected($"{LightingZoneLabel(zone)}未返回当前配置，无法建立可见的开启状态。");
            return await ApplyLightingZoneCoreAsync(
                EnsureVisibleLightingConfiguration(currentConfiguration with { Enabled = true }, baseline.HasDetailedConfiguration),
                cancellationToken).ConfigureAwait(false);
        }

        if (zone == LightingZone.Logo && !enabled)
            return await TurnOffLogoPreservingCoastAsync(cancellationToken).ConfigureAwait(false);

        var readbackPayload = await channel.WriteLightingAndReadStatusAsync(
            zone,
            GcuQuickControlsCommand.KeyboardBacklight(enabled),
            cancellationToken).ConfigureAwait(false);
        var state = MergeLightingState(
            baseline,
            GcuQuickControlsStatusParser.ParseLightingZone(zone, readbackPayload));
        if (state is not null) lightingZoneCache[zone] = state;
        return state?.Configuration?.Enabled == enabled
            ? new(true, $"{LightingZoneLabel(zone)}已{(enabled ? "开启" : "关闭")}并回读确认。")
            : ControlResult.Rejected($"{LightingZoneLabel(zone)}电源写入后未得到目标状态回读。");
    }

    public async ValueTask<ControlResult> ApplyLightingZoneAsync(
        LightingZoneConfiguration configuration, CancellationToken cancellationToken)
    {
        await lightingOperationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await ApplyLightingZoneCoreAsync(configuration, cancellationToken).ConfigureAwait(false); }
        finally { lightingOperationLock.Release(); }
    }

    private async ValueTask<ControlResult> ApplyLightingZoneCoreAsync(
        LightingZoneConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (configuration.Brightness is < 0 or > 4 || configuration.Speed is < 1 or > 4)
            return ControlResult.Rejected("灯光亮度必须为 0–4，速度必须为 1–4。");

        var baseline = lightingZoneCache.GetValueOrDefault(configuration.Zone);
        if (baseline?.Available != true)
            baseline = await ReadLightingZoneAsync(configuration.Zone, cancellationToken).ConfigureAwait(false);
        if (baseline?.Available != true)
            return ControlResult.Rejected($"未从官方状态主题发现{LightingZoneLabel(configuration.Zone)}，未发送命令。");
        if (configuration.Zone == LightingZone.Logo && !configuration.Enabled)
            return await TurnOffLogoPreservingCoastAsync(cancellationToken).ConfigureAwait(false);
        if (configuration.Effect != RevoHelper.Core.Control.LightingEffect.OemDefault &&
            !baseline.SupportedEffects.Contains(configuration.Effect))
            return ControlResult.Rejected($"当前{LightingZoneLabel(configuration.Zone)}不支持 {configuration.Effect} 灯效。");

        if (configuration.Enabled &&
            configuration.Zone is LightingZone.CoastLightbar or LightingZone.Logo)
            configuration = EnsureVisibleLightingConfiguration(configuration, true);
        else if (configuration.Enabled &&
                 configuration.Zone == LightingZone.LiquidCooler &&
                 configuration.Effect == RevoHelper.Core.Control.LightingEffect.Static &&
                 configuration.Color == new LightingRgb(0, 0, 0))
            configuration = configuration with { Color = new(0, 176, 255) };

        if (configuration.Enabled &&
            configuration.Zone is LightingZone.CoastLightbar or LightingZone.Logo)
        {
            await channel.WriteLightingAsync(
                configuration.Zone,
                GcuQuickControlsCommand.KeyboardBacklight(true),
                cancellationToken).ConfigureAwait(false);
        }

        if (configuration.Zone == LightingZone.LiquidCooler)
        {
            foreach (var dynamicEffect in new[]
                     {
                         RevoHelper.Core.Control.LightingEffect.Breathing,
                         RevoHelper.Core.Control.LightingEffect.Colorful,
                         RevoHelper.Core.Control.LightingEffect.Rotate
                     })
            {
                await channel.WriteLightingAsync(
                    LightingZone.LiquidCooler,
                    GcuQuickControlsCommand.WaterEffectOff(dynamicEffect),
                    cancellationToken).ConfigureAwait(false);
            }
        }

        // SetEffectALL changes the stored effect but does not enable the keyboard's
        // independent power switch. A cached On state must not skip this command.
        if (configuration.Enabled && configuration.Zone == LightingZone.Keyboard &&
            configuration.Effect != RevoHelper.Core.Control.LightingEffect.OemDefault)
            await channel.WriteLightingAsync(LightingZone.Keyboard,
                GcuQuickControlsCommand.KeyboardBacklight(true), cancellationToken).ConfigureAwait(false);

        var effectPayload = GcuQuickControlsCommand.LightingEffect(configuration, baseline.HardwareType);
        string readbackPayload;
        if (configuration.Enabled &&
            configuration.Zone is LightingZone.CoastLightbar or LightingZone.Logo)
        {
            await channel.WriteLightingAsync(configuration.Zone, effectPayload, cancellationToken)
                .ConfigureAwait(false);
            readbackPayload = await channel.WriteLightingAndReadStatusAsync(
                configuration.Zone,
                GcuQuickControlsCommand.KeyboardBacklight(true),
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            readbackPayload = await channel.WriteLightingAndReadStatusAsync(
                configuration.Zone,
                effectPayload,
                cancellationToken).ConfigureAwait(false);
        }
        var parsedState = GcuQuickControlsStatusParser.ParseLightingZone(configuration.Zone, readbackPayload);
        var state = MergeLightingState(baseline, parsedState);
        var reportedFields = parsedState?.EffectiveReportedFields ?? LightingReportedFields.None;
        var sparseEffectAccepted = reportedFields.HasFlag(LightingReportedFields.Power) &&
                                   (reportedFields & ~(LightingReportedFields.Power | LightingReportedFields.Brightness)) == 0 &&
                                   parsedState?.Configuration?.Enabled == configuration.Enabled &&
                                   (!configuration.Enabled || !reportedFields.HasFlag(LightingReportedFields.Brightness) ||
                                    parsedState.Configuration.Brightness == configuration.Brightness);
        if (sparseEffectAccepted)
        {
            state = new(
                true,
                parsedState!.HardwareType ?? baseline.HardwareType,
                configuration,
                parsedState.SupportedEffects.Count > 0 ? parsedState.SupportedEffects : baseline.SupportedEffects,
                false);
        }

        if (configuration.Enabled &&
            configuration.Zone is LightingZone.CoastLightbar or LightingZone.Logo)
        {
            if (state?.Configuration?.Enabled != true)
                return ControlResult.Rejected(
                    $"{LightingZoneLabel(configuration.Zone)}灯效已发送，但最终开启状态未得到回读确认。");
        }

        if (state is not null) lightingZoneCache[configuration.Zone] = state;
        if (sparseEffectAccepted)
            return new(true,
                $"{LightingZoneLabel(configuration.Zone)}命令已发送；仅确认电源已{(configuration.Enabled ? "开启" : "关闭")}，灯效与颜色未回报，请观察实体灯。");
        if (LightingMatches(state?.Configuration, configuration))
            return new(true, $"{LightingZoneLabel(configuration.Zone)}灯效已应用并回读确认。");
        return ControlResult.Rejected(
            $"{LightingZoneLabel(configuration.Zone)}命令已发送，但回读未匹配目标状态，未确认生效。");
    }

    private async ValueTask<ControlResult> TurnOffLogoPreservingCoastAsync(CancellationToken cancellationToken)
    {
        // Use fresh, explicitly reported power, never an old merged cache or a guessed default.
        LightingZoneState? coast = null;
        try
        {
            coast = GcuQuickControlsStatusParser.ParseLightingZone(LightingZone.CoastLightbar,
                await channel.ReadLightingStatusAsync(LightingZone.CoastLightbar, cancellationToken).ConfigureAwait(false));
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { }
        var preserveCoast = coast?.EffectiveReportedFields.HasFlag(LightingReportedFields.Power) == true &&
                            coast.Configuration?.Enabled == true;
        string? failure = null;
        try
        {
            var logo = GcuQuickControlsStatusParser.ParseLightingZone(LightingZone.Logo,
                await channel.WriteLightingAndReadStatusAsync(LightingZone.Logo,
                    GcuQuickControlsCommand.KeyboardBacklight(false), cancellationToken).ConfigureAwait(false));
            if (logo is not null) lightingZoneCache[LightingZone.Logo] = logo;
            if (logo?.Configuration?.Enabled != false) failure = "LOGO 关闭未得到回读确认。";
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { failure = $"LOGO 关闭回读失败：{exception.Message}"; }
        // The OEM command can blank the physical coast strip without changing its saved On flag.
        // Reassert power only: do not reset its color/effect or turn an originally-off strip on.
        if (preserveCoast)
        {
            try
            {
                var restored = GcuQuickControlsStatusParser.ParseLightingZone(LightingZone.CoastLightbar,
                    await channel.WriteLightingAndReadStatusAsync(LightingZone.CoastLightbar,
                        GcuQuickControlsCommand.KeyboardBacklight(true), cancellationToken).ConfigureAwait(false));
                if (restored is not null) lightingZoneCache[LightingZone.CoastLightbar] = MergeLightingState(coast, restored)!;
                if (restored?.Configuration?.Enabled != true) failure = "LOGO 关闭已请求，但海岸灯补偿未得到回读确认。";
            }
            catch (Exception exception) when (exception is not OperationCanceledException) { failure = $"海岸灯补偿失败：{exception.Message}"; }
        }
        return failure is not null ? ControlResult.Rejected(failure) : new(true, preserveCoast
            ? "LOGO 已关闭，已补发海岸灯开启命令；请以实体灯为准。"
            : "LOGO 已关闭，未改动海岸灯开关。");
    }

    public async ValueTask<ControlResult> SetLightingSleepTimerAsync(int seconds, CancellationToken cancellationToken)
    {
        if (!LightingProfileCatalog.OfficialSleepSeconds.Contains(seconds))
            return ControlResult.Rejected("灯效休眠时间不是官方支持的档位，未发送命令。");
        await channel.WriteSettingAsync(GcuQuickControlsCommand.LightingSleepTimer(seconds), cancellationToken)
            .ConfigureAwait(false);

        int? actual = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            actual = GcuQuickControlsStatusParser.ParseLightingSleepTimer(
                await channel.ReadSettingStatusAsync(cancellationToken).ConfigureAwait(false));
            if (actual == seconds)
                return new(true, seconds == 0
                    ? "键盘与灯带自动休眠已关闭并回读确认。"
                    : $"键盘与灯带自动熄灯档位 {FormatLightingSleepSeconds(seconds)} 已回读确认。");
            if (attempt < 2)
                await verificationDelay(TimeSpan.FromMilliseconds(160), cancellationToken).ConfigureAwait(false);
        }

        return ControlResult.Rejected(actual is null
            ? "休眠命令已发送，但官方状态未返回 CloseTimer，无法确认。"
            : $"休眠命令已发送，但官方状态仍为 {actual}，未确认生效。");
    }

    private static string FormatLightingSleepSeconds(int seconds) => seconds < 60
        ? $"{seconds} 秒"
        : seconds % 3600 == 0
            ? $"{seconds / 3600} 小时"
            : $"{seconds / 60} 分钟";

    public async ValueTask<ControlResult> SetBatteryProtectionAsync(BatteryProtectionMode mode, CancellationToken cancellationToken)
    {
        await channel.WriteBatteryProtectionAsync(GcuQuickControlsCommand.BatteryProtection(mode), cancellationToken).ConfigureAwait(false);
        var state = await ReadBatteryProtectionAsync(cancellationToken).ConfigureAwait(false);
        return state?.Mode == mode
            ? new(true, "电池保护模式已应用并回读确认。")
            : ControlResult.Rejected("电池保护模式写入后未得到目标状态回读。");
    }

    public async ValueTask<ControlResult> SetDisplayColorAsync(DisplayColorMode mode, CancellationToken cancellationToken)
    {
        await channel.WriteSettingAsync(GcuQuickControlsCommand.DisplayColor(mode), cancellationToken).ConfigureAwait(false);
        var state = await WaitForDisplayFeaturesAsync(
            candidate => candidate?.ColorCalibrationEnabled != false && candidate?.ColorMode == mode ||
                         IsColorCalibrationFailure(candidate?.ColorCalibrationResultCode),
            cancellationToken).ConfigureAwait(false);
        if (IsColorCalibrationFailure(state?.ColorCalibrationResultCode))
            return ControlResult.Rejected($"{DisplayColorLabel(mode)} 色域应用失败：{state!.ColorCalibrationResultCode}。");
        return state?.ColorCalibrationEnabled != false && state?.ColorMode == mode
            ? new(true, $"{DisplayColorLabel(mode)} 色域已应用并回读确认。")
            : new(true, $"{DisplayColorLabel(mode)} 色域命令已提交，官方 ICC 校准流程仍在处理。");
    }

    public async ValueTask<ControlResult> SetDisplayColorEnabledAsync(
        bool enabled,
        DisplayColorMode mode,
        CancellationToken cancellationToken)
    {
        await channel.WriteSettingAsync(GcuQuickControlsCommand.DisplayColorEnabled(enabled, mode), cancellationToken).ConfigureAwait(false);
        var state = await WaitForDisplayFeaturesAsync(
            candidate => candidate?.ColorCalibrationEnabled == enabled &&
                         (!enabled || candidate.ColorMode == mode) ||
                         IsColorCalibrationFailure(candidate?.ColorCalibrationResultCode),
            cancellationToken).ConfigureAwait(false);
        if (IsColorCalibrationFailure(state?.ColorCalibrationResultCode))
            return ControlResult.Rejected($"色彩管理应用失败：{state!.ColorCalibrationResultCode}。");
        return state?.ColorCalibrationEnabled == enabled && (!enabled || state.ColorMode == mode)
            ? new(true, enabled
                ? $"色彩管理已开启，{DisplayColorLabel(mode)} 色域已回读确认。"
                : "色彩管理已关闭并回读确认。")
            : new(true, enabled
                ? $"色彩管理与 {DisplayColorLabel(mode)} 色域命令已提交，官方 ICC 校准流程仍在处理。"
                : "关闭色彩管理命令已提交，官方状态仍在更新。");
    }

    public async ValueTask<ControlResult> SetLocalDimmingAsync(bool enabled, CancellationToken cancellationToken)
    {
        await channel.WriteSettingAsync(GcuQuickControlsCommand.LocalDimming(enabled), cancellationToken).ConfigureAwait(false);
        var state = await WaitForDisplayFeaturesAsync(
            candidate => candidate?.LocalDimmingEnabled == enabled,
            cancellationToken).ConfigureAwait(false);
        return state?.LocalDimmingEnabled == enabled
            ? new(true, $"Mini LED 局部调光已{(enabled ? "开启" : "关闭")}并回读确认。")
            : ControlResult.Rejected("Mini LED 局部调光写入后未得到目标状态回读。");
    }

    public async ValueTask<ControlResult> SetLiquidCoolingFanAsync(LiquidCoolingFanMode mode, CancellationToken cancellationToken)
    {
        var baseline = await ReadLiquidCoolingAsync(cancellationToken).ConfigureAwait(false);
        if (baseline?.Connected != true) return ControlResult.Rejected("未检测到已连接的官方水冷设备，未发送控制命令。");
        await channel.WriteLiquidCoolingAsync(GcuQuickControlsCommand.LiquidCoolingFan(mode), cancellationToken).ConfigureAwait(false);
        var state = await WaitForLiquidCoolingStateAsync(
            candidate => candidate?.Connected == true && candidate.FanMode == mode, cancellationToken).ConfigureAwait(false);
        return state?.Connected == true && state.FanMode == mode
            ? new(true, "水冷风扇策略已应用并回读确认。")
            : ControlResult.Rejected("水冷风扇策略写入后未得到目标状态回读。");
    }

    public async ValueTask<ControlResult> SetLiquidCoolingPumpAsync(LiquidCoolingPumpMode mode, CancellationToken cancellationToken)
    {
        var baseline = await ReadLiquidCoolingAsync(cancellationToken).ConfigureAwait(false);
        if (baseline?.Connected != true) return ControlResult.Rejected("未检测到已连接的官方水冷设备，未发送控制命令。");
        await channel.WriteLiquidCoolingAsync(GcuQuickControlsCommand.LiquidCoolingPump(mode), cancellationToken).ConfigureAwait(false);
        var state = await WaitForLiquidCoolingStateAsync(
            candidate => candidate?.Connected == true && candidate.PumpMode == mode, cancellationToken).ConfigureAwait(false);
        return state?.Connected == true && state.PumpMode == mode
            ? new(true, "水冷泵策略已应用并回读确认。")
            : ControlResult.Rejected("水冷泵策略写入后未得到目标状态回读。");
    }

    public async ValueTask<ControlResult> SelectLiquidCoolingDeviceAsync(string deviceId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return ControlResult.Rejected("请先选择扫描到的水冷设备。");
        await liquidCoolingOperationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var baseline = await ReadLiquidCoolingAsync(cancellationToken).ConfigureAwait(false);
            var discovered = baseline?.Devices.Any(device =>
                string.Equals(device.Id, deviceId, StringComparison.OrdinalIgnoreCase)) == true;
            // GCU can retain DevMACString while its transient discovery list is empty.
            // Reuse that existing selection; do not rewrite or invent a device identity.
            if (!discovered && string.Equals(baseline?.SelectedDeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
                return new(true, "使用 GCU 已记住的水冷设备，未改写设备记录。");
            if (!discovered) return ControlResult.Rejected("所选设备不在本次 GCU 扫描结果中，未发送命令。");
            await channel.WriteLiquidCoolingAsync(GcuQuickControlsCommand.LiquidCoolingDevice(deviceId), cancellationToken).ConfigureAwait(false);
            var state = await WaitForLiquidCoolingStateAsync(
                candidate => string.Equals(candidate?.SelectedDeviceId, deviceId, StringComparison.OrdinalIgnoreCase),
                cancellationToken).ConfigureAwait(false);
            return string.Equals(state?.SelectedDeviceId, deviceId, StringComparison.OrdinalIgnoreCase)
                ? new(true, "水冷设备已选择并回读确认。")
                : ControlResult.Rejected("设备选择写入后未得到目标状态回读。");
        }
        finally
        {
            liquidCoolingOperationLock.Release();
        }
    }

    public ValueTask<ControlResult> ConnectLiquidCoolingAsync(CancellationToken cancellationToken) =>
        SetLiquidCoolingConnectionAsync(true, cancellationToken);

    public ValueTask<ControlResult> DisconnectLiquidCoolingAsync(CancellationToken cancellationToken) =>
        SetLiquidCoolingConnectionAsync(false, cancellationToken);

    public async ValueTask<ControlResult> StartLiquidCoolingPrimingAsync(CancellationToken cancellationToken)
    {
        await liquidCoolingOperationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var baseline = await ReadLiquidCoolingAsync(cancellationToken).ConfigureAwait(false);
            if (baseline?.Connected != true) return ControlResult.Rejected("水冷设备尚未连接，未启动注水模式。");
            if (!baseline.CanControl) return ControlResult.Rejected("当前水冷状态不允许控制，未启动注水模式。");
            if (baseline.IsPriming) return new(true, "当前已经处于注水模式，请等待流程自动结束。");
            await channel.WriteLiquidCoolingAsync(
                GcuQuickControlsCommand.LiquidCoolingAction(LiquidCoolingDeviceAction.InputWater), cancellationToken).ConfigureAwait(false);
            var state = await ReadLiquidCoolingAsync(cancellationToken).ConfigureAwait(false);
            return new(true, state?.IsPriming == true
                ? "当前状态为注水模式，请等待流程自动结束。"
                : "注水模式命令已发送，请等待水冷设备完成固定流程。");
        }
        finally
        {
            liquidCoolingOperationLock.Release();
        }
    }

    public async ValueTask<ControlResult> ResetLiquidCoolingDeviceAsync(CancellationToken cancellationToken)
    {
        await liquidCoolingOperationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var baseline = await ReadLiquidCoolingAsync(cancellationToken).ConfigureAwait(false);
            if (baseline?.CanControl != true) return ControlResult.Rejected("当前水冷状态不允许重设，未发送命令。");
            await channel.WriteLiquidCoolingAsync(
                GcuQuickControlsCommand.LiquidCoolingAction(LiquidCoolingDeviceAction.Reset), cancellationToken).ConfigureAwait(false);
            var state = await WaitForLiquidCoolingStateAsync(
                candidate => candidate is not null && !candidate.Connected && string.IsNullOrWhiteSpace(candidate.SelectedDeviceId),
                cancellationToken).ConfigureAwait(false);
            return state is not null && !state.Connected && string.IsNullOrWhiteSpace(state.SelectedDeviceId)
                ? new(true, "水冷设备记录已清除并回读确认。")
                : ControlResult.Rejected("重设命令发送后，设备记录尚未清除。");
        }
        finally
        {
            liquidCoolingOperationLock.Release();
        }
    }

    private async ValueTask<ControlResult> SetLiquidCoolingConnectionAsync(bool connect, CancellationToken cancellationToken)
    {
        await liquidCoolingOperationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var baseline = await ReadLiquidCoolingAsync(cancellationToken).ConfigureAwait(false);
            if (baseline is null) return ControlResult.Rejected("GCU 未返回水冷状态，未发送连接命令。");
            if (baseline.Connected == connect)
                return new(true, connect ? "水冷设备已经连接。" : "水冷设备已经断开。");
            if (!baseline.CanControl) return ControlResult.Rejected("当前水冷状态不允许连接操作，未发送命令。");
            if (connect && string.IsNullOrWhiteSpace(baseline.SelectedDeviceId))
                return ControlResult.Rejected("请先选择扫描到的水冷设备。");

            await channel.WriteLiquidCoolingAsync(
                GcuQuickControlsCommand.LiquidCoolingAction(connect
                    ? LiquidCoolingDeviceAction.Connect
                    : LiquidCoolingDeviceAction.Disconnect), cancellationToken).ConfigureAwait(false);
            var state = await WaitForLiquidCoolingStateAsync(
                candidate => candidate?.Connected == connect, cancellationToken).ConfigureAwait(false);
            return state?.Connected == connect
                ? new(true, connect ? "水冷设备已连接并回读确认。" : "水冷设备已断开并回读确认。")
                : ControlResult.Rejected(connect ? "连接命令发送后未得到已连接状态。" : "断开命令发送后未得到已断开状态。");
        }
        finally
        {
            liquidCoolingOperationLock.Release();
        }
    }

    private static string ShortcutLabel(ShortcutControl control) => control switch
    {
        ShortcutControl.WinKey => "Win 键",
        ShortcutControl.FnKey => "Fn 键",
        ShortcutControl.Osd => "OSD 提示",
        _ => control.ToString()
    };

    private static string SystemToggleLabel(SystemToggleControl control) => control switch
    {
        SystemToggleControl.Wifi => "Wi-Fi",
        SystemToggleControl.Bluetooth => "蓝牙",
        SystemToggleControl.Touchpad => "触摸板",
        SystemToggleControl.Webcam => "网络摄像头",
        SystemToggleControl.CopilotKey => "Copilot 键",
        _ => control.ToString()
    };

    private static string PowerToggleLabel(PowerToggleControl control) => control switch
    {
        PowerToggleControl.UsbChargingWhileOff => "关机 USB 供电",
        PowerToggleControl.AcAutoBoot => "来电自动开机",
        PowerToggleControl.HighPerformancePowerMode => "Windows 电源模式跟随",
        PowerToggleControl.DeepSleep => "深度睡眠",
        _ => control.ToString()
    };

    private static bool? ReadSystemToggle(SystemToggleState? state, SystemToggleControl control) => control switch
    {
        SystemToggleControl.Wifi => state?.WifiEnabled,
        SystemToggleControl.Bluetooth => state?.BluetoothEnabled,
        SystemToggleControl.Touchpad => state?.TouchpadEnabled,
        SystemToggleControl.Webcam => state?.WebcamEnabled,
        SystemToggleControl.CopilotKey => state?.CopilotKeyEnabled,
        _ => null
    };

    private static bool? ReadPowerToggle(PowerSettingsState? state, PowerToggleControl control) => control switch
    {
        PowerToggleControl.UsbChargingWhileOff => state?.UsbChargingWhileOffEnabled,
        PowerToggleControl.AcAutoBoot => state?.AcAutoBootEnabled,
        PowerToggleControl.HighPerformancePowerMode => state?.HighPerformancePowerModeEnabled,
        PowerToggleControl.DeepSleep => state?.DeepSleepEnabled,
        _ => null
    };

    private static string DisplayColorLabel(DisplayColorMode mode) => mode switch
    {
        DisplayColorMode.Default => "Default",
        DisplayColorMode.Srgb => "sRGB",
        DisplayColorMode.P3 => "P3",
        DisplayColorMode.AdobeRgb => "Adobe RGB",
        _ => mode.ToString()
    };

    private static string LightingZoneLabel(LightingZone zone) => zone switch
    {
        LightingZone.Keyboard => "键盘背光",
        LightingZone.CoastLightbar => "海岸灯带",
        LightingZone.Logo => "LOGO 灯",
        LightingZone.LiquidCooler => "水冷灯",
        _ => zone.ToString()
    };

    private static bool LightingMatches(LightingZoneConfiguration? actual, LightingZoneConfiguration expected)
    {
        if (actual is null || actual.Enabled != expected.Enabled) return false;
        if (!expected.Enabled || expected.Effect == RevoHelper.Core.Control.LightingEffect.OemDefault) return true;
        if (actual.Effect != expected.Effect) return false;

        if (expected.Zone == LightingZone.LiquidCooler)
            return expected.Effect != RevoHelper.Core.Control.LightingEffect.Static || actual.Color == expected.Color;

        if (actual.Brightness != expected.Brightness || actual.Speed != expected.Speed) return false;
        var expectedDirection = expected.Zone == LightingZone.Keyboard &&
                                expected.Effect == RevoHelper.Core.Control.LightingEffect.Wave
            ? expected.Direction
            : LightingDirection.None;
        if (actual.Direction != expectedDirection) return false;
        return !UsesUserColor(expected.Effect) || actual.Color == expected.Color;
    }

    private static LightingZoneState? MergeLightingState(
        LightingZoneState? previous,
        LightingZoneState? current) => LightingStateAccumulator.Merge(previous, current);

    private static bool UsesUserColor(RevoHelper.Core.Control.LightingEffect effect) => effect is not (
        RevoHelper.Core.Control.LightingEffect.Rainbow or
        RevoHelper.Core.Control.LightingEffect.ColorMarquee or
        RevoHelper.Core.Control.LightingEffect.Colorful);

    private static LightingZoneConfiguration EnsureVisibleLightingConfiguration(
        LightingZoneConfiguration configuration,
        bool hasDetailedConfiguration)
    {
        if (!hasDetailedConfiguration)
        {
            return configuration with
            {
                Effect = RevoHelper.Core.Control.LightingEffect.Static,
                Color = new(0, 176, 255),
                Brightness = 3,
                Speed = 2,
                Direction = LightingDirection.None
            };
        }

        var color = UsesUserColor(configuration.Effect) && configuration.Color == new LightingRgb(0, 0, 0)
            ? new LightingRgb(0, 176, 255)
            : configuration.Color;
        return configuration with
        {
            Color = color,
            Brightness = configuration.Brightness == 0 ? 1 : configuration.Brightness
        };
    }

    private static bool IsColorCalibrationFailure(string? resultCode) =>
        resultCode?.Contains("Fail", StringComparison.OrdinalIgnoreCase) == true ||
        resultCode?.Contains("Network", StringComparison.OrdinalIgnoreCase) == true ||
        resultCode?.Contains("NotFound", StringComparison.OrdinalIgnoreCase) == true ||
        resultCode?.Contains("ConnectServer", StringComparison.OrdinalIgnoreCase) == true;

    private async ValueTask<DisplayFeatureState?> WaitForDisplayFeaturesAsync(
        Func<DisplayFeatureState?, bool> isTarget,
        CancellationToken cancellationToken)
    {
        DisplayFeatureState? latest = null;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            latest = await ReadDisplayFeaturesAsync(cancellationToken).ConfigureAwait(false);
            if (isTarget(latest)) return latest;
            if (attempt < 4)
                await verificationDelay(TimeSpan.FromMilliseconds(180), cancellationToken).ConfigureAwait(false);
        }
        return latest;
    }

    private async ValueTask<SystemToggleState?> WaitForSystemTogglesAsync(
        Func<SystemToggleState?, bool> isTarget,
        int maximumAttempts,
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        SystemToggleState? latest = null;
        for (var attempt = 0; attempt < maximumAttempts; attempt++)
        {
            latest = await ReadSystemTogglesAsync(cancellationToken).ConfigureAwait(false);
            if (isTarget(latest) || latest is null) return latest;
            if (attempt + 1 < maximumAttempts) await verificationDelay(delay, cancellationToken).ConfigureAwait(false);
        }
        return latest;
    }

    private async ValueTask<PowerSettingsState?> WaitForPowerSettingsAsync(
        Func<PowerSettingsState?, bool> isTarget, CancellationToken cancellationToken)
    {
        PowerSettingsState? latest = null;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            latest = await ReadPowerSettingsAsync(cancellationToken).ConfigureAwait(false);
            if (isTarget(latest) || latest is null) return latest;
            if (attempt < 4) await verificationDelay(TimeSpan.FromMilliseconds(180), cancellationToken).ConfigureAwait(false);
        }
        return latest;
    }

    private async ValueTask<LiquidCoolingState?> WaitForLiquidCoolingStateAsync(
        Func<LiquidCoolingState?, bool> isTarget,
        CancellationToken cancellationToken)
    {
        LiquidCoolingState? latest = null;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            latest = await ReadLiquidCoolingAsync(cancellationToken).ConfigureAwait(false);
            if (isTarget(latest)) return latest;
            if (attempt < 7)
                await verificationDelay(TimeSpan.FromMilliseconds(400), cancellationToken).ConfigureAwait(false);
        }
        return latest;
    }
}
