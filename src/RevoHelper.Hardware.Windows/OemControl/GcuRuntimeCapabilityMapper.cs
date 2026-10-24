using RevoHelper.Core.Control;
using RevoHelper.Core.Hardware;

namespace RevoHelper.Hardware.Windows.OemControl;

public static class GcuRuntimeCapabilityMapper
{
    public static OemRuntimeEvidence Map(GcuCpuPolicyStatus status)
    {
        var modes = new HashSet<PlatformMode>();
        if (status.OfficeProfileIndex.HasValue) modes.Add(PlatformMode.Office);
        if (status.GamingProfileIndex.HasValue) modes.Add(PlatformMode.Balanced);
        if (status.TurboProfileIndex.HasValue)
        {
            modes.Add(PlatformMode.TurboQuiet);
            if (status.Extreme.HasValue || status.Silent.HasValue) modes.Add(PlatformMode.TurboHigh);
        }
        if (status.CustomProfileIndex.HasValue) modes.Add(PlatformMode.Custom);

        var powerLimits = status.Pl1MinimumWatts > 0 &&
                          status.Pl1MaximumWatts >= status.Pl1MinimumWatts &&
                          status.Pl2MinimumWatts > 0 &&
                          status.Pl2MaximumWatts >= status.Pl2MinimumWatts;
        return new(modes, powerLimits, false, false, false, false);
    }
}
