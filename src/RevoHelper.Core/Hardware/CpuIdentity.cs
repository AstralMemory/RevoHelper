namespace RevoHelper.Core.Hardware;

public enum CpuVendor
{
    Unknown,
    Intel,
    Amd
}

public sealed record CpuIdentity(
    CpuVendor Vendor,
    string VendorId,
    int Family,
    int Model,
    int Stepping,
    int Architecture)
{
    public static CpuIdentity Unknown { get; } = new(
        CpuVendor.Unknown,
        string.Empty,
        0,
        0,
        0,
        0);
}
