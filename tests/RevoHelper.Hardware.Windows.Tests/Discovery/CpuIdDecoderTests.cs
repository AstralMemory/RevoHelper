using RevoHelper.Core.Hardware;
using RevoHelper.Hardware.Windows.Discovery;

namespace RevoHelper.Hardware.Windows.Tests.Discovery;

public sealed class CpuIdDecoderTests
{
    [Fact]
    public void Decode_IntelFamily6_UsesExtendedModel()
    {
        const int eax = 2 | (6 << 8) | (12 << 16) | (6 << 4);

        var result = CpuIdDecoder.Decode("GenuineIntel", eax);

        Assert.Equal(CpuVendor.Intel, result.Vendor);
        Assert.Equal(6, result.Family);
        Assert.Equal(198, result.Model);
        Assert.Equal(2, result.Stepping);
    }

    [Fact]
    public void Decode_AmdExtendedFamily_UsesExtendedFamilyAndModel()
    {
        const int eax = 1 | (15 << 8) | (10 << 20) | (4 << 16) | (1 << 4);

        var result = CpuIdDecoder.Decode("AuthenticAMD", eax);

        Assert.Equal(CpuVendor.Amd, result.Vendor);
        Assert.Equal(25, result.Family);
        Assert.Equal(65, result.Model);
        Assert.Equal(1, result.Stepping);
    }

    [Fact]
    public void Decode_UnknownVendor_DoesNotInferFromAProductName()
    {
        var result = CpuIdDecoder.Decode("UnknownVendor", 0);

        Assert.Equal(CpuVendor.Unknown, result.Vendor);
    }
}
