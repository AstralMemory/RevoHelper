using RevoHelper.Hardware.Windows.CpuPower;

namespace RevoHelper.Hardware.Windows.Tests.CpuPower;

public sealed class IntelCorePerfLimitReasonDecoderTests
{
    [Fact]
    public void DecodeCurrent_ReportsOnlyLiveLimitBits()
    {
        const ulong raw =
            (1UL << 7) |
            (1UL << 10) |
            (1UL << 24) |
            (1UL << 26);

        var reasons = IntelCorePerfLimitReasonDecoder.DecodeCurrent(raw);

        Assert.Equal(["VR_TDC", "PBM_PL1"], reasons);
    }
}
