using RevoHelper.Core.Control;

namespace RevoHelper.Hardware.Windows.CpuPower;

/// <summary>
/// A privileged direct CPU backend that can prove its write path without changing
/// the effective policy: implementations write the exact value they just read.
/// </summary>
public interface IVerifiableCpuPolicyController : ICpuPolicyController
{
    ValueTask<ControlResult> VerifyWriteAccessAsync(CancellationToken cancellationToken);
}
