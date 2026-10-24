using RevoHelper.Core.Monitoring;

namespace RevoHelper.Core.Tests.Monitoring;

public sealed class FrameTargetSelectionTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static readonly ProcessTarget Minecraft = new(42, Now, "javaw", "Minecraft");
    private static readonly ProcessTarget Meter = new(43, Now, "MiniMeters", "MiniMeters");
    private static readonly ProcessTarget RevoHelper = new(100, Now, "RevoHelper", "性能监控");

    [Fact]
    public void AutoTarget_FollowsActualForegroundProcess_WithoutGameNameList()
    {
        var provider = new FakeTargets { Foreground = Minecraft };
        var selection = new FrameTargetSelection(100);
        Assert.Equal(Minecraft, selection.Refresh(provider));
        provider.Foreground = Meter;
        Assert.Equal(Meter, selection.Refresh(provider));
    }

    [Fact]
    public void ClickingMonitor_RetainsPreviousExternalTarget()
    {
        var provider = new FakeTargets { Foreground = Minecraft };
        var selection = new FrameTargetSelection(100);
        selection.Refresh(provider);
        provider.Foreground = RevoHelper;

        Assert.Equal(Minecraft, selection.Refresh(provider));
    }

    [Fact]
    public void DesktopWithoutTarget_ClearsPreviousApplication()
    {
        var provider = new FakeTargets { Foreground = Minecraft };
        var selection = new FrameTargetSelection(100);
        selection.Refresh(provider);
        provider.Foreground = null;

        Assert.Null(selection.Refresh(provider));
    }

    [Fact]
    public void PinnedTarget_IsNotReplacedByForegroundApplication()
    {
        var provider = new FakeTargets { Foreground = Meter };
        var selection = new FrameTargetSelection(100);
        selection.SetPinnedTarget(Minecraft);

        Assert.Equal(Minecraft, selection.Refresh(provider));
    }

    [Fact]
    public void ExitedOrReusedPinnedPid_DoesNotJumpToUnrelatedProcess()
    {
        var provider = new FakeTargets { Foreground = Meter, DeadTarget = Minecraft };
        var selection = new FrameTargetSelection(100);
        selection.SetPinnedTarget(Minecraft);

        Assert.Null(selection.Refresh(provider));
        Assert.Equal(Minecraft, selection.PinnedTarget);
    }

    [Fact]
    public void ExitedLastTarget_IsClearedEvenWhileRevoHelperIsForeground()
    {
        var provider = new FakeTargets { Foreground = Minecraft };
        var selection = new FrameTargetSelection(100);
        selection.Refresh(provider);
        provider.Foreground = RevoHelper;
        provider.DeadTarget = Minecraft;

        Assert.Null(selection.Refresh(provider));
    }

    [Fact]
    public void Unpinning_ImmediatelyDiscardsPinnedTarget()
    {
        var selection = new FrameTargetSelection(100);
        selection.SetPinnedTarget(Minecraft);
        selection.SetPinnedTarget(null);

        Assert.Null(selection.CurrentTarget);
        Assert.Null(selection.PinnedTarget);
    }

    private sealed class FakeTargets : IProcessTargetProvider
    {
        public ProcessTarget? Foreground { get; set; }
        public ProcessTarget? DeadTarget { get; set; }
        public ProcessTarget? GetForegroundTarget() => Foreground;
        public bool IsAlive(ProcessTarget target) => target != DeadTarget;
        public IReadOnlyList<ProcessTarget> GetWindowTargets() => [Minecraft, Meter];
    }
}
