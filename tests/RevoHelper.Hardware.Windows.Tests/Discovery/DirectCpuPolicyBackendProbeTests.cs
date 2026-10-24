using RevoHelper.Core.Control;
using RevoHelper.Core.Hardware;
using RevoHelper.Hardware.Windows.CpuPower;
using RevoHelper.Hardware.Windows.Discovery;

namespace RevoHelper.Hardware.Windows.Tests.Discovery;

public sealed class DirectCpuPolicyBackendProbeTests
{
    [Fact]
    public async Task ProbeAsync_IntelReadbackAndSameValueWriteSucceed_ExposesVerifiedController()
    {
        var controller = new FakeCpuPolicyController(new CpuPolicy(45, 85, null));
        var probe = new DirectCpuPolicyBackendProbe(
            () => (controller, "signed Intel test backend"),
            _ => throw new InvalidOperationException());

        var result = await probe.ProbeAsync(
            new CpuIdentity(CpuVendor.Intel, "GenuineIntel", 6, 198, 2, 9),
            "C:\\app",
            CancellationToken.None);

        Assert.Same(controller, result.Controller);
        Assert.Equal(CpuControlBackend.IntelRapl, result.Capability!.Backend);
        Assert.Equal(CapabilityAvailability.Available, result.Capability.Availability);
        Assert.Contains("真实读写探测", result.Capability.Reason);
        Assert.False(controller.Disposed);
        Assert.Equal(1, controller.VerifyCount);
    }

    [Fact]
    public async Task ProbeAsync_ReadbackSucceedsButWriteVerificationFails_DoesNotExposeController()
    {
        var controller = new FakeCpuPolicyController(new CpuPolicy(45, 85, null))
        {
            VerifyResult = ControlResult.Rejected("write blocked")
        };
        var probe = new DirectCpuPolicyBackendProbe(
            () => (controller, "signed Intel test backend"),
            _ => throw new InvalidOperationException());

        var result = await probe.ProbeAsync(
            new CpuIdentity(CpuVendor.Intel, "GenuineIntel", 6, 198, 2, 9),
            "C:\\app",
            CancellationToken.None);

        Assert.Null(result.Controller);
        Assert.Equal(CapabilityAvailability.Unavailable, result.Capability!.Availability);
        Assert.Contains("write blocked", result.Capability.Reason, StringComparison.Ordinal);
        Assert.True(controller.Disposed);
    }

    [Fact]
    public async Task ProbeAsync_AmdReadbackFails_DoesNotExposeControllerAndDisposesIt()
    {
        var controller = new FakeCpuPolicyController(null);
        var probe = new DirectCpuPolicyBackendProbe(
            () => throw new InvalidOperationException(),
            _ => (controller, "RyzenAdj test backend"));

        var result = await probe.ProbeAsync(
            new CpuIdentity(CpuVendor.Amd, "AuthenticAMD", 25, 117, 2, 9),
            "C:\\app",
            CancellationToken.None);

        Assert.Null(result.Controller);
        Assert.Equal(CpuControlBackend.AmdSmu, result.Capability!.Backend);
        Assert.Equal(CapabilityAvailability.Unavailable, result.Capability.Availability);
        Assert.True(controller.Disposed);
    }

    [Fact]
    public async Task ProbeAsync_UnknownCpu_ReturnsNoDirectCapability()
    {
        var probe = new DirectCpuPolicyBackendProbe(
            () => throw new InvalidOperationException(),
            _ => throw new InvalidOperationException());

        var result = await probe.ProbeAsync(CpuIdentity.Unknown, "C:\\app", CancellationToken.None);

        Assert.Null(result.Controller);
        Assert.Null(result.Capability);
    }

    private sealed class FakeCpuPolicyController(CpuPolicy? readback) : IVerifiableCpuPolicyController, IDisposable
    {
        public bool Disposed { get; private set; }
        public int VerifyCount { get; private set; }
        public ControlResult VerifyResult { get; init; } = new(true, "test");

        public ValueTask<CpuPolicy?> ReadCpuPolicyAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(readback);

        public ValueTask<ControlResult> ApplyCpuPolicyAsync(
            CpuPolicy policy,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new ControlResult(true, "test"));

        public ValueTask<ControlResult> VerifyWriteAccessAsync(CancellationToken cancellationToken)
        {
            VerifyCount++;
            return ValueTask.FromResult(VerifyResult);
        }

        public void Dispose() => Disposed = true;
    }
}
