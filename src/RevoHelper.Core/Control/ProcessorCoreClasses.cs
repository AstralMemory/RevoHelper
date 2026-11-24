namespace RevoHelper.Core.Control;

/// <summary>
/// The physical cores grouped by the efficiency class Windows reports for each of them.
/// A higher class is a faster, less efficient core, so the highest class present holds the
/// P-cores and every lower class holds E-cores. Read from Windows, never inferred from the
/// processor name or vendor.
/// </summary>
/// <param name="EfficiencyClassMask">
/// Bit n is set when class n holds E-cores. Class 0 alone by default, as on processors with
/// one kind of E-core.
/// </param>
public sealed record ProcessorCoreClasses(
    int PerformanceClass,
    int PerformanceCores,
    int EfficiencyCores,
    int EfficiencyClassMask = 0b1)
{
    /// <summary>Windows has a maximum-frequency setting for classes 0, 1 and 2 only.</summary>
    public const int HighestWindowsSettingClass = 2;

    /// <summary>True when Windows reports more than one class of core.</summary>
    public bool IsHybrid => PerformanceCores > 0 && EfficiencyCores > 0;

    /// <summary>
    /// True when every class maps onto a Windows setting, so P- and E-cores can be capped
    /// separately. A class above 2 has no setting: then one cap must cover every core.
    /// </summary>
    public bool CanLimitSeparately =>
        IsHybrid &&
        PerformanceClass is >= 1 and <= HighestWindowsSettingClass &&
        EfficiencyClassMask != 0 &&
        EfficiencyClassMask >> PerformanceClass == 0;

    /// <summary>The classes that hold E-cores, lowest first.</summary>
    public IEnumerable<int> EfficiencyClasses =>
        Enumerable.Range(0, 31).Where(processorClass => (EfficiencyClassMask >> processorClass & 1) == 1);

    /// <summary>Groups one efficiency class per physical core; null when no core was reported.</summary>
    public static ProcessorCoreClasses? FromCoreEfficiencyClasses(IEnumerable<int> efficiencyClassPerCore)
    {
        var classes = efficiencyClassPerCore.ToArray();
        if (classes.Length == 0)
        {
            return null;
        }

        var performanceClass = classes.Max();
        var performanceCores = classes.Count(value => value == performanceClass);
        var efficiencyClassMask = classes
            .Where(value => value < performanceClass && value < 31)
            .Aggregate(0, (mask, value) => mask | 1 << value);
        return new(performanceClass, performanceCores, classes.Length - performanceCores, efficiencyClassMask);
    }
}
