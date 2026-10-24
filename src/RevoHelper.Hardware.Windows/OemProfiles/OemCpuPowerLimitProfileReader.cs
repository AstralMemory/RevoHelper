using System.Text.RegularExpressions;
using RevoHelper.Core.Control;

namespace RevoHelper.Hardware.Windows.OemProfiles;

public sealed partial class OemCpuPowerLimitProfileReader(string profileDirectory)
{
    public const string DefaultProfileDirectory = @"C:\Program Files\OEM\机械革命控制中心\AiStoneService\MyControlCenter\UserPofiles";

    public IReadOnlyList<CpuPowerLimitProfile> Read()
    {
        if (!Directory.Exists(profileDirectory))
        {
            return [];
        }

        var profiles = new List<CpuPowerLimitProfile>();
        foreach (var path in Directory.EnumerateFiles(profileDirectory, "Mode*_Profile*.json"))
        {
            var match = ProfileNamePattern().Match(Path.GetFileName(path));
            if (!match.Success || !TryMapMode(match.Groups["mode"].Value, out var mode))
            {
                continue;
            }

            try
            {
                var key = $"{mode}:{match.Groups["profile"].Value}";
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

    private static bool TryMapMode(string mode, out string mapped)
    {
        mapped = mode switch
        {
            "1" => "balanced",
            "2" => "office",
            "3" => "turbo",
            "4" => "custom",
            _ => string.Empty
        };
        return mapped.Length > 0;
    }

    [GeneratedRegex(@"^Mode(?<mode>\d+)_Profile(?<profile>\d+)\.json$", RegexOptions.IgnoreCase)]
    private static partial Regex ProfileNamePattern();
}
