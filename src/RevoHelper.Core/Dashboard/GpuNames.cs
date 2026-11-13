using System.Text.RegularExpressions;

namespace RevoHelper.Core.Dashboard;

public static partial class GpuNames
{
    /// <summary>
    /// Intel uses "Arc" for both. Discrete cards carry a series letter (Arc A370M, Arc B580,
    /// Arc Pro A30M); "Arc Graphics" and "Arc 140V" are the integrated GPU of Core Ultra chips.
    /// </summary>
    public static bool IsDiscreteIntelArc(string name) => DiscreteArc().IsMatch(name);

    [GeneratedRegex(@"Arc(\(TM\))?\s+(Pro\s+)?[A-Z]\d{2,3}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DiscreteArc();
}
