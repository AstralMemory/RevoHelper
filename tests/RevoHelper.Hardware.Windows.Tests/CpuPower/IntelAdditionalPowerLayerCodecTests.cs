using RevoHelper.Hardware.Windows.CpuPower;

namespace RevoHelper.Hardware.Windows.Tests.CpuPower;

public sealed class IntelAdditionalPowerLayerCodecTests
{
    [Fact]
    public void DecodeIaPlaneLimit_UsesRaplUnitAndControlBits()
    {
        const ulong raw = 680UL | (1UL << 15) | (1UL << 16) | (1UL << 31);

        var decoded = IntelIaPlanePowerLimitCodec.Decode(3, raw);

        Assert.Equal(85, decoded.Watts);
        Assert.True(decoded.Enabled);
        Assert.True(decoded.Clamped);
        Assert.True(decoded.Locked);
    }

    [Fact]
    public void DecodeVrCurrentConfig_UsesOneEighthAmpUnits()
    {
        const ulong raw = 800UL | (1UL << 31);

        var decoded = IntelVrCurrentConfigCodec.Decode(raw);

        Assert.Equal(100, decoded.CurrentLimitAmps);
        Assert.True(decoded.Locked);
    }

    [Fact]
    public void DecodePackageThermalStatus_ReportsOnlyLiveStatusBits()
    {
        const ulong raw = (1UL << 2) | (1UL << 10) | (1UL << 11);

        var decoded = IntelPackageThermalStatusDecoder.DecodeCurrent(raw);

        Assert.Equal(["PROCHOT", "POWER_LIMIT"], decoded);
    }

    [Fact]
    public void DecodePackagePowerInfo_UsesRaplUnitForThermalMinAndMax()
    {
        const ulong raw = 440UL | (80UL << 16) | (1280UL << 32);

        var decoded = IntelPackagePowerInfoCodec.Decode(3, raw);

        Assert.Equal(55, decoded.ThermalSpecWatts);
        Assert.Equal(10, decoded.MinimumWatts);
        Assert.Equal(160, decoded.MaximumWatts);
    }
}
