using RevoHelper.Hardware.Windows.CpuPower;

namespace RevoHelper.Hardware.Windows.Tests.CpuPower;

public sealed class IntelRaplCounterCodecTests
{
    [Fact]
    public void Delta32_HandlesCounterWrap()
    {
        var delta = IntelRaplCounterCodec.Delta32(0xFFFF_FFF0UL, 0x0000_0010UL);

        Assert.Equal(32U, delta);
    }

    [Fact]
    public void ToJoules_UsesEnergyUnitExponent()
    {
        const ulong unitMsr = 14UL << 8;

        var joules = IntelRaplCounterCodec.ToJoules(unitMsr, 16_384U);

        Assert.Equal(1, joules, 6);
    }

    [Fact]
    public void ToSeconds_UsesTimeUnitExponent()
    {
        const ulong unitMsr = 10UL << 16;

        var seconds = IntelRaplCounterCodec.ToSeconds(unitMsr, 1_024U);

        Assert.Equal(1, seconds, 6);
    }
}
