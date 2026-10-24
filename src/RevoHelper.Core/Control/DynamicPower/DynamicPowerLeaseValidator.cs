namespace RevoHelper.Core.Control.DynamicPower;

public static class DynamicPowerLeaseValidator
{
    public static void Validate(DynamicPowerLease lease)
    {
        if (lease.SchemaVersion != 1) throw new InvalidDataException("未知的动态功耗恢复记录版本。");
        ValidateContext(lease.Context);
        ValidateValues(lease.Original);
        ValidateValues(lease.Expected);
        if (lease.Expected != Cap(lease.Original)) throw new InvalidDataException("动态功耗恢复记录的目标值无效。");
    }

    public static void ValidateContext(DynamicPowerContext context)
    {
        if (context is null || context.Scheme == Guid.Empty) throw new InvalidDataException("电源方案标识无效。");
    }

    public static void ValidateValues(DynamicFrequencyValues values)
    {
        if (values is null || Enumerable.Range(0, 3).Any(i => values[i] is not (0 or >= 100 and <= 64000)))
            throw new InvalidDataException("频率限制应为 0 或 100–64000 MHz。");
    }

    public static DynamicFrequencyValues Cap(DynamicFrequencyValues values) => new(Cap(values[0]), Cap(values[1]), Cap(values[2]));
    private static uint Cap(uint value) => value == 0 ? 3000 : Math.Min(value, 3000);
}
