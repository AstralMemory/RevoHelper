using System.Management;

namespace RevoHelper.Hardware.Windows.Discovery;

public sealed class SystemManagementWmiQuery : IWmiQuery
{
    public IReadOnlyDictionary<string, string> ReadFirst(string className)
    {
        return ReadAll(className).FirstOrDefault()
            ?? throw new InvalidOperationException($"WMI class {className} returned no instances.");
    }

    public IReadOnlyList<IReadOnlyDictionary<string, string>> ReadAll(string className)
    {
        using var searcher = new ManagementObjectSearcher($"SELECT * FROM {className}");
        using var results = searcher.Get();

        return results
            .Cast<ManagementObject>()
            .Select(ReadProperties)
            .ToArray();
    }

    private static IReadOnlyDictionary<string, string> ReadProperties(ManagementObject item)
    {
        using (item)
        {
            return item.Properties
                .Cast<PropertyData>()
                .ToDictionary(
                    property => property.Name,
                    property => property.Value?.ToString()?.Trim() ?? string.Empty,
                    StringComparer.OrdinalIgnoreCase);
        }
    }
}
