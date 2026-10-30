using System.ComponentModel;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace RevoHelper.Hardware.Windows.BatteryCharge;

// Only constructed when an explicit read starts. All constructors are zero-I/O.
internal sealed class SystemManagementReviewedChargeBackend : IReviewedChargeBackend
{
    private string? logicalZeroPath;
    private string? selectedInstanceName;
    private bool disposed;

    public bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public ChargeWmiSignature ReadInterfaceSignature(TimeSpan timeout)
    {
        CheckDisposed();
        using var type = new ManagementClass(new ManagementScope(@"root\wmi"), new ManagementPath("AcpiTest_MULong"),
            new ObjectGetOptions { Timeout = timeout, UseAmendedQualifiers = false });
        type.Get();
        var method = type.Methods.Cast<MethodData>().SingleOrDefault(item => item.Name == "GetSetULong")
            ?? throw new InvalidDataException("Required WMI method is missing.");
        using var input = method.InParameters;
        using var output = method.OutParameters;
        var data = input?.Properties.Cast<PropertyData>().SingleOrDefault(property => property.Name == "Data");
        var result = output?.Properties.Cast<PropertyData>().SingleOrDefault(property => property.Name == "Return");
        if (data is null || result is null || data.IsArray || result.IsArray)
            throw new InvalidDataException("WMI Data/Return parameter shape differs from the reviewed scalar signature.");
        return new(Convert.ToString(FindQualifier(type.Qualifiers, "guid"), CultureInfo.InvariantCulture) ?? string.Empty,
            method.Name, Convert.ToInt32(FindQualifier(method.Qualifiers, "WmiMethodId"), CultureInfo.InvariantCulture),
            data.Name, data.Type.ToString(), result.Name, result.Type.ToString());
    }

    public IReadOnlyList<ChargeWmiInstance> ReadInterfaceInstances(TimeSpan timeout)
    {
        var rows = Query(@"root\wmi", "SELECT * FROM AcpiTest_MULong", timeout);
        var instances = rows.Select(row => new ChargeWmiInstance(RequireString(row, "InstanceName"), RequireBool(row, "Active"))).ToArray();
        var selected = ReviewedChargeProtocol.SelectReviewedInstance(instances);
        var zero = rows.Single(row => string.Equals(RequireString(row, "InstanceName"),
            selected, StringComparison.OrdinalIgnoreCase));
        logicalZeroPath = RequireString(zero, "__RELPATH");
        if (new ManagementPath(logicalZeroPath).ClassName != "AcpiTest_MULong")
            throw new InvalidDataException("WMI instance path does not address the reviewed class.");
        selectedInstanceName = selected;
        return instances;
    }

    public uint? InvokeGetSetULong(string instanceName, ulong packet, TimeSpan timeout)
    {
        CheckDisposed();
        if (!string.Equals(instanceName, selectedInstanceName, StringComparison.OrdinalIgnoreCase) || logicalZeroPath is null)
            throw new InvalidOperationException("Only the previously reviewed logical-zero instance may be invoked.");
        using var target = new ManagementObject(new ManagementScope(@"root\wmi"), new ManagementPath(logicalZeroPath),
            new ObjectGetOptions { Timeout = timeout });
        using var input = target.GetMethodParameters("GetSetULong");
        input["Data"] = packet;
        // Microsoft documents timeout for the corresponding semi-synchronous
        // operation. The outer transport additionally enforces a client deadline;
        // neither is claimed to reverse an already executed firmware operation.
        // https://learn.microsoft.com/dotnet/api/system.management.managementoptions.timeout
        using var output = target.InvokeMethod("GetSetULong", input, new InvokeMethodOptions { Timeout = timeout });
        var property = output?.Properties.Cast<PropertyData>().SingleOrDefault(item => item.Name == "Return");
        if (property?.Value is null) return null;
        if (property.Type != CimType.UInt32 || property.IsArray || property.Value is not uint value)
            throw new InvalidDataException("WMI Return value is not the reviewed UInt32 scalar.");
        return value;
    }

    public ReviewedChargeTelemetry ReadBatteryTelemetry(TimeSpan timeout)
    {
        var battery = RequireOne(Query(@"root\cimv2", "SELECT EstimatedChargeRemaining FROM Win32_Battery", timeout), "Windows battery");
        var statuses = Query(@"root\wmi", "SELECT * FROM BatteryStatus", timeout);
        var active = statuses.Where(row => RequireBool(row, "Active")).ToArray();
        var status = RequireOne(active, "active BatteryStatus");
        if (!GetSystemPowerStatus(out var power)) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (power.ACLineStatus is not (0 or 1)) throw new InvalidDataException("Windows AC state is unknown.");
        var online = RequireBool(status, "PowerOnline");
        if (online != (power.ACLineStatus == 1)) throw new InvalidDataException("Independent Windows AC observations disagree.");
        int? percent = battery.GetValueOrDefault("EstimatedChargeRemaining") is null
            ? null : checked((int)RequireUInt(battery, "EstimatedChargeRemaining"));
        uint? remaining = status.GetValueOrDefault("RemainingCapacity") is null ? null : RequireUInt(status, "RemainingCapacity");
        if (remaining == uint.MaxValue) remaining = null;
        var telemetry = new ReviewedChargeTelemetry(percent, online, RequireBool(status, "Charging"),
            RequireBool(status, "Discharging"), RequireUInt(status, "ChargeRate"), remaining);
        ReviewedChargeProtocol.ValidateTelemetry(telemetry);
        return telemetry;
    }

    private List<Dictionary<string, object?>> Query(string scope, string query, TimeSpan timeout)
    {
        CheckDisposed();
        var options = new System.Management.EnumerationOptions { Timeout = timeout, ReturnImmediately = true, Rewindable = false, BlockSize = 1 };
        using var searcher = new ManagementObjectSearcher(scope, query, options);
        using var results = searcher.Get();
        var rows = new List<Dictionary<string, object?>>();
        foreach (ManagementObject item in results)
        {
            using (item)
            {
                var row = item.Properties.Cast<PropertyData>().ToDictionary(property => property.Name, property => (object?)property.Value,
                    StringComparer.OrdinalIgnoreCase);
                row["__RELPATH"] = item.Path.RelativePath;
                rows.Add(row);
            }
        }
        return rows;
    }

    private static Dictionary<string, object?> RequireOne(IReadOnlyList<Dictionary<string, object?>> rows, string label) =>
        rows.Count == 1 ? rows[0] : throw new InvalidDataException($"A single {label} observation is required.");

    private static string RequireString(IReadOnlyDictionary<string, object?> row, string name) =>
        row.TryGetValue(name, out var value) && value is string text && !string.IsNullOrEmpty(text)
            ? text : throw new InvalidDataException($"Required WMI string {name} is missing.");

    private static bool RequireBool(IReadOnlyDictionary<string, object?> row, string name) =>
        row.TryGetValue(name, out var value) && value is bool flag
            ? flag : throw new InvalidDataException($"Required WMI boolean {name} is missing or unknown.");

    private static uint RequireUInt(IReadOnlyDictionary<string, object?> row, string name)
    {
        if (!row.TryGetValue(name, out var value) || value is null || value is not
            (byte or ushort or uint or ulong or sbyte or short or int or long))
            throw new InvalidDataException($"Required WMI integer {name} is missing or malformed.");
        try { return Convert.ToUInt32(value, CultureInfo.InvariantCulture); }
        catch (Exception exception) when (exception is OverflowException or FormatException)
        { throw new InvalidDataException($"Required WMI integer {name} is out of range.", exception); }
    }

    private static object FindQualifier(QualifierDataCollection qualifiers, string name) =>
        qualifiers.Cast<QualifierData>().SingleOrDefault(qualifier => string.Equals(qualifier.Name, name, StringComparison.OrdinalIgnoreCase))?.Value
        ?? throw new InvalidDataException($"Required WMI qualifier {name} is missing.");

    private void CheckDisposed() => ObjectDisposedException.ThrowIf(disposed, this);
    public void Dispose() { disposed = true; logicalZeroPath = null; selectedInstanceName = null; }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    [StructLayout(LayoutKind.Sequential)]
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
