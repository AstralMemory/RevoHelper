namespace RevoHelper.Hardware.Windows.CpuPower;

public sealed record IntelRaplPowerLimits(int Pl1Watts, int Pl2Watts, bool Locked);

public static class IntelRaplPowerLimitCodec
{
    private const ulong LimitMask = 0x7FFFUL;
    private const int Pl1EnableBit = 15;
    private const int Pl2Offset = 32;
    private const int Pl2EnableBit = 47;
    private const int LockBit = 63;
    private const ulong ModifiedBitsMask =
        LimitMask |
        (1UL << Pl1EnableBit) |
        (LimitMask << Pl2Offset) |
        (1UL << Pl2EnableBit);

    public const ulong PreservedBitsMask = ~ModifiedBitsMask;

    public static IntelRaplPowerLimits Decode(ulong powerUnitMsr, ulong packagePowerLimitMsr)
    {
        var unit = PowerUnit(powerUnitMsr);
        return new(
            ToWatts(packagePowerLimitMsr & LimitMask, unit),
            ToWatts((packagePowerLimitMsr >> Pl2Offset) & LimitMask, unit),
            (packagePowerLimitMsr & (1UL << LockBit)) != 0);
    }

    public static ulong Encode(
        ulong powerUnitMsr,
        ulong originalPackagePowerLimitMsr,
        int pl1Watts,
        int pl2Watts)
    {
        if ((originalPackagePowerLimitMsr & (1UL << LockBit)) != 0)
        {
            throw new InvalidOperationException("Intel RAPL 包功耗寄存器已被固件锁定。");
        }

        if (pl1Watts <= 0 || pl2Watts <= 0 || pl1Watts > pl2Watts)
        {
            throw new ArgumentOutOfRangeException(nameof(pl1Watts), "PL1/PL2 必须为正数且 PL1 不高于 PL2。");
        }

        var unit = PowerUnit(powerUnitMsr);
        var pl1 = ToEncoded(pl1Watts, unit);
        var pl2 = ToEncoded(pl2Watts, unit);
        return (originalPackagePowerLimitMsr & PreservedBitsMask) |
               pl1 |
               (1UL << Pl1EnableBit) |
               (pl2 << Pl2Offset) |
               (1UL << Pl2EnableBit);
    }

    private static double PowerUnit(ulong powerUnitMsr)
    {
        var exponent = (int)(powerUnitMsr & 0xFUL);
        return 1d / (1UL << exponent);
    }

    private static int ToWatts(ulong encoded, double unit) =>
        (int)Math.Round(encoded * unit, MidpointRounding.AwayFromZero);

    private static ulong ToEncoded(int watts, double unit)
    {
        var encoded = checked((ulong)Math.Round(watts / unit, MidpointRounding.AwayFromZero));
        if (encoded > LimitMask)
        {
            throw new ArgumentOutOfRangeException(nameof(watts), "目标功耗超出当前处理器 RAPL 编码范围。");
        }

        return encoded;
    }
}
