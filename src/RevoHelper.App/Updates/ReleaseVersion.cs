using System.Globalization;
using System.Text.RegularExpressions;

namespace RevoHelper.App.Updates;

/// <summary>
/// A version from our own build or a GitHub release tag. Anything else is rejected, so text from
/// the network can only ever reach the UI as digits we formatted ourselves.
/// </summary>
public sealed record ReleaseVersion(Version Number, int? Beta) : IComparable<ReleaseVersion>
{
    private static readonly Regex Pattern = new(
        @"\A[vV]?([0-9]{1,5})\.([0-9]{1,5})(?:\.([0-9]{1,5}))?(?:\.([0-9]{1,5}))?(?:-beta\.([0-9]{1,5}))?(?:\+[0-9A-Za-z.-]+)?\z",
        RegexOptions.CultureInvariant);

    public static ReleaseVersion? TryParse(string? text)
    {
        if (text is null) return null;
        var match = Pattern.Match(text);
        if (!match.Success) return null;
        int Part(int group) => match.Groups[group].Success
            ? int.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture)
            : 0;
        return new(new Version(Part(1), Part(2), Part(3), Part(4)), match.Groups[5].Success ? Part(5) : null);
    }

    /// <summary>A beta comes before the release of the same number, so 1.0.4-beta.6 is older than 1.0.4.</summary>
    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null) return 1;
        var number = Number.CompareTo(other.Number);
        if (number != 0) return number;
        return (Beta, other.Beta) switch
        {
            (null, null) => 0,
            (null, _) => 1,
            (_, null) => -1,
            var (mine, theirs) => mine.Value.CompareTo(theirs.Value)
        };
    }

    public bool IsNewerThan(ReleaseVersion other) => CompareTo(other) > 0;

    public override string ToString()
    {
        var number = Number.Revision > 0 ? Number.ToString(4) : Number.ToString(3);
        return Beta is { } beta ? $"V{number}-beta.{beta}" : $"V{number}";
    }
}
