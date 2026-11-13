using RevoHelper.Hardware.Windows.Telemetry;

namespace RevoHelper.Hardware.Windows.Tests.Telemetry;

public sealed class NvidiaDevicePowerProbeTests
{
    // Read-only question to Windows; it must never throw, whatever the machine has.
    [Fact]
    public void Probe_RunsWithoutThrowing()
    {
        var value = new NvidiaDevicePowerProbe().ReadPowered();
        if (Environment.GetEnvironmentVariable("REVOHELPER_GPU_POWER_PROBE_FILE") is { Length: > 0 } file)
            File.WriteAllText(file, value?.ToString() ?? "null");
    }
}
