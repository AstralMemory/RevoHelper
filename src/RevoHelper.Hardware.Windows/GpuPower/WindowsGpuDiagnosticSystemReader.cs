using System.Globalization;
using System.Management;

namespace RevoHelper.Hardware.Windows.GpuPower;

/// <summary>Model and firmware names only: no serial numbers, user or computer names.</summary>
public sealed record GpuDiagnosticMachine(string Manufacturer, string Model, string Family, string Baseboard,
    string Bios, string EmbeddedController, string Processor, string Windows);

/// <summary>NVIDIA Platform Controllers and Framework (ACPI NVDA0820), which carries the firmware's GPU power values to the driver.</summary>
public sealed record NvpcfDevice(bool Present, string? Name, string? Status, uint? ErrorCode)
{
    public static NvpcfDevice Missing { get; } = new(false, null, null, null);
}

public static class WindowsGpuDiagnosticSystemReader
{
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(5);

    public static GpuDiagnosticMachine ReadMachine()
    {
        var computer = First("SELECT Manufacturer, Model, SystemFamily FROM Win32_ComputerSystem");
        var board = First("SELECT Product FROM Win32_BaseBoard");
        var bios = First("SELECT SMBIOSBIOSVersion, EmbeddedControllerMajorVersion, EmbeddedControllerMinorVersion FROM Win32_BIOS");
        var processor = First("SELECT Name FROM Win32_Processor");
        return new(Text(computer, "Manufacturer"), Text(computer, "Model"), Text(computer, "SystemFamily"),
            Text(board, "Product"), Text(bios, "SMBIOSBIOSVersion"), EmbeddedControllerVersion(bios),
            Text(processor, "Name"), Environment.OSVersion.Version.ToString());
    }

    public static NvpcfDevice ReadNvpcf()
    {
        var rows = Query(@"SELECT Name, Status, ConfigManagerErrorCode FROM Win32_PnPEntity WHERE DeviceID LIKE 'ACPI\\NVDA0820%'");
        if (rows.Count == 0) return NvpcfDevice.Missing;
        var row = rows[0];
        uint? code = row.TryGetValue("ConfigManagerErrorCode", out var value) && value is not null
            ? Convert.ToUInt32(value, CultureInfo.InvariantCulture) : null;
        return new(true, Text(row, "Name"), Text(row, "Status"), code);
    }

    /// <summary>Windows' own view of mains power; null when Windows does not know.</summary>
    public static bool? ReadPowerOnline() =>
        GetSystemPowerStatus(out var status) && status.ACLineStatus is 0 or 1 ? status.ACLineStatus == 1 : null;

    // EmbeddedControllerMajorVersion / MinorVersion are 255 when the firmware does not report them.
    private static string EmbeddedControllerVersion(IReadOnlyDictionary<string, object?> bios)
    {
        if (!bios.TryGetValue("EmbeddedControllerMajorVersion", out var major) || major is null ||
            !bios.TryGetValue("EmbeddedControllerMinorVersion", out var minor) || minor is null) return "";
        var majorValue = Convert.ToInt32(major, CultureInfo.InvariantCulture);
        var minorValue = Convert.ToInt32(minor, CultureInfo.InvariantCulture);
        return majorValue == 255 && minorValue == 255 ? "" : $"{majorValue}.{minorValue}";
    }

    private static IReadOnlyDictionary<string, object?> First(string query)
    {
        try { return Query(query).FirstOrDefault() ?? new Dictionary<string, object?>(); }
        catch (Exception exception) when (exception is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        { return new Dictionary<string, object?>(); }
    }

    private static List<Dictionary<string, object?>> Query(string query)
    {
        var options = new System.Management.EnumerationOptions { Timeout = QueryTimeout, ReturnImmediately = true, Rewindable = false };
        using var searcher = new ManagementObjectSearcher(new ManagementScope(@"root\cimv2"), new ObjectQuery(query), options);
        using var results = searcher.Get();
        var rows = new List<Dictionary<string, object?>>();
        foreach (ManagementObject item in results)
        {
            using (item)
                rows.Add(item.Properties.Cast<PropertyData>().ToDictionary(property => property.Name,
                    property => (object?)property.Value, StringComparer.OrdinalIgnoreCase));
        }
        return rows;
    }

    private static string Text(IReadOnlyDictionary<string, object?> row, string name) =>
        row.TryGetValue(name, out var value) && value is not null ? value.ToString()?.Trim() ?? "" : "";

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }
}
