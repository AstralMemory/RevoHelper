namespace RevoHelper.Hardware.Windows.CpuPower;

public static class IntelCorePerfLimitReasonDecoder
{
    private static readonly (int Bit, string Name)[] CurrentReasons =
    [
        (0, "PROCHOT"),
        (1, "THERMAL"),
        (4, "RSR_LIMIT"),
        (5, "RATL"),
        (6, "VR_THERMALERT"),
        (7, "VR_TDC"),
        (8, "OTHER"),
        (10, "PBM_PL1"),
        (11, "PBM_PL2"),
        (12, "MAX_TURBO_LIMIT"),
        (13, "TURBO_ATTEN")
    ];

    public static IReadOnlyList<string> DecodeCurrent(ulong raw) =>
        CurrentReasons
            .Where(reason => (raw & (1UL << reason.Bit)) != 0)
            .Select(reason => reason.Name)
            .ToArray();
}
