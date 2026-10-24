using RevoHelper.Core.Control;

namespace RevoHelper.Hardware.Windows.OemControl;

public interface IGcuPlatformModeChannel
{
    ValueTask<GcuCpuPolicyStatus> ReadStatusAsync(CancellationToken cancellationToken);
    ValueTask WriteModeAsync(PlatformModeSelection selection, CancellationToken cancellationToken);
    ValueTask WriteTurboStrategyAsync(PlatformMode mode, CancellationToken cancellationToken);
    ValueTask WriteCompanionModeAsync(PlatformMode mode, CancellationToken cancellationToken);
}
