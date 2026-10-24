using RevoHelper.Core.Control;

namespace RevoHelper.Hardware.Windows.OemControl;

public static class GcuPlatformModeStatusMapper
{
    public static PlatformModeSelection? Map(GcuCpuPolicyStatus status) => status.OperatingMode switch
    {
        0 => new(PlatformMode.Office, status.OfficeProfileIndex ?? 0),
        1 => new(PlatformMode.Balanced, status.GamingProfileIndex ?? 0),
        2 when status.Extreme == 1 => new(PlatformMode.TurboHigh, status.TurboProfileIndex ?? 0),
        2 => new(PlatformMode.TurboQuiet, status.TurboProfileIndex ?? 0),
        3 => new(PlatformMode.Custom, (status.CustomProfileIndex ?? 0) + 1),
        _ => null
    };
}
