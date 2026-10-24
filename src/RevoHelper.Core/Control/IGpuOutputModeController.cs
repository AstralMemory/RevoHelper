namespace RevoHelper.Core.Control;

public interface IGpuOutputModeController
{
    ValueTask<GpuOutputModeState?> ReadStateAsync(CancellationToken cancellationToken);
    ValueTask<ControlResult> SetModeAsync(GpuOutputMode mode, CancellationToken cancellationToken);
}
