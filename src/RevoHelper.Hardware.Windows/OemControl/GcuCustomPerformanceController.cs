using RevoHelper.Core.Control;
using RevoHelper.Hardware.Windows.OemProfiles;
using System.Text.Json;

namespace RevoHelper.Hardware.Windows.OemControl;

public sealed partial class GcuCustomPerformanceController(
    IGcuCustomPerformanceChannel channel,
    string profileDirectory = OemCpuPowerLimitProfileReader.DefaultProfileDirectory,
    Func<TimeSpan, CancellationToken, Task>? verificationDelay = null,
    Func<CancellationToken, ValueTask<double?>>? readEnforcedGpuPowerLimit = null,
    GpuPowerBudgetController? powerBudget = null,
    Func<CancellationToken, ValueTask<PlatformModeSelection?>>? readMode = null) : ICustomPerformanceController
{
    private readonly Func<TimeSpan, CancellationToken, Task> verificationDelay =
        verificationDelay ?? Task.Delay;

    public async ValueTask<CustomPerformanceState?> ReadAsync(int profileIndex, CancellationToken cancellationToken)
    {
        ValidateProfile(profileIndex);
        if (!await IsRequestedProfileActiveAsync(profileIndex, cancellationToken).ConfigureAwait(false))
            return null;
        try
        {
            var payload = await channel.ReadCustomPerformanceStatusAsync(cancellationToken).ConfigureAwait(false);
            if (!await IsRequestedProfileActiveAsync(profileIndex, cancellationToken).ConfigureAwait(false)) return null;
            var live = GcuCustomPerformanceStatusParser.Parse(payload);
            if (live is not null) return live;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The OEM profile is the durable fallback when the live status omits advanced fields.
        }

        var path = Path.Combine(profileDirectory, $"Mode4_Profile{profileIndex}.json");
        try
        {
            var stored = File.Exists(path) ? GcuCustomPerformanceStatusParser.Parse(await File.ReadAllTextAsync(path, cancellationToken)) : null;
            return await IsRequestedProfileActiveAsync(profileIndex, cancellationToken).ConfigureAwait(false) ? stored : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public async ValueTask<CustomPerformanceSnapshot> ReadSnapshotAsync(int profileIndex, CancellationToken token)
    {
        ValidateProfile(profileIndex);
        if (!await IsRequestedProfileActiveAsync(profileIndex, token).ConfigureAwait(false))
            return new(null, null);
        var payload = await channel.ReadCustomPerformanceStatusAsync(token).ConfigureAwait(false);
        if (!await IsRequestedProfileActiveAsync(profileIndex, token).ConfigureAwait(false)) return new(null, null);
        var live = GcuCustomPerformanceStatusParser.Parse(payload);
        var gpu = GcuCustomPerformanceStatusParser.ParseGpuClockOffsets(payload);
        var state = live ?? await ReadAsync(profileIndex, token).ConfigureAwait(false);
        if (!await IsRequestedProfileActiveAsync(profileIndex, token).ConfigureAwait(false)) return new(null, null);
        return new(state, gpu);
    }

    public ValueTask<ControlResult> ApplyAsync(int profileIndex, CustomPerformanceState state, CancellationToken cancellationToken) =>
        ExecuteForSelectionAsync(profileIndex, true, () => ApplyCoreAsync(profileIndex, state, cancellationToken), cancellationToken);

    private async ValueTask<ControlResult> ApplyCoreAsync(
        int profileIndex,
        CustomPerformanceState state,
        CancellationToken cancellationToken)
    {
        ValidateProfile(profileIndex);
        if (powerBudget is not null && !await powerBudget.IsActiveProfileAsync(profileIndex, cancellationToken).ConfigureAwait(false))
            return ControlResult.Rejected("当前自定义方案已变化；未写入草稿。");
        if (state.CpuTemperatureWallC is < 70 or > 105)
            return ControlResult.Rejected("CPU 温度墙必须在 70–105°C 之间。");
        if (state.CpuBurstPowerLimitWatts is < 5 or > 400)
            return ControlResult.Rejected("CPU 瞬时功耗墙必须在 5–400 W 之间。");
        if (state.GpuConfigurableTgpWatts is < 25 or > 250)
            return ControlResult.Rejected("GPU TGP 必须在 25–250 W 之间。");
        if (state.GpuDynamicBoostWatts is < 0 or > 50)
            return ControlResult.Rejected("Dynamic Boost 必须在 0–50 W 之间。");
        if (!GpuClockOffsetLimits.InputBounds.Contains(new(state.GpuCoreClockOffsetMhz, state.GpuMemoryClockOffsetMhz)))
            return ControlResult.Rejected("GPU 频率偏移超出安全输入范围。");

        var commands = new[]
        {
            GcuCustomPerformanceCommand.CpuTemperatureWallSwitch(state.CpuTemperatureWallEnabled),
            GcuCustomPerformanceCommand.CpuTemperatureWall(state.CpuTemperatureWallC),
            GcuCustomPerformanceCommand.CpuBurstPowerLimit(state.CpuBurstPowerLimitWatts),
            GcuCustomPerformanceCommand.CpuAdvancedMenu(state.CpuAdvancedMenuEnabled),
            GcuCustomPerformanceCommand.OverclockingSwitch(state.OverclockingEnabled),
            GcuCustomPerformanceCommand.GpuConfigurableTgpSwitch(true),
            GcuCustomPerformanceCommand.GpuConfigurableTgp(state.GpuConfigurableTgpWatts),
            GcuCustomPerformanceCommand.GpuDynamicBoostSwitch(state.GpuDynamicBoostEnabled),
            GcuCustomPerformanceCommand.GpuDynamicBoost(state.GpuDynamicBoostWatts),
            GcuCustomPerformanceCommand.GpuCoreOffset(state.GpuCoreClockOffsetMhz),
            GcuCustomPerformanceCommand.GpuMemoryOffset(state.GpuMemoryClockOffsetMhz)
        };
        foreach (var command in commands)
        {
            if (powerBudget is not null && IsBudgetCommand(command)) continue;
            await WriteOperationAsync(command, cancellationToken).ConfigureAwait(false);
        }
        if (powerBudget is not null && state.OverclockingEnabled)
        {
            var budget = await powerBudget.ApplyBudgetAsync(new(state.OverclockingEnabled, state.GpuConfigurableTgpWatts,
                state.GpuDynamicBoostEnabled, state.GpuDynamicBoostWatts, state.GpuCoreClockOffsetMhz, state.GpuMemoryClockOffsetMhz),
                cancellationToken, operationSelection).ConfigureAwait(false);
            if (!budget.Succeeded) return budget;
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var readbackPayload = await ReadOperationPayloadAsync(cancellationToken).ConfigureAwait(false);
            var readback = GcuCustomPerformanceStatusParser.Parse(readbackPayload);
            var tgpEnabled = GcuCustomPerformanceStatusParser.ParseGpuConfigurableTgpEnabled(readbackPayload);
            if (readback is not null &&
                (readback == state || (powerBudget is not null && !state.GpuDynamicBoostEnabled &&
                    (readback with { GpuDynamicBoostWatts = state.GpuDynamicBoostWatts }) == state)) && tgpEnabled == true)
                return new(true, "自定义 CPU / GPU 参数已写入并从 OEM 状态回读确认。");
            if (attempt < 2)
                await verificationDelay(TimeSpan.FromMilliseconds(180), cancellationToken).ConfigureAwait(false);
        }
        return ControlResult.Rejected(
            "自定义 CPU / GPU 参数已发送，但三次 OEM 回读未匹配（含 Configurable TGP 使能），未确认生效。");
    }

    public async ValueTask<GpuPerformanceState?> ReadGpuAsync(CancellationToken cancellationToken)
    {
        try
        {
            var payload = await channel.ReadCustomPerformanceStatusAsync(cancellationToken).ConfigureAwait(false);
            return GcuCustomPerformanceStatusParser.ParseGpu(payload);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }
    }

    public ValueTask<ControlResult> ApplyGpuAsync(GpuPerformanceState state, CancellationToken cancellationToken) =>
        ExecuteForSelectionAsync(null, false, () => ApplyGpuCoreAsync(state, cancellationToken), cancellationToken);

    private async ValueTask<ControlResult> ApplyGpuCoreAsync(
        GpuPerformanceState state,
        CancellationToken cancellationToken)
    {
        if (state.GpuConfigurableTgpWatts is < 25 or > 250)
            return ControlResult.Rejected("GPU TGP 必须在 25–250 W 之间。");
        if (state.GpuDynamicBoostWatts is < 0 or > 50)
            return ControlResult.Rejected("Dynamic Boost 必须在 0–50 W 之间。");
        if (!GpuClockOffsetLimits.InputBounds.Contains(new(state.GpuCoreClockOffsetMhz, state.GpuMemoryClockOffsetMhz)))
            return ControlResult.Rejected("GPU 频率偏移超出安全输入范围。");

        var commands = new[]
        {
            GcuCustomPerformanceCommand.OverclockingSwitch(state.OverclockingEnabled),
            GcuCustomPerformanceCommand.GpuConfigurableTgpSwitch(true),
            GcuCustomPerformanceCommand.GpuConfigurableTgp(state.GpuConfigurableTgpWatts),
            GcuCustomPerformanceCommand.GpuDynamicBoostSwitch(state.GpuDynamicBoostEnabled),
            GcuCustomPerformanceCommand.GpuDynamicBoost(state.GpuDynamicBoostWatts),
            GcuCustomPerformanceCommand.GpuCoreOffset(state.GpuCoreClockOffsetMhz),
            GcuCustomPerformanceCommand.GpuMemoryOffset(state.GpuMemoryClockOffsetMhz)
        };
        foreach (var command in commands)
        {
            if (powerBudget is not null && IsBudgetCommand(command)) continue;
            await WriteOperationAsync(command, cancellationToken).ConfigureAwait(false);
        }
        if (powerBudget is not null && state.OverclockingEnabled)
        {
            var budget = await powerBudget.ApplyBudgetAsync(state, cancellationToken, operationSelection).ConfigureAwait(false);
            if (!budget.Succeeded) return budget;
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var readbackPayload = await ReadOperationPayloadAsync(cancellationToken).ConfigureAwait(false);
            var readback = GcuCustomPerformanceStatusParser.ParseGpu(readbackPayload);
            var tgpEnabled = GcuCustomPerformanceStatusParser.ParseGpuConfigurableTgpEnabled(readbackPayload);
            if (readback is not null &&
                (readback == state || (powerBudget is not null && !state.GpuDynamicBoostEnabled &&
                    (readback with { GpuDynamicBoostWatts = state.GpuDynamicBoostWatts }) == state)) && tgpEnabled == true)
                return new(true, "当前 OEM 模式的 GPU 参数已写入并回读确认。");
            if (attempt < 2)
                await verificationDelay(TimeSpan.FromMilliseconds(180), cancellationToken).ConfigureAwait(false);
        }
        return ControlResult.Rejected(
            "GPU 参数已发送，但三次 OEM 回读未匹配（含 Configurable TGP 使能），未确认生效；该预设可能仅允许自定义模式调整。");
    }

    public async ValueTask<GpuClockOffsetState?> ReadGpuClockOffsetsAsync(CancellationToken cancellationToken)
    {
        var payload = await channel.ReadCustomPerformanceStatusAsync(cancellationToken).ConfigureAwait(false);
        return GcuCustomPerformanceStatusParser.ParseGpuClockOffsets(payload);
    }

    public ValueTask<ControlResult> ApplyGpuClockOffsetsAsync(GpuClockOffsets offsets, CancellationToken cancellationToken) =>
        ExecuteForSelectionAsync(null, true, () => ApplyGpuClockOffsetsCoreAsync(offsets, cancellationToken), cancellationToken);

    public ValueTask<ControlResult> ApplyGpuClockOffsetsAsync(int profileIndex, GpuClockOffsets offsets, CancellationToken cancellationToken) =>
        ExecuteForSelectionAsync(profileIndex, true, () => ApplyGpuClockOffsetsCoreAsync(offsets, cancellationToken), cancellationToken);

    private async ValueTask<ControlResult> ApplyGpuClockOffsetsCoreAsync(
        GpuClockOffsets offsets, CancellationToken cancellationToken)
    {
        var current = await ReadGpuClockOffsetsAsync(cancellationToken).ConfigureAwait(false);
        await EnsureOperationSelectionAsync(cancellationToken).ConfigureAwait(false);
        if (current is not { IsCustomMode: true, OverclockingEnabled: true })
            return ControlResult.Rejected("仅在自定义模式且 GPU 超性能开关开启时允许调整频率偏移。");
        if (current.Limits is not { } limits || !limits.Contains(offsets))
            return ControlResult.Rejected("GPU 频率偏移超出当前 OEM 可调范围，或未读取到范围；未写入。");
        if (current.Offsets == offsets) return new(true, "GPU 频率偏移已与 OEM 实时读数一致。");

        // Match the official frontend: an offset edit must not resend OC, TGP or CPU switches.
        if (current.Offsets.CoreMhz != offsets.CoreMhz)
            await WriteOperationAsync(GcuCustomPerformanceCommand.GpuCoreOffset(offsets.CoreMhz), cancellationToken).ConfigureAwait(false);
        if (current.Offsets.MemoryMhz != offsets.MemoryMhz)
            await WriteOperationAsync(GcuCustomPerformanceCommand.GpuMemoryOffset(offsets.MemoryMhz), cancellationToken).ConfigureAwait(false);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var readback = await ReadGpuClockOffsetsAsync(cancellationToken).ConfigureAwait(false);
            await EnsureOperationSelectionAsync(cancellationToken).ConfigureAwait(false);
            if (readback is { IsCustomMode: true, OverclockingEnabled: true } && readback.Offsets == offsets)
                return new(true, $"GPU 频率偏移已回读确认：核心 {offsets.CoreMhz} MHz，显存 {offsets.MemoryMhz} MHz。");
            if (attempt < 2)
                await verificationDelay(TimeSpan.FromMilliseconds(180), cancellationToken).ConfigureAwait(false);
        }
        return ControlResult.Rejected("GPU 频率偏移已发送，但 OEM 回读未匹配；未确认生效。");
    }

    private static void ValidateProfile(int profileIndex)
    {
        if (profileIndex is < 1 or > 5) throw new ArgumentOutOfRangeException(nameof(profileIndex));
    }

    private static bool IsBudgetCommand(string command) =>
        command.Contains("GpuConfigurableTGP", StringComparison.Ordinal) || command.Contains("GpuDynamicBoost", StringComparison.Ordinal);
}
