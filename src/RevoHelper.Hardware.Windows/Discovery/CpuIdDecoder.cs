using RevoHelper.Core.Hardware;

namespace RevoHelper.Hardware.Windows.Discovery;

public static class CpuIdDecoder
{
    public static CpuIdentity Decode(string vendorId, int signatureEax, int architecture = 0)
    {
        var baseFamily = (signatureEax >> 8) & 0x0f;
        var baseModel = (signatureEax >> 4) & 0x0f;
        var extendedFamily = (signatureEax >> 20) & 0xff;
        var extendedModel = (signatureEax >> 16) & 0x0f;
        var family = baseFamily == 0x0f ? baseFamily + extendedFamily : baseFamily;
        var model = baseFamily is 0x06 or 0x0f
            ? baseModel | (extendedModel << 4)
            : baseModel;

        return new(
            Vendor(vendorId),
            vendorId,
            family,
            model,
            signatureEax & 0x0f,
            architecture);
    }

    public static CpuVendor Vendor(string? vendorId) => vendorId?.Trim() switch
    {
        "GenuineIntel" => CpuVendor.Intel,
        "AuthenticAMD" => CpuVendor.Amd,
        _ => CpuVendor.Unknown
    };
}
