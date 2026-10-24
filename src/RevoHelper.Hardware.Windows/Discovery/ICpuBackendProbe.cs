using RevoHelper.Core.Hardware;

namespace RevoHelper.Hardware.Windows.Discovery;

public interface ICpuBackendProbe
{
    IReadOnlyList<CpuBackendCapability> Probe(CpuIdentity cpu);
}
