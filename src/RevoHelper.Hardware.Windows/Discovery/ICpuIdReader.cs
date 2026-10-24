namespace RevoHelper.Hardware.Windows.Discovery;

public sealed record CpuIdSignature(
    string VendorId,
    int Family,
    int Model,
    int Stepping);

public interface ICpuIdReader
{
    CpuIdSignature? Read();
}
