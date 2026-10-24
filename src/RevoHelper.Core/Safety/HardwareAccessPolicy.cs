using RevoHelper.Core.Hardware;

namespace RevoHelper.Core.Safety;

public static class HardwareAccessPolicy
{
    public static HardwareAccessDecision Evaluate(
        HardwareOperation operation,
        HardwareSafetyContext context)
    {
        if (operation is HardwareOperation.ReadIdentity or HardwareOperation.ReadSensors)
        {
            return new(true, "Read-only operation.");
        }

        if (operation is HardwareOperation.SetDirectFanDuty)
        {
            return new(false, "Direct userspace fan-duty control is not permitted.");
        }

        if (context.MachineStatus is not MachineSupportStatus.Supported)
        {
            return new(false, "The machine is not explicitly supported for hardware writes.");
        }

        if (!context.AdapterVerified)
        {
            return new(false, "The hardware adapter has not been verified.");
        }

        if (context.VerifiedOperations is not null &&
            !context.VerifiedOperations.Contains(operation))
        {
            return new(false, $"The {operation} control module has not been verified.");
        }

        if (operation is HardwareOperation.WriteFanCurve &&
            !context.FirmwareFanFallbackVerified)
        {
            return new(false, "Firmware fan fallback has not been verified.");
        }

        return new(true, "All required safety prerequisites are verified.");
    }
}
