using RevoHelper.Hardware.Windows.CpuPower;

namespace RevoHelper.Hardware.Windows.Tests.CpuPower;

public sealed class IntelRaplPowerLimitCodecTests
{
    [Fact]
    public void Decode_UsesRaplPowerUnitAndBothPackageLimitFields()
    {
        const ulong powerUnitMsr = 3; // 1 / 2^3 W = 0.125 W per encoded unit.
        var packageLimit = 360UL | (1UL << 15) | (520UL << 32) | (1UL << 47);

        var decoded = IntelRaplPowerLimitCodec.Decode(powerUnitMsr, packageLimit);

        Assert.Equal(45, decoded.Pl1Watts);
        Assert.Equal(65, decoded.Pl2Watts);
        Assert.False(decoded.Locked);
    }

    [Fact]
    public void Encode_PreservesTimeClampAndUnrelatedBits()
    {
        const ulong powerUnitMsr = 3;
        const ulong original =
            0x12UL << 17 |
            1UL << 16 |
            0x2AUL << 49 |
            1UL << 48 |
            1UL << 60;

        var encoded = IntelRaplPowerLimitCodec.Encode(powerUnitMsr, original, 45, 65);

        Assert.Equal(original & IntelRaplPowerLimitCodec.PreservedBitsMask,
            encoded & IntelRaplPowerLimitCodec.PreservedBitsMask);
        Assert.Equal(360UL, encoded & 0x7FFFUL);
        Assert.Equal(520UL, (encoded >> 32) & 0x7FFFUL);
        Assert.NotEqual(0UL, encoded & (1UL << 15));
        Assert.NotEqual(0UL, encoded & (1UL << 47));
    }

    [Fact]
    public void Encode_RejectsLockedRegister()
    {
        var locked = 1UL << 63;

        var exception = Assert.Throws<InvalidOperationException>(() =>
            IntelRaplPowerLimitCodec.Encode(3, locked, 45, 65));

        Assert.Contains("锁定", exception.Message, StringComparison.Ordinal);
    }
}
