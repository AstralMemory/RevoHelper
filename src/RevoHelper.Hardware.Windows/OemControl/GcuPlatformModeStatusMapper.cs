using RevoHelper.Core.Control;

namespace RevoHelper.Hardware.Windows.OemControl;

public static class GcuPlatformModeStatusMapper
{
    public static PlatformModeSelection? Map(GcuCpuPolicyStatus status) => status.OperatingMode switch
    {
        0 => new(PlatformMode.Office, status.OfficeProfileIndex ?? 0),
        1 => new(PlatformMode.Balanced, status.GamingProfileIndex ?? 0),
        2 when status.SupportsTurboStrategies && status.Extreme == 1 => new(PlatformMode.TurboHigh, status.TurboProfileIndex ?? 0),
        2 => new(PlatformMode.TurboQuiet, status.TurboProfileIndex ?? 0),
        // Custom slots have distinct settings. An absent index cannot identify
        // a restore baseline and must not silently become user-facing slot 1.
        3 when status.CustomProfileIndex is >= 0 and <= 4 => new(PlatformMode.Custom, status.CustomProfileIndex.Value + 1),
        _ => null
    };
}
