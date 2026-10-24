using RevoHelper.Core.Control;
using RevoHelper.Core.Hardware;
using RevoHelper.Hardware.Windows.CpuPower;

namespace RevoHelper.Hardware.Windows.Discovery;

public sealed record DirectCpuPolicyBackendProbeResult(
    ICpuPolicyController? Controller,
    CpuBackendCapability? Capability);

/// <summary>
/// Performs an actual read probe before a privileged CPU power backend is exposed.
/// CPU vendor strings select candidates only; successful hardware readback grants availability.
/// </summary>
public sealed class DirectCpuPolicyBackendProbe
{
    private readonly Func<(ICpuPolicyController Controller, string CandidateReason)> intelFactory;
    private readonly Func<string, (ICpuPolicyController Controller, string CandidateReason)> amdFactory;

    public DirectCpuPolicyBackendProbe()
        : this(CreateIntelCandidate, CreateAmdCandidate)
    {
    }

    public DirectCpuPolicyBackendProbe(
        Func<(ICpuPolicyController Controller, string CandidateReason)> intelFactory,
        Func<string, (ICpuPolicyController Controller, string CandidateReason)> amdFactory)
    {
        this.intelFactory = intelFactory;
        this.amdFactory = amdFactory;
    }

    public async Task<DirectCpuPolicyBackendProbeResult> ProbeAsync(
        CpuIdentity cpu,
        string applicationDirectory,
        CancellationToken cancellationToken)
    {
        return cpu.Vendor switch
        {
            CpuVendor.Intel => await ProbeCandidateAsync(
                CpuControlBackend.IntelRapl,
                intelFactory,
                cancellationToken).ConfigureAwait(false),
            CpuVendor.Amd => await ProbeCandidateAsync(
                CpuControlBackend.AmdSmu,
                () => amdFactory(applicationDirectory),
                cancellationToken).ConfigureAwait(false),
            _ => new(null, null)
        };
    }

    private static async Task<DirectCpuPolicyBackendProbeResult> ProbeCandidateAsync(
        CpuControlBackend backend,
        Func<(ICpuPolicyController Controller, string CandidateReason)> factory,
        CancellationToken cancellationToken)
    {
        ICpuPolicyController? controller = null;
        try
        {
            var candidate = factory();
            controller = candidate.Controller;
            var readback = await controller.ReadCpuPolicyAsync(cancellationToken).ConfigureAwait(false);
            if (readback is not null && controller is IVerifiableCpuPolicyController verifiable)
            {
                var verification = await verifiable.VerifyWriteAccessAsync(cancellationToken).ConfigureAwait(false);
                if (verification.Succeeded)
                {
                    return new(
                        controller,
                        new CpuBackendCapability(
                            backend,
                            CapabilityAvailability.Available,
                            $"{candidate.CandidateReason} 已完成真实读写探测。"));
                }

                DisposeController(controller);
                return new(
                    null,
                    new CpuBackendCapability(
                        backend,
                        CapabilityAvailability.Unavailable,
                        $"{candidate.CandidateReason} 写入验证失败：{verification.Reason}"));
            }

            DisposeController(controller);
            return new(
                null,
                new CpuBackendCapability(
                    backend,
                    CapabilityAvailability.Unavailable,
                    readback is null
                        ? $"{candidate.CandidateReason} 未取得真实功耗限制回读，保持禁用。"
                        : $"{candidate.CandidateReason} 不支持无状态变化的写入验证，保持禁用。"));
        }
        catch (OperationCanceledException)
        {
            DisposeController(controller);
            throw;
        }
        catch (Exception exception)
        {
            DisposeController(controller);
            return new(
                null,
                new CpuBackendCapability(
                    backend,
                    CapabilityAvailability.Unavailable,
                    $"直连后端探测失败：{exception.Message}"));
        }
    }

    private static (ICpuPolicyController Controller, string CandidateReason) CreateIntelCandidate()
    {
        var module = WindowsPawnIoModule.LoadSignedIntelMsrModule();
        var access = new PawnIoMsrAccess(module);
        return (new IntelRaplCpuPolicyController(access), access.AvailabilityReason);
    }

    private static (ICpuPolicyController Controller, string CandidateReason) CreateAmdCandidate(
        string applicationDirectory)
    {
        var path = RyzenAdjProcessClient.GetDefaultExecutablePath(applicationDirectory);
        var client = new RyzenAdjProcessClient(path);
        return (new RyzenAdjCpuPolicyController(client), client.AvailabilityReason);
    }

    private static void DisposeController(ICpuPolicyController? controller)
    {
        if (controller is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
