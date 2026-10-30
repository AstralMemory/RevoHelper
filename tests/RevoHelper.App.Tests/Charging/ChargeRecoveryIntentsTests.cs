using RevoHelper.App.Charging;

namespace RevoHelper.App.Tests.Charging;

public sealed class ChargeRecoveryIntentsTests
{
    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(true, true)]
    public void ReadableJournalRetainsRecordedOwnership(bool threshold, bool platform)
    {
        var result = ChargeRecoveryIntents.ReadOrConservativeFallback(
            phase => phase == "ThresholdApplyIntent" ? threshold : platform);
        Assert.Equal(threshold, result.Threshold); Assert.Equal(platform, result.Platform);
        Assert.Null(result.Error);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void UnreadableJournalNeverPreventsOwnedRecovery(int failure)
    {
        var result = ChargeRecoveryIntents.ReadOrConservativeFallback(_ => throw failure switch
        { 0 => new IOException("unreadable"), 1 => new UnauthorizedAccessException("denied"), _ => new KeyNotFoundException("missing field") });
        Assert.True(result.Threshold); Assert.True(result.Platform); Assert.NotNull(result.Error);
    }
}
