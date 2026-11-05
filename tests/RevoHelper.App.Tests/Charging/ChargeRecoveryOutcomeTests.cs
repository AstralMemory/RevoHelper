using RevoHelper.App.Charging;

namespace RevoHelper.App.Tests.Charging;

public sealed class ChargeRecoveryOutcomeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CacheOnlyMismatchDoesNotKeepTemporaryPlatformControlsLocked(bool cacheReadFailed)
    {
        // Replays the real S3 outcome: C3/B9 confirmed, only the read-only cache differs.
        var errors = cacheReadFailed ? new[] { "复核0x9164：模拟读取失败" } : Array.Empty<string>();
        var result = new ChargeRecoveryResult(true, true, false, errors);

        var wire = ExperimentalChargeHelperHost.CreateRecoveryStatus(result, 80);

        Assert.Equal("Restored", wire.Phase);
        Assert.False(wire.Enabled);
        Assert.False(wire.RecoveryPending);
        Assert.Same(result, wire.Recovery);
        Assert.False(wire.Recovery!.CacheMatchesOriginal);
        Assert.Equal(errors, wire.Recovery.Errors);
        Assert.Contains("缓存尚未确认一致", wire.Message);
        Assert.DoesNotContain("缓存观察已恢复", wire.Message);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    public void EitherLiveControlUnrestoredStillRequiresRecovery(bool platform, bool threshold, bool cache)
    {
        var result = new ChargeRecoveryResult(platform, threshold, cache, []);

        var wire = ExperimentalChargeHelperHost.CreateRecoveryStatus(result, 60);

        Assert.Equal("RecoveryPending", wire.Phase);
        Assert.True(wire.RecoveryPending);
        Assert.Same(result, wire.Recovery);
    }

    [Fact]
    public void FullyMatchingRecoveryStillReportsAllObservedValuesMatching()
    {
        var result = new ChargeRecoveryResult(true, true, true, []);

        var wire = ExperimentalChargeHelperHost.CreateRecoveryStatus(result, 80);

        Assert.Equal("Restored", wire.Phase);
        Assert.False(wire.RecoveryPending);
        Assert.Contains("缓存观察已恢复", wire.Message);
    }
}
