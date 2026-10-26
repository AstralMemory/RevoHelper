using System.Globalization;
using System.Text.RegularExpressions;
using RevoHelper.Core.Control;

namespace RevoHelper.Hardware.Windows.OemProfiles;

internal static partial class OemProfileFileName
{
    public static bool TryParse(string path, out string key, out PlatformMode mode)
    {
        key = string.Empty;
        mode = default;
        var match = Pattern().Match(Path.GetFileName(path));
        if (!match.Success || !int.TryParse(match.Groups["profile"].Value, NumberStyles.None,
                CultureInfo.InvariantCulture, out var profile) || profile < 1)
            return false;

        var modeKey = match.Groups["mode"].Value switch
        {
            "1" => "balanced",
            "2" => "office",
            "3" => "turbo",
            "4" => "custom",
            _ => string.Empty
        };
        if (modeKey.Length == 0) return false;
        mode = modeKey switch
        {
            "balanced" => PlatformMode.Balanced,
            "office" => PlatformMode.Office,
            "turbo" => PlatformMode.TurboQuiet,
            _ => PlatformMode.Custom
        };
        key = $"{modeKey}:{match.Groups["profile"].Value}";
        return true;
    }

    [GeneratedRegex(@"^Mode(?<mode>\d+)_Profile(?<profile>\d+)\.json$", RegexOptions.IgnoreCase)]
    private static partial Regex Pattern();
}
