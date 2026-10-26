using RevoHelper.Core.Control;
using RevoHelper.Core.Hardware;

namespace RevoHelper.Hardware.Windows.OemControl;

public static class GcuRuntimeCapabilityMapper
{
    public static OemRuntimeEvidence Map(GcuCpuPolicyStatus status)
    {
        var modes = new HashSet<PlatformMode>();
        // The currently reported mode is evidence even on firmware that omits
        // stock profile indexes. It says nothing about the other mode families.
        if (status.OfficeProfileIndex.HasValue || status.OperatingMode == 0) modes.Add(PlatformMode.Office);
        if (status.GamingProfileIndex.HasValue || status.OperatingMode == 1) modes.Add(PlatformMode.Balanced);
        if (status.TurboProfileIndex.HasValue || status.OperatingMode == 2)
        {
            modes.Add(PlatformMode.TurboQuiet);
            if (status.SupportsTurboStrategies) modes.Add(PlatformMode.TurboHigh);
        }
        if (status.CustomProfileIndex is >= 0 and <= 4 || status.OperatingMode == 3) modes.Add(PlatformMode.Custom);

        var powerLimits = status.HasCpuPowerLimitSnapshot && status.Pl1MinimumWatts > 0 &&
                          status.Pl1MaximumWatts >= status.Pl1MinimumWatts &&
                          status.Pl2MinimumWatts > 0 &&
                          status.Pl2MaximumWatts >= status.Pl2MinimumWatts;
        return new(modes, powerLimits, false, false, false, false);
    }
}
