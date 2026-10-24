using RevoHelper.Core.Control;
using RevoHelper.Core.Hardware;

namespace RevoHelper.Hardware.Windows.OemProfiles;

public static class OemCpuPowerLimitProfileCapabilityMapper
{
    public static OemRuntimeEvidence Map(IEnumerable<CpuPowerLimitProfile> profiles)
    {
        var modes = new HashSet<PlatformMode>();
        foreach (var profile in profiles)
        {
            var separator = profile.Key.IndexOf(':');
            var modeKey = separator >= 0 ? profile.Key[..separator] : profile.Key;
            switch (modeKey.ToLowerInvariant())
            {
                case "office":
                    modes.Add(PlatformMode.Office);
                    break;
                case "balanced":
                    modes.Add(PlatformMode.Balanced);
                    break;
                case "turbo":
                    modes.Add(PlatformMode.TurboQuiet);
                    modes.Add(PlatformMode.TurboHigh);
                    break;
                case "custom":
                    modes.Add(PlatformMode.Custom);
                    break;
            }
        }

        // Profile files prove that the OEM exposes a platform mode. They do not
        // prove that RevoHelper can write or verify the corresponding power limit.
        return new(modes, false, false, false, false, false);
    }
}
