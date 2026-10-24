namespace RevoHelper.Hardware.Windows.Discovery;

public sealed class WindowsDisplayAdapterReader(IWmiQuery query)
{
    public IReadOnlyList<string> ReadNames() =>
        query.ReadAll("Win32_VideoController")
            .Where(values => !IsVirtual(values))
            .Select(values => values.TryGetValue("Name", out var name) ? name.Trim() : string.Empty)
            .Where(name => name.Length > 0)
            .ToArray();

    private static bool IsVirtual(IReadOnlyDictionary<string, string> values)
    {
        var pnpDeviceId = values.TryGetValue("PNPDeviceID", out var id) ? id : string.Empty;
        var name = values.TryGetValue("Name", out var adapterName) ? adapterName : string.Empty;

        return pnpDeviceId.StartsWith(@"ROOT\DISPLAY", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Virtual Display", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Remote Display", StringComparison.OrdinalIgnoreCase);
    }
}
