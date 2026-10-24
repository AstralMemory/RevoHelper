using System.Windows;
using RevoHelper.App.Presentation;

namespace RevoHelper.App.Tests.Presentation;

public sealed class WindowScaleGeometryTests
{
    public static IEnumerable<object[]> ScalesAndDrawers =>
        new[] { 70, 80, 90, 100, 110, 120, 130 }
            .SelectMany(scale => new[] { false, true }.Select(open => new object[] { scale, open }));

    [Theory]
    [MemberData(nameof(ScalesAndDrawers))]
    public void Calculate_ScalesWholeWindowAndPreservesMainAnchor(int percent, bool open)
    {
        var layout = WindowScaleGeometry.Calculate(percent, open, new Rect(0, 0, 4000, 2000), 1500, 120);
        var scale = percent / 100d;
        Assert.Equal((open ? 940 : 580) * scale, layout.Width, 8);
        Assert.Equal(550 * scale, layout.Height, 8);
        Assert.Equal((open ? 360 : 0) * scale, layout.DrawerOffset, 8);
        Assert.Equal(1500, layout.Left + layout.DrawerOffset, 8);
        Assert.Equal(120, layout.Top);
        Assert.False(layout.IsCapped);
    }

    [Theory]
    [InlineData(650, 500)]
    [InlineData(1400, 500)]
    [InlineData(350, 250)]
    public void Calculate_CapsUniformlyToWorkAreaWithoutOffscreenControls(double width, double height)
    {
        var area = new Rect(-700, 80, width, height);
        var layout = WindowScaleGeometry.Calculate(130, true, area, -600, 200);
        Assert.True(layout.IsCapped);
        Assert.Equal(Math.Min(width / 940, height / 550), layout.EffectiveScale, 8);
        Assert.True(area.Contains(new Rect(layout.Left, layout.Top, layout.Width, layout.Height)));
        Assert.Equal(940d / 550, layout.Width / layout.Height, 8);
    }

    [Fact]
    public void Calculate_RepeatedDrawerTransitionsDoNotAccumulateOffsets()
    {
        var area = new Rect(0, 0, 2000, 1200);
        var mainAnchor = 600d;
        for (var iteration = 0; iteration < 20; iteration++)
        {
            var open = WindowScaleGeometry.Calculate(130, true, area, mainAnchor, 200);
            var closed = WindowScaleGeometry.Calculate(130, false, area, open.MainPanelLeft, open.Top);
            Assert.Equal(mainAnchor, closed.Left, 8);
            mainAnchor = closed.MainPanelLeft;
        }
    }

    [Fact]
    public void Calculate_ClampsBothAxesAndNormalizesUnsupportedScale()
    {
        var layout = WindowScaleGeometry.Calculate(999, true, new Rect(20, 30, 1000, 700), -500, 900);
        Assert.Equal(1, layout.EffectiveScale);
        Assert.Equal(20, layout.Left);
        Assert.Equal(180, layout.Top);
    }
}
