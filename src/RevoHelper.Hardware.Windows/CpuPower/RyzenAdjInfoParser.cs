using System.Globalization;
using System.Text.RegularExpressions;

namespace RevoHelper.Hardware.Windows.CpuPower;

public static partial class RyzenAdjInfoParser
{
    public static bool TryParsePowerLimits(string output, out RyzenAdjPowerLimits limits)
    {
        limits = new RyzenAdjPowerLimits(0, 0, 0);
        if (!TryReadWatts(output, "STAPM LIMIT", out var stapm) ||
            !TryReadWatts(output, "PPT LIMIT SLOW", out var slow) ||
            !TryReadWatts(output, "PPT LIMIT FAST", out var fast))
        {
            return false;
        }

        limits = new RyzenAdjPowerLimits(stapm, slow, fast);
        return true;
    }

    private static bool TryReadWatts(string output, string label, out decimal watts)
    {
        watts = 0;
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!line.Contains(label, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var suffix = line[(line.IndexOf(label, StringComparison.OrdinalIgnoreCase) + label.Length)..];
            var match = NumberRegex().Match(suffix);
            if (!match.Success ||
                !decimal.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
                value <= 0 || value > 1000)
            {
                return false;
            }

            watts = decimal.Round(value, 3, MidpointRounding.AwayFromZero);
            return watts > 0;
        }

        return false;
    }

    [GeneratedRegex(@"[-+]?\d+(?:\.\d+)?", RegexOptions.CultureInvariant)]
    private static partial Regex NumberRegex();
}
