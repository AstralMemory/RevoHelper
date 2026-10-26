using RevoHelper.Core.Hardware;

namespace RevoHelper.Hardware.Windows.Discovery;

public sealed class WindowsMachineIdentityReader(
    IWmiQuery query,
    ICpuIdReader? cpuIdReader = null,
    Action<string, Exception>? reportQueryFailure = null)
{
    public MachineIdentity Read()
    {
        var computer = ReadFields("Win32_ComputerSystem");
        var processor = ReadFields("Win32_Processor");
        var bios = ReadFields("Win32_BIOS");
        var baseboard = ReadFields("Win32_BaseBoard");

        CpuIdSignature? cpuId = null;
        try
        {
            cpuId = (cpuIdReader ?? new X86CpuIdReader()).Read();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            reportQueryFailure?.Invoke("CPUID", exception);
        }
        var vendorId = cpuId?.VendorId ?? Value(processor, "Manufacturer");
        var cpuIdentity = new CpuIdentity(
            CpuIdDecoder.Vendor(vendorId),
            vendorId,
            cpuId?.Family ?? 0,
            cpuId?.Model ?? 0,
            cpuId?.Stepping ?? 0,
            Number(processor, "Architecture"));

        return new MachineIdentity(
            Value(computer, "Manufacturer"),
            Value(computer, "Model"),
            Value(baseboard, "Product"),
            Value(processor, "Name"),
            Value(bios, "SMBIOSBIOSVersion"),
            Number(processor, "NumberOfCores"),
            Number(processor, "NumberOfLogicalProcessors"),
            cpuIdentity);
    }

    private IReadOnlyDictionary<string, string> ReadFields(string className)
    {
        try
        {
            return query.ReadFirst(className);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // An unavailable optional WMI class must not erase a confirmed manufacturer.
            // A failed computer query remains unknown; never infer a brand from the CPU.
            reportQueryFailure?.Invoke(className, exception);
            return new Dictionary<string, string>();
        }
    }

    private static string Value(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) ? value : string.Empty;

    private static int Number(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) && int.TryParse(value, out var number)
            ? number
            : 0;
}
