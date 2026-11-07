using RevoHelper.App.Updates;

namespace RevoHelper.App.Tests.Updates;

public sealed class ReleaseVersionTests
{
    [Theory]
    [InlineData("v1.0.3", "V1.0.3")]
    [InlineData("V1.0.3", "V1.0.3")]
    [InlineData("1.0.3", "V1.0.3")]
    [InlineData("1.1", "V1.1.0")]
    [InlineData("v1.0.3.1", "V1.0.3.1")]
    [InlineData("1.0.4-beta.5", "V1.0.4-beta.5")]
    [InlineData("1.0.4-beta.5+20261107", "V1.0.4-beta.5")]
    [InlineData("1.0.4+abc.123", "V1.0.4")]
    public void TryParse_AcceptsOurVersionsAndFormatsThemItself(string text, string shown)
    {
        var version = ReleaseVersion.TryParse(text);
        Assert.NotNull(version);
        Assert.Equal(shown, version.ToString());
        Assert.Equal(0, version.CompareTo(ReleaseVersion.TryParse(shown)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("v1")]
    [InlineData("v1.0.x")]
    [InlineData("1.0.3-rc.1")]
    [InlineData("1.0.3.4.5")]
    [InlineData(" v1.0.3")]
    [InlineData("v1.0.3 ")]
    [InlineData("v1.0.3\n")]
    [InlineData("v999999.0.0")]
    [InlineData("１.０.３")]
    [InlineData("https://example.com/v1.0.3")]
    [InlineData("v1.0.3<script>")]
    public void TryParse_RejectsEverythingElse(string? text) => Assert.Null(ReleaseVersion.TryParse(text));

    [Theory]
    [InlineData("v1.0.4", "1.0.4-beta.6")]
    [InlineData("1.0.4-beta.6", "1.0.4-beta.5")]
    [InlineData("1.0.4-beta.1", "v1.0.3")]
    [InlineData("v1.0.10", "v1.0.9")]
    [InlineData("v1.1.0", "v1.0.99")]
    [InlineData("v1.0.3.1", "v1.0.3")]
    public void IsNewerThan_OrdersReleases(string newer, string older)
    {
        var first = ReleaseVersion.TryParse(newer)!;
        var second = ReleaseVersion.TryParse(older)!;
        Assert.True(first.IsNewerThan(second));
        Assert.False(second.IsNewerThan(first));
    }

    [Theory]
    [InlineData("v1.0.3", "1.0.3")]
    [InlineData("1.1", "1.1.0")]
    [InlineData("1.0.4-beta.5+20261107", "1.0.4-beta.5")]
    public void IsNewerThan_IsFalseForTheSameVersion(string first, string second) =>
        Assert.False(ReleaseVersion.TryParse(first)!.IsNewerThan(ReleaseVersion.TryParse(second)!));
}
