namespace RevoHelper.Hardware.Windows.CpuPower;

public sealed record IntelIaPlanePowerLimit(int Watts, bool Enabled, bool Clamped, bool Locked);

public static class IntelIaPlanePowerLimitCodec
{
    public static IntelIaPlanePowerLimit Decode(ulong powerUnitMsr, ulong raw) =>
        new(
            IntelPowerUnit.ToWatts(powerUnitMsr, raw & 0x7FFFUL),
            (raw & (1UL << 15)) != 0,
            (raw & (1UL << 16)) != 0,
            (raw & (1UL << 31)) != 0);
}

public sealed record IntelVrCurrentConfig(double CurrentLimitAmps, bool Locked);

public static class IntelVrCurrentConfigCodec
{
    public static IntelVrCurrentConfig Decode(ulong raw) =>
        new((raw & 0xFFFFUL) / 8d, (raw & (1UL << 31)) != 0);
}

public static class IntelPackageThermalStatusDecoder
{
    private static readonly (int Bit, string Name)[] CurrentReasons =
    [
        (0, "THERMAL"),
        (2, "PROCHOT"),
        (4, "CRITICAL_TEMPERATURE"),
        (6, "THERMAL_THRESHOLD_1"),
        (8, "THERMAL_THRESHOLD_2"),
        (10, "POWER_LIMIT")
    ];

    public static IReadOnlyList<string> DecodeCurrent(ulong raw) =>
        CurrentReasons
            .Where(reason => (raw & (1UL << reason.Bit)) != 0)
            .Select(reason => reason.Name)
            .ToArray();
}

public sealed record IntelPackagePowerInfo(
    int ThermalSpecWatts,
    int MinimumWatts,
    int MaximumWatts);

public static class IntelPackagePowerInfoCodec
{
    public static IntelPackagePowerInfo Decode(ulong powerUnitMsr, ulong raw) =>
        new(
            IntelPowerUnit.ToWatts(powerUnitMsr, raw & 0x7FFFUL),
            IntelPowerUnit.ToWatts(powerUnitMsr, (raw >> 16) & 0x7FFFUL),
            IntelPowerUnit.ToWatts(powerUnitMsr, (raw >> 32) & 0x7FFFUL));
}

internal static class IntelPowerUnit
{
    public static int ToWatts(ulong powerUnitMsr, ulong encoded)
    {
        var unit = 1d / (1UL << (int)(powerUnitMsr & 0xFUL));
        return (int)Math.Round(encoded * unit, MidpointRounding.AwayFromZero);
    }
}
