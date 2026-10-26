using RevoHelper.Core.Control;

namespace RevoHelper.Hardware.Windows.OemControl;

/// <summary>Confirms OEM GPU fields independently from the driver's observed power ceiling.</summary>
public sealed class GpuPowerBudgetController(IGcuCustomPerformanceChannel channel,
    Func<CancellationToken, ValueTask<PlatformModeSelection?>> readMode,
    Func<CancellationToken, ValueTask<double?>> readCeiling,
    IGpuPowerBudgetStore store,
    Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Func<TimeSpan, CancellationToken, Task> wait = delay ?? Task.Delay;

    // Old releases compensated one setting by silently changing another. Keep the API/store
    // constructor compatible, but never use those records to remap a live setting or write baseline.
    public ValueTask<GpuPowerBudgetRecord?> ReadRecordAsync(string payload, int? profile, CancellationToken token)
    {
        _ = store;
        return ValueTask.FromResult<GpuPowerBudgetRecord?>(null);
    }

    public async ValueTask<ControlResult> ApplyAsync(CustomPerformanceSetting setting, int value, CancellationToken token,
        PlatformModeSelection? expectedSelection = null)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try { return await ApplyCoreAsync(setting, value, token, expectedSelection: expectedSelection).ConfigureAwait(false); }
        catch (Exception failure) when (failure is not OperationCanceledException)
        { return ControlResult.Rejected($"GPU 实时状态未获确认；未继续写入：{failure.Message}"); }
        finally { gate.Release(); }
    }

    public async ValueTask<ControlResult> ApplyBudgetAsync(GpuPerformanceState desired, CancellationToken token,
        PlatformModeSelection? expectedSelection = null)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try { return await ApplyCoreAsync(CustomPerformanceSetting.GpuTgpWatts, desired.GpuConfigurableTgpWatts,
            token, desired, expectedSelection).ConfigureAwait(false); }
        catch (Exception failure) when (failure is not OperationCanceledException)
        { return ControlResult.Rejected($"GPU 实时状态未获确认；未继续写入：{failure.Message}"); }
        finally { gate.Release(); }
    }

    private async ValueTask<ControlResult> ApplyCoreAsync(CustomPerformanceSetting setting, int value, CancellationToken token,
        GpuPerformanceState? desired = null, PlatformModeSelection? expectedSelection = null)
    {
        var selection = await readMode(token).ConfigureAwait(false);
        if (expectedSelection is not null && selection != expectedSelection)
            return ControlResult.Rejected("自定义方案已切换；未写入原方案参数。");
        if (selection is not { Mode: PlatformMode.Custom } || selection.ProfileIndex is < 1 or > 5)
            return ControlResult.Rejected("请在自定义模式调整 GPU 参数。");

        var originalPayload = await ReadForSelectionAsync(selection, token).ConfigureAwait(false);
        var original = GcuCustomPerformanceStatusParser.ParseGpu(originalPayload);
        if (original is not { OverclockingEnabled: true })
            return ControlResult.Rejected("请在自定义模式开启 GPU 超性能，并等待读取实时状态。");

        var requests = new List<FieldRequest>();
        if (desired is not null)
        {
            if (ValidateTgp(originalPayload, desired.GpuConfigurableTgpWatts) is { } tgpError)
                return ControlResult.Rejected(tgpError);
            if (desired.GpuDynamicBoostEnabled &&
                ValidateBoostWatts(originalPayload, desired.GpuDynamicBoostWatts, requireCurrentlyEnabled: false) is { } boostError)
                return ControlResult.Rejected(boostError);
            requests.Add(new(null, 1));
            requests.Add(new(CustomPerformanceSetting.GpuTgpWatts, desired.GpuConfigurableTgpWatts));
            requests.Add(new(CustomPerformanceSetting.GpuDynamicBoostEnabled, desired.GpuDynamicBoostEnabled ? 1 : 0));
            // OFF owns only the switch. Its saved ON tier is not a zero-watt command.
            if (desired.GpuDynamicBoostEnabled)
                requests.Add(new(CustomPerformanceSetting.GpuDynamicBoostWatts, desired.GpuDynamicBoostWatts));
        }
        else
        {
            switch (setting)
            {
                case CustomPerformanceSetting.GpuDynamicBoostEnabled when value is 0 or 1:
                    requests.Add(new(setting, value));
                    break;
                case CustomPerformanceSetting.GpuTgpWatts:
                    if (ValidateTgp(originalPayload, value) is { } tgpError)
                        return ControlResult.Rejected(tgpError);
                    requests.Add(new(null, 1));
                    requests.Add(new(setting, value));
                    break;
                case CustomPerformanceSetting.GpuDynamicBoostWatts:
                    if (ValidateBoostWatts(originalPayload, value, requireCurrentlyEnabled: true) is { } boostError)
                        return ControlResult.Rejected(boostError);
                    requests.Add(new(setting, value));
                    break;
                default:
                    return ControlResult.Rejected("GPU 参数或开关值无效；未写入。");
            }
        }
        foreach (var request in requests)
            if (ReadField(originalPayload, request.Setting) is null)
                return ControlResult.Rejected("OEM 未返回本次参数的实时状态；未写入。");

        var mutations = new List<FieldMutation>();
        try
        {
            foreach (var request in requests)
                await WriteFieldAsync(selection, request.Setting, ReadField(originalPayload, request.Setting)!.Value,
                    request.Value, mutations, token).ConfigureAwait(false);

            // Only fields owned by this edit are confirmed; unrelated CPU, clocks and power
            // fields are never replayed from an old UI draft or a legacy compensation record.
            var confirmedPayload = mutations.Count == 0
                ? originalPayload : await ReadForSelectionAsync(selection, token).ConfigureAwait(false);
            foreach (var request in requests)
                if (ReadField(confirmedPayload, request.Setting) != request.Value)
                    throw new IOException(DescribeMismatch(request.Setting, request.Value,
                        ReadField(confirmedPayload, request.Setting)));
            await EnsureSelectionAsync(selection, token).ConfigureAwait(false);
            var confirmed = GcuCustomPerformanceStatusParser.ParseGpu(confirmedPayload)!;
            var driver = await ObserveDriverAsync(token).ConfigureAwait(false);
            return new(true, DescribeConfirmed(setting, desired, confirmed) + driver);
        }
        catch (Exception failure)
        {
            var driver = await IsSelectionAsync(selection, CancellationToken.None).ConfigureAwait(false)
                ? await ObserveDriverAsync(CancellationToken.None).ConfigureAwait(false) : string.Empty;
            var restoration = await RestoreOwnedFieldsAsync(selection, mutations).ConfigureAwait(false);
            if (failure is OperationCanceledException) throw;
            return ControlResult.Rejected($"GPU 参数未完成：{failure.Message}" + driver + restoration);
        }
    }

    private static string? ValidateTgp(string payload, int value)
    {
        var limits = GcuCustomPerformanceStatusParser.ParseGpuPowerLimits(payload);
        if (limits is null || GcuCustomPerformanceStatusParser.ParseGpuConfigurableTgpEnabled(payload) is null)
            return "未读取到完整的 OEM TGP 范围或使能状态；未写入。";
        return value < GpuPowerBudgetRange.MinimumTarget(limits) || value > limits.TgpMaximum
            ? "TGP 设置超出当前机型可尝试范围；未写入。" : null;
    }

    private static string? ValidateBoostWatts(string payload, int value, bool requireCurrentlyEnabled)
    {
        if (requireCurrentlyEnabled &&
            GcuCustomPerformanceStatusParser.ParseSetting(payload, CustomPerformanceSetting.GpuDynamicBoostEnabled) != 1)
            return "请先开启 Dynamic Boost 再调整其档位；未写入。";
        var limits = GcuCustomPerformanceStatusParser.ParseGpuPowerLimits(payload);
        return limits is null || value < limits.DynamicBoostMinimum || value > limits.DynamicBoostMaximum
            ? "Dynamic Boost 档位超出当前 OEM 范围，或范围不可读；未写入。" : null;
    }

    private async Task WriteFieldAsync(PlatformModeSelection selection, CustomPerformanceSetting? setting,
        int before, int value, List<FieldMutation> mutations, CancellationToken token)
    {
        var live = await ReadForSelectionAsync(selection, token).ConfigureAwait(false);
        var current = ReadField(live, setting);
        if (current == value) return;
        if (current != before)
            throw new IOException("本次参数在写入前已被其它操作修改，未覆盖新的设置。");
        var mutation = new FieldMutation(setting, before, value);
        mutations.Add(mutation);
        await EnsureSelectionAsync(selection, token).ConfigureAwait(false);
        await channel.WriteCustomPerformanceAsync(Command(setting, value), token).ConfigureAwait(false);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var payload = await ReadForSelectionAsync(selection, token).ConfigureAwait(false);
            mutation.LastObserved = ReadField(payload, setting);
            if (mutation.LastObserved == value) return;
            if (attempt < 2) await wait(TimeSpan.FromMilliseconds(100), token).ConfigureAwait(false);
        }
        throw new IOException(DescribeMismatch(setting, value, mutation.LastObserved));
    }

    private async Task<string> RestoreOwnedFieldsAsync(PlatformModeSelection selection, List<FieldMutation> mutations)
    {
        if (mutations.Count == 0) return "原设置未改变。";
        const string notRestored = "未确认恢复原值；已保留后续参数变化或停止向其它方案写入，请重新读取状态。";
        // Preflight the entire owned set before restoring any dependency. For example, an
        // external TGP edit also owns its enabled state; restoring only our old OFF switch
        // would otherwise invalidate the newer target even if its watts were left alone.
        // A mismatched readback may be another writer, not firmware clamping our request.
        // LastObserved is diagnostic only and must never grant restoration ownership.
        foreach (var mutation in mutations)
        {
            try
            {
                var payload = await ReadForSelectionAsync(selection, CancellationToken.None).ConfigureAwait(false);
                var current = ReadField(payload, mutation.Setting);
                if (current is null ||
                    (current != mutation.Before && current != mutation.Requested))
                    return notRestored;
            }
            catch (Exception) { return notRestored; }
        }
        var wroteRestore = false;
        foreach (var mutation in mutations.AsEnumerable().Reverse())
        {
            try
            {
                var payload = await ReadForSelectionAsync(selection, CancellationToken.None).ConfigureAwait(false);
                var current = ReadField(payload, mutation.Setting);
                if (current == mutation.Before) continue;
                // A later external edit must never be overwritten by restoration. If a
                // publish failed before readback, only our exact requested value is owned.
                if (current is null || current != mutation.Requested)
                    return notRestored;
                await WriteFieldAsync(selection, mutation.Setting, current.Value, mutation.Before, [],
                    CancellationToken.None).ConfigureAwait(false);
                wroteRestore = true;
            }
            catch (Exception) { return notRestored; }
        }
        return wroteRestore ? "已恢复操作前本次修改的 OEM 设置。" : "原有参数未改变。";
    }

    private async ValueTask<string> ObserveDriverAsync(CancellationToken token)
    {
        try
        {
            var observed = await readCeiling(token).ConfigureAwait(false);
            return observed is { } watts && double.IsFinite(watts) && watts > 0
                ? $"驱动当前功率上限：{watts:0.#} W（独立读数，不代表实时功耗）。"
                : "驱动当前功率上限不可读，实际效果未确认。";
        }
        catch (Exception)
        {
            return "驱动当前功率上限不可读，实际效果未确认。";
        }
    }

    private static string DescribeConfirmed(CustomPerformanceSetting setting, GpuPerformanceState? desired,
        GpuPerformanceState actual) => desired is not null
        ? $"GPU 功耗字段已由 OEM 回读确认：TGP {actual.GpuConfigurableTgpWatts} W，Dynamic Boost " +
            $"{(actual.GpuDynamicBoostEnabled ? "开启" : "关闭")}，保存档位 {actual.GpuDynamicBoostWatts} W。"
        : setting switch
        {
            CustomPerformanceSetting.GpuDynamicBoostEnabled =>
                $"Dynamic Boost 已{(actual.GpuDynamicBoostEnabled ? "开启" : "关闭")}（OEM 回读确认）；" +
                $"TGP 设置 {actual.GpuConfigurableTgpWatts} W，保存档位 {actual.GpuDynamicBoostWatts} W。",
            CustomPerformanceSetting.GpuDynamicBoostWatts =>
                $"Dynamic Boost 档位 {actual.GpuDynamicBoostWatts} W 已由 OEM 回读确认。",
            _ => $"TGP 设置 {actual.GpuConfigurableTgpWatts} W 已由 OEM 回读确认。"
        };

    private static string DescribeMismatch(CustomPerformanceSetting? setting, int requested, int? observed)
    {
        var unit = setting is CustomPerformanceSetting.GpuTgpWatts or CustomPerformanceSetting.GpuDynamicBoostWatts ? " W" : string.Empty;
        var name = setting switch
        {
            CustomPerformanceSetting.GpuTgpWatts => "TGP",
            CustomPerformanceSetting.GpuDynamicBoostEnabled => "Dynamic Boost 开关",
            CustomPerformanceSetting.GpuDynamicBoostWatts => "Dynamic Boost 档位",
            _ => "Configurable TGP 使能"
        };
        return $"请求 {name} {requested}{unit} 未获确认；OEM 实际回读 {name} " +
            (observed is { } value ? $"{value}{unit}。" : "不可读。");
    }

    private static int? ReadField(string payload, CustomPerformanceSetting? setting) => setting is { } field
        ? GcuCustomPerformanceStatusParser.ParseSetting(payload, field)
        : GcuCustomPerformanceStatusParser.ParseGpuConfigurableTgpEnabled(payload) is { } enabled ? enabled ? 1 : 0 : null;

    private static string Command(CustomPerformanceSetting? setting, int value) => setting switch
    {
        CustomPerformanceSetting.GpuTgpWatts => GcuCustomPerformanceCommand.GpuConfigurableTgp(value),
        CustomPerformanceSetting.GpuDynamicBoostEnabled => GcuCustomPerformanceCommand.GpuDynamicBoostSwitch(value == 1),
        CustomPerformanceSetting.GpuDynamicBoostWatts => GcuCustomPerformanceCommand.GpuDynamicBoost(value),
        null => GcuCustomPerformanceCommand.GpuConfigurableTgpSwitch(value == 1),
        _ => throw new ArgumentOutOfRangeException(nameof(setting))
    };

    private async ValueTask<string> ReadForSelectionAsync(PlatformModeSelection selection, CancellationToken token)
    {
        await EnsureSelectionAsync(selection, token).ConfigureAwait(false);
        var payload = await channel.ReadCustomPerformanceStatusAsync(token).ConfigureAwait(false);
        await EnsureSelectionAsync(selection, token).ConfigureAwait(false);
        if (GcuCustomPerformanceStatusParser.ParseOperatingMode(payload) != 3)
            throw new IOException("当前性能档位已变化，已停止写入。");
        return payload;
    }

    public async ValueTask<bool> IsActiveProfileAsync(int profile, CancellationToken token) =>
        await readMode(token).ConfigureAwait(false) == new PlatformModeSelection(PlatformMode.Custom, profile);

    public ValueTask<PlatformModeSelection?> ReadSelectionAsync(CancellationToken token) => readMode(token);

    private async ValueTask<bool> IsSelectionAsync(PlatformModeSelection expected, CancellationToken token)
    {
        try { return await readMode(token).ConfigureAwait(false) == expected; }
        catch (Exception) { return false; }
    }

    private async Task EnsureSelectionAsync(PlatformModeSelection expected, CancellationToken token)
    {
        if (await readMode(token).ConfigureAwait(false) != expected)
            throw new IOException("自定义方案已切换，已停止写入原方案参数。");
    }

    private sealed record FieldRequest(CustomPerformanceSetting? Setting, int Value);
    private sealed class FieldMutation(CustomPerformanceSetting? setting, int before, int requested)
    {
        public CustomPerformanceSetting? Setting { get; } = setting;
        public int Before { get; } = before;
        public int Requested { get; } = requested;
        public int? LastObserved { get; set; }
    }
}
