namespace RevoHelper.Hardware.Windows.CpuPower;

public sealed class PawnIoMsrAccess : IMsrAccess
{
    private readonly IPawnIoModule _module;

    public PawnIoMsrAccess(IPawnIoModule module)
    {
        _module = module ?? throw new ArgumentNullException(nameof(module));
    }

    public bool IsAvailable => _module.IsAvailable;

    public string AvailabilityReason => _module.AvailabilityReason;

    public bool TryRead(uint register, out ulong value)
    {
        value = 0;
        if (!IsAvailable ||
            !_module.TryExecute("ioctl_read_msr", [register], 1, out var output) ||
            output.Length != 1)
        {
            return false;
        }

        value = output[0];
        return true;
    }

    public bool TryWrite(uint register, ulong value) =>
        IsAvailable &&
        _module.TryExecute("ioctl_write_msr", [register, value], 0, out _);

    public void Dispose() => _module.Dispose();
}
