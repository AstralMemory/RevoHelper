using RevoHelper.Core.Hardware;

namespace RevoHelper.Hardware.Windows.Discovery;

public sealed class WindowsCpuBackendProbe : ICpuBackendProbe
{
    public IReadOnlyList<CpuBackendCapability> Probe(CpuIdentity cpu)
    {
        var capabilities = new List<CpuBackendCapability>
        {
            new(
                CpuControlBackend.WindowsPowerPolicy,
                CapabilityAvailability.Available,
                "Windows PowrProf processor policy API is available.")
        };

        switch (cpu.Vendor)
        {
            case CpuVendor.Intel:
                capabilities.Add(new(
                    CpuControlBackend.IntelRapl,
                    CapabilityAvailability.Unverified,
                    "Intel CPU detected; direct RAPL access still requires a verified MSR/MMIO driver path."));
                break;
            case CpuVendor.Amd:
                capabilities.Add(new(
                    CpuControlBackend.AmdSmu,
                    CapabilityAvailability.Unverified,
                    "AMD CPU detected; direct SMU access still requires a verified RyzenAdj/SMU backend."));
                break;
        }

        return capabilities;
    }
}
