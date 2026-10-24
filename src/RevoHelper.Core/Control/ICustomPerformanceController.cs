namespace RevoHelper.Core.Control;

public sealed record CustomPerformanceState(
    bool OverclockingEnabled,
    bool CpuTemperatureWallEnabled,
    int CpuTemperatureWallC,
    int CpuBurstPowerLimitWatts,
    bool CpuAdvancedMenuEnabled,
    int GpuConfigurableTgpWatts,
    bool GpuDynamicBoostEnabled,
    int GpuDynamicBoostWatts,
    int GpuCoreClockOffsetMhz,
    int GpuMemoryClockOffsetMhz);

public sealed record GpuPerformanceState(
    bool OverclockingEnabled,
    int GpuConfigurableTgpWatts,
    bool GpuDynamicBoostEnabled,
    int GpuDynamicBoostWatts,
    int GpuCoreClockOffsetMhz,
    int GpuMemoryClockOffsetMhz);

public sealed record GpuClockOffsets(int CoreMhz, int MemoryMhz);

public sealed record GpuClockOffsetLimits(int CoreMinimum, int CoreMaximum, int MemoryMinimum, int MemoryMaximum)
{
    public static GpuClockOffsetLimits InputBounds { get; } = new(-300, 400, -1000, 1500);
    public bool Contains(GpuClockOffsets offsets) =>
        offsets.CoreMhz >= CoreMinimum && offsets.CoreMhz <= CoreMaximum &&
        offsets.MemoryMhz >= MemoryMinimum && offsets.MemoryMhz <= MemoryMaximum;
}

public sealed record GpuClockOffsetState(bool IsCustomMode, bool OverclockingEnabled,
    GpuClockOffsets Offsets, GpuClockOffsetLimits? Limits, GpuPowerLimits? PowerLimits = null);

public sealed record GpuPowerLimits(int TgpMinimum, int TgpMaximum, int DynamicBoostMinimum, int DynamicBoostMaximum);

public sealed record CustomPerformanceSnapshot(CustomPerformanceState? State,
    GpuClockOffsetState? Gpu, string PowerBudgetNote = "");

public enum CustomPerformanceSetting
{
    CpuTemperatureWallEnabled,
    CpuTemperatureWallC,
    CpuBurstPowerLimitWatts,
    CpuAdvancedMenuEnabled,
    GpuOverclockingEnabled,
    GpuTgpWatts,
    GpuDynamicBoostEnabled,
    GpuDynamicBoostWatts,
    GpuCoreOffsetMhz,
    GpuMemoryOffsetMhz
}

public interface ICustomPerformanceController
{
    ValueTask<CustomPerformanceState?> ReadAsync(int profileIndex, CancellationToken cancellationToken);
    async ValueTask<CustomPerformanceSnapshot> ReadSnapshotAsync(int profileIndex, CancellationToken cancellationToken) =>
        new(await ReadAsync(profileIndex, cancellationToken), await ReadGpuClockOffsetsAsync(cancellationToken));
    ValueTask<ControlResult> ApplyAsync(
        int profileIndex,
        CustomPerformanceState state,
        CancellationToken cancellationToken);
    ValueTask<GpuPerformanceState?> ReadGpuAsync(CancellationToken cancellationToken);
    ValueTask<ControlResult> ApplyGpuAsync(GpuPerformanceState state, CancellationToken cancellationToken);
    ValueTask<GpuClockOffsetState?> ReadGpuClockOffsetsAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<GpuClockOffsetState?>(null);
    ValueTask<ControlResult> ApplyGpuClockOffsetsAsync(GpuClockOffsets offsets, CancellationToken cancellationToken) =>
        ValueTask.FromResult(ControlResult.Rejected("未检测到独立 GPU 频率偏移接口。"));
    ValueTask<ControlResult> ApplyGpuClockOffsetsAsync(int profileIndex, GpuClockOffsets offsets, CancellationToken cancellationToken) =>
        ApplyGpuClockOffsetsAsync(offsets, cancellationToken);
    ValueTask<ControlResult> ApplySettingAsync(CustomPerformanceSetting setting, int value, CancellationToken cancellationToken) =>
        ValueTask.FromResult(ControlResult.Rejected("未检测到高级参数单项写入接口。"));
    ValueTask<ControlResult> ApplySettingAsync(int profileIndex, CustomPerformanceSetting setting, int value, CancellationToken cancellationToken) =>
        ApplySettingAsync(setting, value, cancellationToken);
}
