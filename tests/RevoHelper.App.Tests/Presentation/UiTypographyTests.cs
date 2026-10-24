using RevoHelper.App.Presentation;

namespace RevoHelper.App.Tests.Presentation;

public sealed class UiTypographyTests
{
    [Theory]
    [InlineData(7, 8.3)]
    [InlineData(8.8, 10.1)]
    [InlineData(11, 11.8)]
    [InlineData(18, 18.5)]
    public void SmallCopyReceivesAnExtraIncrementWithoutChangingGeometry(double size, double expected) =>
        Assert.Equal(expected, UiFontExtension.Grow(size), 6);
}
