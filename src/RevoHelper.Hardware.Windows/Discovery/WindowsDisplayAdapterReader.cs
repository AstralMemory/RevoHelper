using RevoHelper.Core.Dashboard;

namespace RevoHelper.Hardware.Windows.Discovery;

/// <summary>
/// The physical display adapters with their PCI vendor and, for Intel, whether it is the integrated
/// GPU. Intel has placed its integrated GPU at PCI bus 0, device 2 on every processor that has one,
/// so that position decides it, not the product name. When WMI is unavailable the list comes from
/// Plug and Play directly.
/// </summary>
public sealed class WindowsDisplayAdapterReader(
    IWmiQuery query,
    Func<string, PciLocation?>? readPciLocation = null,
    Func<IReadOnlyList<(string InstanceId, string Name)>>? presentDisplayAdapters = null)
{
    private readonly Func<string, PciLocation?> location = readPciLocation ?? PlugAndPlayDevices.ReadPciLocation;
    private readonly Func<IReadOnlyList<(string InstanceId, string Name)>> plugAndPlay =
        presentDisplayAdapters ?? PlugAndPlayDevices.PresentDisplayAdapters;

    public IReadOnlyList<string> ReadNames() => ReadAdapters().Select(adapter => adapter.Name).ToArray();

    public IReadOnlyList<DisplayAdapter> ReadAdapters()
    {
        IReadOnlyList<(string InstanceId, string Name)> devices;
        try
        {
            devices = query.ReadAll("Win32_VideoController")
                .Where(values => !IsVirtual(values))
                .Select(values => (
                    values.TryGetValue("PNPDeviceID", out var id) ? id : string.Empty,
                    values.TryGetValue("Name", out var name) ? name.Trim() : string.Empty))
                .Where(device => device.Item2.Length > 0)
                .ToArray();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            devices = [];
        }

        if (devices.Count == 0)
        {
            try { devices = plugAndPlay(); }
            catch (Exception) { devices = []; }
        }

        return devices.Select(device => Describe(device.InstanceId, device.Name, location)).ToArray();
    }

    /// <summary>Vendor from the instance ID; for Intel, the PCI position says whether it is the integrated GPU.</summary>
    public static DisplayAdapter Describe(string instanceId, string name, Func<string, PciLocation?> readPciLocation)
    {
        var vendor = HardwareVendors.FromInstanceId(instanceId);
        if (vendor is HardwareVendor.Unknown) vendor = HardwareVendors.FromName(name);
        bool? integrated = null;
        if (vendor is HardwareVendor.Intel && instanceId.Length > 0)
        {
            try
            {
                if (readPciLocation(instanceId) is { } pci) integrated = pci is { Bus: 0, Device: 2 };
            }
            catch (Exception) { /* Unknown position: the vendor rule decides. */ }
        }
        return new(name, vendor, integrated);
    }

    private static bool IsVirtual(IReadOnlyDictionary<string, string> values)
    {
        var pnpDeviceId = values.TryGetValue("PNPDeviceID", out var id) ? id : string.Empty;
        var name = values.TryGetValue("Name", out var adapterName) ? adapterName : string.Empty;

        return pnpDeviceId.StartsWith(@"ROOT\DISPLAY", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Virtual Display", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Remote Display", StringComparison.OrdinalIgnoreCase);
    }
}
