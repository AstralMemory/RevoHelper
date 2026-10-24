using RevoHelper.Core.Control;

namespace RevoHelper.Hardware.Windows.OemControl;

public interface IGcuGpuOutputModeChannel
{
    ValueTask<GcuGpuOutputModeStatus> ReadStatusAsync(CancellationToken cancellationToken);
    ValueTask WriteModeAsync(GpuOutputMode mode, CancellationToken cancellationToken);
}

public interface IGpuOutputModeCapabilitySource
{
    GpuOutputModeCapabilities Read();
}
