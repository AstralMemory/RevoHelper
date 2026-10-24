using RevoHelper.Core.Hardware;
using RevoHelper.Hardware.Windows.Discovery;

namespace RevoHelper.Hardware.Windows.Tests.Discovery;

public sealed class WindowsCpuBackendProbeTests
{
    [Fact]
    public void Probe_Intel_ReportsWindowsPolicyAndIntelRaplCandidateOnly()
    {
        var capabilities = new WindowsCpuBackendProbe().Probe(
            new(CpuVendor.Intel, "GenuineIntel", 6, 198, 2, 9));

        Assert.Equal(CapabilityAvailability.Available,
            capabilities.Single(item => item.Backend == CpuControlBackend.WindowsPowerPolicy).Availability);
        Assert.Equal(CapabilityAvailability.Unverified,
            capabilities.Single(item => item.Backend == CpuControlBackend.IntelRapl).Availability);
        Assert.DoesNotContain(capabilities, item => item.Backend == CpuControlBackend.AmdSmu);
    }

    [Fact]
    public void Probe_Amd_ReportsWindowsPolicyAndAmdSmuCandidateOnly()
    {
        var capabilities = new WindowsCpuBackendProbe().Probe(
            new(CpuVendor.Amd, "AuthenticAMD", 25, 97, 2, 9));

        Assert.Equal(CapabilityAvailability.Unverified,
            capabilities.Single(item => item.Backend == CpuControlBackend.AmdSmu).Availability);
        Assert.DoesNotContain(capabilities, item => item.Backend == CpuControlBackend.IntelRapl);
    }

    [Fact]
    public void Probe_Unknown_DoesNotGuessBackendFromCpuName()
    {
        var capabilities = new WindowsCpuBackendProbe().Probe(CpuIdentity.Unknown);

        Assert.Single(capabilities);
        Assert.Equal(CpuControlBackend.WindowsPowerPolicy, capabilities[0].Backend);
    }
}
