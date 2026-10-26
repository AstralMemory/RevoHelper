using RevoHelper.Core.Control;
using RevoHelper.Core.Discovery;
using RevoHelper.Hardware.Windows.Discovery;

namespace RevoHelper.Hardware.Windows.OemProfiles;

public sealed class OemCpuPowerLimitProfileReader(string profileDirectory)
{
    public const string DefaultProfileDirectory = @"C:\Program Files\OEM\机械革命控制中心\AiStoneService\MyControlCenter\UserPofiles";

    public static string ResolveProfileDirectory(OemControlInventory inventory)
    {
        var discovered = inventory.Components.FirstOrDefault(component => component.Name == "OEM profile store")?.Path;
        if (!string.IsNullOrWhiteSpace(discovered) && Path.IsPathFullyQualified(discovered)) return discovered;
        var service = inventory.Components.FirstOrDefault(component => component.Name == "GCUBridge" && component.Present);
        var controlRoot = OemControlPathResolver.ResolveFromServiceImagePath(service?.Path);
        // A known installation owns its profile directory even when that directory
        // is empty; do not mix it with files left by a different installation.
        return controlRoot is not null ? Path.Combine(controlRoot, "UserPofiles") : DefaultProfileDirectory;
    }

    public IReadOnlyList<CpuPowerLimitProfile> Read()
    {
        if (!Directory.Exists(profileDirectory))
        {
            return [];
        }

        var profiles = new List<CpuPowerLimitProfile>();
        foreach (var path in Directory.EnumerateFiles(profileDirectory, "Mode*_Profile*.json"))
        {
            if (!OemProfileFileName.TryParse(path, out var key, out _))
            {
                continue;
            }

            try
            {
                var profile = OemCpuPowerLimitProfileParser.Parse(key, File.ReadAllText(path));
                if (profile is not null)
                {
                    profiles.Add(profile);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // One unreadable OEM profile must not prevent the remaining read-only inventory.
            }
        }

        return profiles.OrderBy(profile => profile.Key, StringComparer.OrdinalIgnoreCase).ToArray();
    }

}
