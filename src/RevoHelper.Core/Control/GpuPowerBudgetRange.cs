namespace RevoHelper.Core.Control;

/// <summary>
/// Input range for explicit low-power trials. The OEM's advertised bounds remain
/// unchanged; a lower request is accepted only after OEM and driver readback.
/// </summary>
public static class GpuPowerBudgetRange
{
    public const int LowPowerTargetWatts = 70;
    public const int MinimumRawTgpWatts = 25;

    public static int MinimumTarget(GpuPowerLimits limits) =>
        Math.Min(limits.TgpMinimum, LowPowerTargetWatts);
}
