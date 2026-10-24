using RevoHelper.Core.Control;

namespace RevoHelper.Hardware.Windows.OemControl;

public sealed partial class GcuCustomPerformanceController
{
    public ValueTask<ControlResult> ApplySettingAsync(CustomPerformanceSetting setting, int value, CancellationToken cancellationToken) =>
        ExecuteForSelectionAsync(null, true, () => ApplySettingCoreAsync(setting, value, cancellationToken), cancellationToken);

    public ValueTask<ControlResult> ApplySettingAsync(int profileIndex, CustomPerformanceSetting setting, int value, CancellationToken cancellationToken) =>
        ExecuteForSelectionAsync(profileIndex, true, () => ApplySettingCoreAsync(setting, value, cancellationToken), cancellationToken);

    private async ValueTask<ControlResult> ApplySettingCoreAsync(
        CustomPerformanceSetting setting, int value, CancellationToken cancellationToken)
    {
        if (powerBudget is not null && setting is CustomPerformanceSetting.GpuTgpWatts or
            CustomPerformanceSetting.GpuDynamicBoostEnabled or CustomPerformanceSetting.GpuDynamicBoostWatts)
            return await powerBudget.ApplyAsync(setting, value, cancellationToken, operationSelection).ConfigureAwait(false);
        var payload = await ReadOperationPayloadAsync(cancellationToken).ConfigureAwait(false);
        if (GcuCustomPerformanceStatusParser.ParseOperatingMode(payload) != 3)
            return ControlResult.Rejected("未确认当前为自定义模式；未写入高级参数。");
        if (ValidateSetting(payload, setting, value) is { } error)
            return ControlResult.Rejected(error);

        var enableTgp = setting == CustomPerformanceSetting.GpuTgpWatts &&
                        GcuCustomPerformanceStatusParser.ParseGpuConfigurableTgpEnabled(payload) == false;
        if (GcuCustomPerformanceStatusParser.ParseSetting(payload, setting) == value && !enableTgp)
            return new(true, "该参数已与 OEM 实时读数一致。");

        var powerLimitBefore = setting is CustomPerformanceSetting.GpuDynamicBoostEnabled or
            CustomPerformanceSetting.GpuDynamicBoostWatts && readEnforcedGpuPowerLimit is not null
            ? await readEnforcedGpuPowerLimit(cancellationToken).ConfigureAwait(false)
            : null;
        var previousValue = GcuCustomPerformanceStatusParser.ParseSetting(payload, setting);

        // A user edit owns only this field. In particular, changing TGP must never replay
        // OverClockingSwitch, clock offsets, or any CPU setting from an unrelated UI draft.
        if (enableTgp)
            await WriteOperationAsync(GcuCustomPerformanceCommand.GpuConfigurableTgpSwitch(true), cancellationToken).ConfigureAwait(false);
        await WriteOperationAsync(SettingCommand(setting, value), cancellationToken).ConfigureAwait(false);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var readback = await ReadOperationPayloadAsync(cancellationToken).ConfigureAwait(false);
            if (GcuCustomPerformanceStatusParser.ParseOperatingMode(readback) == 3 &&
                ValidateSetting(readback, setting, value) is null &&
                GcuCustomPerformanceStatusParser.ParseSetting(readback, setting) == value &&
                (setting != CustomPerformanceSetting.GpuTgpWatts ||
                 GcuCustomPerformanceStatusParser.ParseGpuConfigurableTgpEnabled(readback) == true))
            {
                if (setting is CustomPerformanceSetting.GpuDynamicBoostEnabled or CustomPerformanceSetting.GpuDynamicBoostWatts &&
                    powerLimitBefore is { } before && readEnforcedGpuPowerLimit is not null)
                    return await ConfirmDynamicBoostPowerLimitAsync(
                        setting, value, previousValue!.Value, before, readback, cancellationToken).ConfigureAwait(false);
                return new(true, "本次单项参数已发送并由 OEM 回读确认。" +
                    (setting is CustomPerformanceSetting.GpuDynamicBoostEnabled or CustomPerformanceSetting.GpuDynamicBoostWatts
                        ? "驱动功率上限不可用，实际效果未确认。" : string.Empty));
            }
            if (attempt < 2)
                await verificationDelay(TimeSpan.FromMilliseconds(180), cancellationToken).ConfigureAwait(false);
        }
        return ControlResult.Rejected("本次单项参数已发送，但 OEM 回读未匹配；未确认生效。");
    }

    private async ValueTask<ControlResult> ConfirmDynamicBoostPowerLimitAsync(
        CustomPerformanceSetting setting, int value, int previousValue, double before,
        string readback, CancellationToken cancellationToken)
    {
        bool ChangedAsRequested(double observed) => value > previousValue
            ? observed > before + 0.5
            : observed < before - 0.5;

        var observedDriver = false;
        foreach (var delay in new[] { 350, 700, 1000 })
        {
            await verificationDelay(TimeSpan.FromMilliseconds(delay), cancellationToken).ConfigureAwait(false);
            if (await readEnforcedGpuPowerLimit!(cancellationToken).ConfigureAwait(false) is { } observed)
            {
                observedDriver = true;
                if (ChangedAsRequested(observed))
                    return new(true, $"OEM 回读确认；NVIDIA 驱动功率上限已从 {before:0.#} W 变为 {observed:0.#} W。");
            }
        }
        if (!observedDriver)
            return new(true, "OEM 回读确认；NVIDIA 驱动功率上限暂不可读，实际效果未确认。");

        // This firmware acknowledges the ON switch before it reapplies the driver limit.
        // A value change in the same Dynamic Boost branch activates the pending switch.
        if (setting == CustomPerformanceSetting.GpuDynamicBoostEnabled && value == 1 &&
            GcuCustomPerformanceStatusParser.ParseGpuPowerLimits(readback) is { } limits &&
            GcuCustomPerformanceStatusParser.ParseSetting(readback, CustomPerformanceSetting.GpuDynamicBoostWatts) is { } watts)
        {
            var alternate = watts != limits.DynamicBoostMinimum
                ? limits.DynamicBoostMinimum
                : Math.Min(watts + 5, limits.DynamicBoostMaximum);
            if (alternate != watts)
            {
                try
                {
                    await WriteOperationAsync(GcuCustomPerformanceCommand.GpuDynamicBoost(alternate), cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    await WriteOperationAsync(GcuCustomPerformanceCommand.GpuDynamicBoost(watts), CancellationToken.None).ConfigureAwait(false);
                }
                foreach (var delay in new[] { 350, 700, 1000 })
                {
                    await verificationDelay(TimeSpan.FromMilliseconds(delay), cancellationToken).ConfigureAwait(false);
                    var finalStatus = await ReadOperationPayloadAsync(cancellationToken).ConfigureAwait(false);
                    if (GcuCustomPerformanceStatusParser.ParseSetting(finalStatus, setting) != 1 ||
                        GcuCustomPerformanceStatusParser.ParseSetting(finalStatus, CustomPerformanceSetting.GpuDynamicBoostWatts) != watts ||
                        GcuCustomPerformanceStatusParser.ParseSetting(finalStatus, CustomPerformanceSetting.GpuTgpWatts) !=
                        GcuCustomPerformanceStatusParser.ParseSetting(readback, CustomPerformanceSetting.GpuTgpWatts))
                        break;
                    if (await readEnforcedGpuPowerLimit!(cancellationToken).ConfigureAwait(false) is { } observed &&
                        ChangedAsRequested(observed))
                        return new(true, $"Dynamic Boost 已开启；驱动功率上限恢复至 {observed:0.#} W。");
                }
            }
        }

        return ControlResult.Rejected(setting == CustomPerformanceSetting.GpuDynamicBoostWatts
            ? "OEM 已保存 Dynamic Boost 数值，但 NVIDIA 驱动功率上限未变化；本机未确认该档位实际生效。"
            : "OEM 已切换 Dynamic Boost，但 NVIDIA 驱动功率上限未随之变化；未确认实际生效。");
    }

    private static string? ValidateSetting(string payload, CustomPerformanceSetting setting, int value)
    {
        if (!Enum.IsDefined(setting) || GcuCustomPerformanceStatusParser.ParseSetting(payload, setting) is null)
            return "OEM 未返回该参数的实时状态；未写入。";

        if (setting is CustomPerformanceSetting.GpuTgpWatts or CustomPerformanceSetting.GpuDynamicBoostEnabled or CustomPerformanceSetting.GpuDynamicBoostWatts)
        {
            if (GcuCustomPerformanceStatusParser.ParseSetting(payload, CustomPerformanceSetting.GpuOverclockingEnabled) != 1)
                return "请先开启 GPU 超性能开关，再调整 TGP / Dynamic Boost。";
            if (GcuCustomPerformanceStatusParser.ParseGpuPowerLimits(payload) is not { } limits)
                return "未读取到完整的 OEM GPU 功耗范围；未写入。";
            if (setting == CustomPerformanceSetting.GpuTgpWatts &&
                (value < limits.TgpMinimum || value > limits.TgpMaximum ||
                 GcuCustomPerformanceStatusParser.ParseGpuConfigurableTgpEnabled(payload) is null))
                return "TGP 超出当前 OEM 范围，或使能状态未知；未写入。";
            if (setting == CustomPerformanceSetting.GpuDynamicBoostWatts &&
                (value < limits.DynamicBoostMinimum || value > limits.DynamicBoostMaximum ||
                 GcuCustomPerformanceStatusParser.ParseSetting(payload, CustomPerformanceSetting.GpuDynamicBoostEnabled) != 1))
                return "请先开启 Dynamic Boost，并使用当前 OEM 支持的功耗范围。";
        }

        if (setting is CustomPerformanceSetting.GpuCoreOffsetMhz or CustomPerformanceSetting.GpuMemoryOffsetMhz)
        {
            var state = GcuCustomPerformanceStatusParser.ParseGpuClockOffsets(payload);
            if (state is not { IsCustomMode: true, OverclockingEnabled: true, Limits: { } limits })
                return "请先开启 GPU 超性能开关，并等待读取频率偏移范围。";
            var valid = setting == CustomPerformanceSetting.GpuCoreOffsetMhz
                ? value >= limits.CoreMinimum && value <= limits.CoreMaximum
                : value >= limits.MemoryMinimum && value <= limits.MemoryMaximum;
            if (!valid) return "GPU 频率偏移超出当前 OEM 可调范围；未写入。";
        }

        return setting switch
        {
            CustomPerformanceSetting.CpuTemperatureWallEnabled or CustomPerformanceSetting.CpuAdvancedMenuEnabled or
            CustomPerformanceSetting.GpuOverclockingEnabled or CustomPerformanceSetting.GpuDynamicBoostEnabled
                when value is not (0 or 1) => "开关值必须为 0 或 1；未写入。",
            CustomPerformanceSetting.CpuTemperatureWallC when value is < 70 or > 105 => "CPU 温度墙必须在 70–105°C 之间。",
            CustomPerformanceSetting.CpuBurstPowerLimitWatts when value is < 5 or > 400 => "CPU 瞬时功耗墙必须在 5–400 W 之间。",
            _ => null
        };
    }

    private static string SettingCommand(CustomPerformanceSetting setting, int value) => setting switch
    {
        CustomPerformanceSetting.CpuTemperatureWallEnabled => GcuCustomPerformanceCommand.CpuTemperatureWallSwitch(value == 1),
        CustomPerformanceSetting.CpuTemperatureWallC => GcuCustomPerformanceCommand.CpuTemperatureWall(value),
        CustomPerformanceSetting.CpuBurstPowerLimitWatts => GcuCustomPerformanceCommand.CpuBurstPowerLimit(value),
        CustomPerformanceSetting.CpuAdvancedMenuEnabled => GcuCustomPerformanceCommand.CpuAdvancedMenu(value == 1),
        CustomPerformanceSetting.GpuOverclockingEnabled => GcuCustomPerformanceCommand.OverclockingSwitch(value == 1),
        CustomPerformanceSetting.GpuTgpWatts => GcuCustomPerformanceCommand.GpuConfigurableTgp(value),
        CustomPerformanceSetting.GpuDynamicBoostEnabled => GcuCustomPerformanceCommand.GpuDynamicBoostSwitch(value == 1),
        CustomPerformanceSetting.GpuDynamicBoostWatts => GcuCustomPerformanceCommand.GpuDynamicBoost(value),
        CustomPerformanceSetting.GpuCoreOffsetMhz => GcuCustomPerformanceCommand.GpuCoreOffset(value),
        CustomPerformanceSetting.GpuMemoryOffsetMhz => GcuCustomPerformanceCommand.GpuMemoryOffset(value),
        _ => throw new ArgumentOutOfRangeException(nameof(setting))
    };
}
