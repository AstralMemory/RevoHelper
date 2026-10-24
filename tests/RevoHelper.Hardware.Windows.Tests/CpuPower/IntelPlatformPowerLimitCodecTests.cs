using RevoHelper.Hardware.Windows.CpuPower;

namespace RevoHelper.Hardware.Windows.Tests.CpuPower;

public sealed class IntelPlatformPowerLimitCodecTests
{
    [Fact]
    public void Decode_ReportsBothLimitsEnableClampAndLock()
    {
        const ulong powerUnitMsr = 3;
        const ulong raw =
            680UL |
            (1UL << 15) |
            (1UL << 16) |
            (960UL << 32) |
            (1UL << 47) |
            (1UL << 48) |
            (1UL << 63);

        var decoded = IntelPlatformPowerLimitCodec.Decode(powerUnitMsr, raw);

        Assert.Equal(85, decoded.Pl1Watts);
        Assert.True(decoded.Pl1Enabled);
        Assert.True(decoded.Pl1Clamped);
        Assert.Equal(120, decoded.Pl2Watts);
        Assert.True(decoded.Pl2Enabled);
        Assert.True(decoded.Pl2Clamped);
        Assert.True(decoded.Locked);
    }
}
