namespace RevoHelper.Core.Hardware;

public sealed record MachineIdentity(
    string Manufacturer,
    string ProductName,
    string BaseboardProduct,
    string ProcessorName,
    string BiosVersion,
    int ProcessorCoreCount = 0,
    int ProcessorThreadCount = 0,
    CpuIdentity? ProcessorIdentity = null)
{
    public CpuIdentity Cpu => ProcessorIdentity ?? CpuIdentity.Unknown;
}
