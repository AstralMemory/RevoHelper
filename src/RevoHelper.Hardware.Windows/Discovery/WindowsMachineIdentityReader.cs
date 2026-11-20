using Microsoft.Win32;
using RevoHelper.Core.Hardware;

namespace RevoHelper.Hardware.Windows.Discovery;

/// <summary>
/// WMI first. Where WMI is broken or slow (it depends on a service that cleanup tools disable),
/// the same names come from the registry copy Windows keeps of the firmware tables, and the
/// thread count from the scheduler. No value is invented.
/// </summary>
public sealed class WindowsMachineIdentityReader(
    IWmiQuery query,
    ICpuIdReader? cpuIdReader = null,
    Action<string, Exception>? reportQueryFailure = null,
    Func<string, string>? readFirmwareRegistry = null)
{
    private const string BiosKey = @"HARDWARE\DESCRIPTION\System\BIOS";
    private const string ProcessorKey = @"HARDWARE\DESCRIPTION\System\CentralProcessor\0";
    private readonly Func<string, string> registry = readFirmwareRegistry ?? ReadRegistryValue;

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

        var threads = Number(processor, "NumberOfLogicalProcessors");
        return new MachineIdentity(
            Or(Value(computer, "Manufacturer"), () => registry(BiosKey + "|SystemManufacturer")),
            Or(Value(computer, "Model"), () => registry(BiosKey + "|SystemProductName")),
            Or(Value(baseboard, "Product"), () => registry(BiosKey + "|BaseBoardProduct")),
            Or(Value(processor, "Name"), () => registry(ProcessorKey + "|ProcessorNameString")),
            Or(Value(bios, "SMBIOSBIOSVersion"), () => registry(BiosKey + "|BIOSVersion")),
            Number(processor, "NumberOfCores"),
            // The core count has no registry copy; it stays unknown rather than guessed.
            threads > 0 ? threads : Environment.ProcessorCount,
            cpuIdentity);
    }

    /// <summary>The processor's marketing name, as the firmware gave it to Windows.</summary>
    public static string ReadRegistryProcessorName() => ReadRegistryValue(ProcessorKey + "|ProcessorNameString").Trim();

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

    private static string ReadRegistryValue(string keyAndName)
    {
        var separator = keyAndName.IndexOf('|');
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(keyAndName[..separator]);
            return key?.GetValue(keyAndName[(separator + 1)..])?.ToString()?.Trim() ?? string.Empty;
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return string.Empty;
        }
    }

    private static string Or(string value, Func<string> fallback)
    {
        if (!string.IsNullOrWhiteSpace(value)) return value;
        try { return fallback() ?? string.Empty; }
        catch (Exception) { return string.Empty; }
    }

    private static string Value(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) ? value : string.Empty;

    private static int Number(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) && int.TryParse(value, out var number)
            ? number
            : 0;
}
