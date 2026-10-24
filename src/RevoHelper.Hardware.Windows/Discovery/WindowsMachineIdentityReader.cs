using RevoHelper.Core.Hardware;

namespace RevoHelper.Hardware.Windows.Discovery;

public sealed class WindowsMachineIdentityReader(
    IWmiQuery query,
    ICpuIdReader? cpuIdReader = null)
{
    public MachineIdentity Read()
    {
        var computer = query.ReadFirst("Win32_ComputerSystem");
        var processor = query.ReadFirst("Win32_Processor");
        var bios = query.ReadFirst("Win32_BIOS");
        var baseboard = query.ReadFirst("Win32_BaseBoard");

        var cpuId = (cpuIdReader ?? new X86CpuIdReader()).Read();
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

    private static string Value(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) ? value : string.Empty;

    private static int Number(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) && int.TryParse(value, out var number)
            ? number
            : 0;
}
