using RevoHelper.Core.Monitoring;

namespace RevoHelper.Core.Tests.Monitoring;

public sealed class FrameRateAccumulatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 0, 0, 0, TimeSpan.Zero);
    private static readonly ProcessTarget Target = new(42, Now.AddMinutes(-1), "javaw", "Minecraft");

    [Fact]
    public void PresentedFrames_ProduceMeasuredRate_NotRefreshRate()
    {
        var accumulator = new FrameRateAccumulator();
        accumulator.SetTarget(Target);
        AddFrames(accumulator, 1, 60, 1000d / 60);

        var snapshot = accumulator.GetSnapshot(Now);

        Assert.Equal(FrameRateStatus.Available, snapshot.Status);
        Assert.Equal(60d, snapshot.FramesPerSecond!.Value, 6);
    }

    [Fact]
    public void MultipleSwapChains_ChooseDominantChain_DoNotAddTheirRates()
    {
        var accumulator = new FrameRateAccumulator();
        accumulator.SetTarget(Target);
        AddFrames(accumulator, 1, 60, 1000d / 60);
        AddFrames(accumulator, 2, 30, 1000d / 30);

        Assert.Equal(60d, accumulator.GetSnapshot(Now).FramesPerSecond!.Value, 6);
    }

    [Fact]
    public void StaleFrames_DisplayUnavailable_NotLastKnownRate()
    {
        var accumulator = new FrameRateAccumulator();
        accumulator.SetTarget(Target);
        AddFrames(accumulator, 0, 60, 1000d / 60);

        var snapshot = accumulator.GetSnapshot(Now.AddSeconds(3));

        Assert.Null(snapshot.FramesPerSecond);
        Assert.Equal(FrameRateStatus.WaitingForFrames, snapshot.Status);
    }

    [Fact]
    public void ReusedPid_ClearsSamples()
    {
        var accumulator = new FrameRateAccumulator();
        accumulator.SetTarget(Target);
        AddFrames(accumulator, 1, 60, 1000d / 60);

        accumulator.SetTarget(Target with { StartedAtUtc = Now });

        Assert.Null(accumulator.GetSnapshot(Now).FramesPerSecond);
    }

    [Fact]
    public void SameTargetWithChangedWindowTitle_RetainsSamples()
    {
        var accumulator = new FrameRateAccumulator();
        accumulator.SetTarget(Target);
        AddFrames(accumulator, 1, 60, 1000d / 60);

        accumulator.SetTarget(Target with { DisplayName = "Minecraft - changed title" });

        Assert.NotNull(accumulator.GetSnapshot(Now).FramesPerSecond);
    }

    [Fact]
    public void UnknownTargetAndMalformedIntervals_AreIgnored()
    {
        var accumulator = new FrameRateAccumulator();
        accumulator.SetTarget(Target);
        foreach (var interval in new[] { 0d, -1, double.NaN, double.PositiveInfinity, 10001 })
        {
            accumulator.Add(new(42, 1, 1, interval), Now);
        }
        accumulator.Add(new(43, 1, 1, 10), Now);

        Assert.Null(accumulator.GetSnapshot(Now).FramesPerSecond);
    }

    [Fact]
    public void OutOfOrderOrDuplicateFrames_DoNotDistortAverage()
    {
        var accumulator = new FrameRateAccumulator();
        accumulator.SetTarget(Target);
        accumulator.Add(new(42, 1, 1, 10), Now);
        accumulator.Add(new(42, 1, 1.01, 10), Now);
        accumulator.Add(new(42, 1, 1.01, 100), Now);
        accumulator.Add(new(42, 1, .5, 100), Now);

        Assert.Equal(100d, accumulator.GetSnapshot(Now).FramesPerSecond);
    }

    [Fact]
    public void RollingWindow_DiscardsOldIntervals()
    {
        var accumulator = new FrameRateAccumulator();
        accumulator.SetTarget(Target);
        for (var index = 0; index < 60; index++) accumulator.Add(new(42, 1, index / 60d, 1000d / 60), Now);
        for (var index = 0; index < 100; index++) accumulator.Add(new(42, 1, 2 + index / 100d, 10), Now);

        Assert.Equal(100d, accumulator.GetSnapshot(Now).FramesPerSecond!.Value, 6);
    }

    [Fact]
    public void ClearTarget_DoesNotRetainPreviousFps()
    {
        var accumulator = new FrameRateAccumulator();
        accumulator.SetTarget(Target);
        AddFrames(accumulator, 1, 60, 1000d / 60);
        accumulator.SetTarget(null);

        Assert.Null(accumulator.GetSnapshot(Now).FramesPerSecond);
        Assert.Equal(FrameRateStatus.NoTarget, accumulator.GetSnapshot(Now).Status);
    }

    private static void AddFrames(FrameRateAccumulator accumulator, ulong chain, int count, double interval)
    {
        for (var index = 0; index < count; index++)
        {
            accumulator.Add(new(42, chain, index * interval / 1000, interval), Now);
        }
    }
}
