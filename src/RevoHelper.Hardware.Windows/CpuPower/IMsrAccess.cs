namespace RevoHelper.Hardware.Windows.CpuPower;

public interface IMsrAccess : IDisposable
{
    bool IsAvailable { get; }
    string AvailabilityReason { get; }
    bool TryRead(uint register, out ulong value);
    bool TryWrite(uint register, ulong value);
}
