namespace RevoHelper.Core.Control;

public sealed record ProcessorFrequencyLimitPolicy(
    uint AcMaximumMhz,
    uint DcMaximumMhz,
    uint AcClass1MaximumMhz,
    uint DcClass1MaximumMhz,
    uint AcClass2MaximumMhz,
    uint DcClass2MaximumMhz)
{
    public static ProcessorFrequencyLimitPolicy Uniform(uint maximumMhz) =>
        new(maximumMhz, maximumMhz, maximumMhz, maximumMhz, maximumMhz, maximumMhz);

    /// <summary>
    /// Windows applies the base setting to efficiency class 0 and the "Class 1" / "Class 2"
    /// settings to those classes. Classes from <paramref name="performanceClass"/> upward hold
    /// the P-cores; every lower class holds E-cores. With a single class (0) every setting
    /// gets the P-core value, which is the same as <see cref="Uniform"/>. A class above 2 has
    /// no Windows setting and is rejected rather than mapped onto another class.
    /// </summary>
    public static ProcessorFrequencyLimitPolicy ForCoreTypes(
        uint performanceCoresMhz,
        uint efficiencyCoresMhz,
        int performanceClass)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(performanceClass);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(performanceClass, ProcessorCoreClasses.HighestWindowsSettingClass);
        uint ForClass(int processorClass) =>
            processorClass >= performanceClass ? performanceCoresMhz : efficiencyCoresMhz;
        return new(ForClass(0), ForClass(0), ForClass(1), ForClass(1), ForClass(2), ForClass(2));
    }

    /// <summary>The AC value Windows applies to cores of the given efficiency class (0–2).</summary>
    public uint AcMaximumMhzForClass(int processorClass) => processorClass switch
    {
        0 => AcMaximumMhz,
        1 => AcClass1MaximumMhz,
        2 => AcClass2MaximumMhz,
        _ => throw new ArgumentOutOfRangeException(nameof(processorClass), processorClass, "Windows 只有 Class 0–2 的频率设置。")
    };

    /// <summary>True when every class has the same value on AC and on battery.</summary>
    /// <remarks>
    /// A method, not a property: this record is the schema 1 recovery lease, and older builds
    /// reject any JSON member besides the six values, so a property would make them treat a
    /// valid lease as corrupt.
    /// </remarks>
    public bool AcMatchesDc() =>
        AcMaximumMhz == DcMaximumMhz &&
        AcClass1MaximumMhz == DcClass1MaximumMhz &&
        AcClass2MaximumMhz == DcClass2MaximumMhz;
}

public interface IProcessorFrequencyLimitController
{
    ValueTask<ProcessorFrequencyLimitPolicy> ReadAsync(CancellationToken cancellationToken);

    ValueTask<ControlResult> ApplyAsync(
        ProcessorFrequencyLimitPolicy policy,
        CancellationToken cancellationToken);

    // Only a direct user action may replace an unreadable recovery baseline.
    // Ordinary profile synchronization and automatic recovery never call this.
    ValueTask<ControlResult> PrepareForExplicitDisableAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(new ControlResult(true, "频率限制可以关闭。"));
}

public sealed record ProcessorFrequencyLimitLease(
    int SchemaVersion,
    Guid Scheme,
    ProcessorFrequencyLimitPolicy Original,
    ProcessorFrequencyLimitPolicy Expected);

public interface IProcessorFrequencyLimitLeaseStore
{
    ValueTask<ProcessorFrequencyLimitLease?> LoadAsync(CancellationToken cancellationToken);

    ValueTask SaveAsync(
        ProcessorFrequencyLimitLease lease,
        CancellationToken cancellationToken);

    ValueTask ClearAsync(CancellationToken cancellationToken);

    // Preserve unreadable evidence before replacing it with an explicit reset intent.
    ValueTask ArchiveCorruptAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException("当前恢复记录存储不支持保存损坏记录副本。");
}
