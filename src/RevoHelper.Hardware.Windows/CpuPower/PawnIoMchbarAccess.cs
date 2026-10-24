namespace RevoHelper.Hardware.Windows.CpuPower;

/// <summary>
/// Read-only access to the Intel MCHBAR module's package RAPL register.
/// The signed module exposes no write entry point.
/// </summary>
public sealed class PawnIoMchbarAccess : IDisposable
{
    public const ulong PackageRaplLimitOffset = 0x59A0;

    private readonly IPawnIoModule _module;

    public PawnIoMchbarAccess(IPawnIoModule module)
    {
        _module = module ?? throw new ArgumentNullException(nameof(module));
    }

    public bool IsAvailable => _module.IsAvailable;

    public string AvailabilityReason => _module.AvailabilityReason;

    public bool TryReadBaseAddress(out ulong value) =>
        TryRead("ioctl_get_mchbar_addr", [], out value);

    public bool TryReadPackageRaplLimit(out ulong value) =>
        TryRead("ioctl_read_qword", [PackageRaplLimitOffset], out value);

    public void Dispose() => _module.Dispose();

    private bool TryRead(string function, IReadOnlyList<ulong> input, out ulong value)
    {
        value = 0;
        if (!IsAvailable ||
            !_module.TryExecute(function, input, 1, out var output) ||
            output.Length != 1)
        {
            return false;
        }

        value = output[0];
        return true;
    }
}
