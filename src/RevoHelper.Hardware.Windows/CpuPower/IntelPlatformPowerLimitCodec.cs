namespace RevoHelper.Hardware.Windows.CpuPower;

public sealed record IntelPlatformPowerLimits(
    int Pl1Watts,
    bool Pl1Enabled,
    bool Pl1Clamped,
    int Pl2Watts,
    bool Pl2Enabled,
    bool Pl2Clamped,
    bool Locked);

public static class IntelPlatformPowerLimitCodec
{
    private const ulong LimitMask = 0x7FFFUL;

    public static IntelPlatformPowerLimits Decode(ulong powerUnitMsr, ulong raw)
    {
        var unit = 1d / (1UL << (int)(powerUnitMsr & 0xFUL));
        return new(
            ToWatts(raw & LimitMask, unit),
            (raw & (1UL << 15)) != 0,
            (raw & (1UL << 16)) != 0,
            ToWatts((raw >> 32) & LimitMask, unit),
            (raw & (1UL << 47)) != 0,
            (raw & (1UL << 48)) != 0,
            (raw & (1UL << 63)) != 0);
    }

    private static int ToWatts(ulong encoded, double unit) =>
        (int)Math.Round(encoded * unit, MidpointRounding.AwayFromZero);
}
