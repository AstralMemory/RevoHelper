using RevoHelper.Core.Control;

namespace RevoHelper.Hardware.Windows.OemControl;

public interface IGcuGpuOutputModeChannel
{
    ValueTask<GcuGpuOutputModeStatus> ReadStatusAsync(CancellationToken cancellationToken);
    ValueTask WriteModeAsync(GpuOutputMode mode, CancellationToken cancellationToken);
}

/// <summary>Null means the machine did not say, which is different from an explicit "no".</summary>
public sealed record GpuOutputMachineSupport(bool? DirectSwitch, bool? Integrated);

public interface IGpuOutputModeCapabilitySource
{
    GpuOutputMachineSupport Read();
}
