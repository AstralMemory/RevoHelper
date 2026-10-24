using System.Runtime.Intrinsics.X86;
using System.Text;

namespace RevoHelper.Hardware.Windows.Discovery;

public sealed class X86CpuIdReader : ICpuIdReader
{
    public CpuIdSignature? Read()
    {
        if (!X86Base.IsSupported) return null;

        var (_, ebx, ecx, edx) = X86Base.CpuId(0, 0);
        var vendorBytes = new byte[12];
        BitConverter.GetBytes(ebx).CopyTo(vendorBytes, 0);
        BitConverter.GetBytes(edx).CopyTo(vendorBytes, 4);
        BitConverter.GetBytes(ecx).CopyTo(vendorBytes, 8);
        var vendorId = Encoding.ASCII.GetString(vendorBytes).TrimEnd('\0');
        var (signature, _, _, _) = X86Base.CpuId(1, 0);
        var decoded = CpuIdDecoder.Decode(vendorId, signature);

        return new(vendorId, decoded.Family, decoded.Model, decoded.Stepping);
    }
}
