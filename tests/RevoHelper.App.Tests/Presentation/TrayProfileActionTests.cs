using RevoHelper.App.Presentation;
using RevoHelper.Core.Control;

namespace RevoHelper.App.Tests.Presentation;

public sealed class TrayProfileActionTests
{
    [Fact]
    public void All_ContainsEveryQuickSwitchTargetInStableOrder()
    {
        Assert.Collection(
            TrayProfileAction.All,
            item => Assert.Equal(("办公模式", new PlatformModeSelection(PlatformMode.Office)), (item.Label, item.Selection)),
            item => Assert.Equal(("平衡模式", new PlatformModeSelection(PlatformMode.Balanced)), (item.Label, item.Selection)),
            item => Assert.Equal(("狂暴模式 · 静音", new PlatformModeSelection(PlatformMode.TurboQuiet)), (item.Label, item.Selection)),
            item => Assert.Equal(("狂暴模式 · 高能", new PlatformModeSelection(PlatformMode.TurboHigh)), (item.Label, item.Selection)),
            item => Assert.Equal(("自定义方案 1", new PlatformModeSelection(PlatformMode.Custom, 1)), (item.Label, item.Selection)),
            item => Assert.Equal(("自定义方案 2", new PlatformModeSelection(PlatformMode.Custom, 2)), (item.Label, item.Selection)),
            item => Assert.Equal(("自定义方案 3", new PlatformModeSelection(PlatformMode.Custom, 3)), (item.Label, item.Selection)));
    }

    [Theory]
    [InlineData(0, "#22A967")]
    [InlineData(1, "#2185F4")]
    [InlineData(2, "#9339DB")]
    [InlineData(4, "#D58A00")]
    public void Actions_ExposeStableModeColor(int index, string expectedColor) =>
        Assert.Equal(expectedColor, TrayProfileAction.All[index].DotColor);

    [Fact]
    public void IsCurrent_RequiresExactModeAndCustomIndex()
    {
        Assert.True(TrayProfileAction.All[1].IsCurrent(new(PlatformMode.Balanced)));
        Assert.True(TrayProfileAction.All[5].IsCurrent(new(PlatformMode.Custom, 2)));
        Assert.False(TrayProfileAction.All[5].IsCurrent(new(PlatformMode.Custom, 1)));
    }
}
