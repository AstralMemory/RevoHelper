namespace RevoHelper.Hardware.Windows.CpuPower;

public interface IPawnIoModule : IDisposable
{
    bool IsAvailable { get; }

    string AvailabilityReason { get; }

    bool TryExecute(
        string function,
        IReadOnlyList<ulong> input,
        int outputLength,
        out ulong[] output);
}
