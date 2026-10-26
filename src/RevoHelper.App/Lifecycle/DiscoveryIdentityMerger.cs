using RevoHelper.Core.Hardware;

namespace RevoHelper.App.Lifecycle;

/// <summary>Retains observed immutable machine fields across a transient rediscovery failure.</summary>
public static class DiscoveryIdentityMerger
{
    public static MachineIdentity Merge(MachineIdentity? previouslyObserved, MachineIdentity current)
    {
        if (previouslyObserved is null || !IsKnown(previouslyObserved.Manufacturer)) return current;
        if (IsKnown(current.Manufacturer) && !current.Manufacturer.Trim().Equals(
                previouslyObserved.Manufacturer.Trim(), StringComparison.OrdinalIgnoreCase)) return current;

        return current with
        {
            Manufacturer = IsKnown(current.Manufacturer) ? current.Manufacturer : previouslyObserved.Manufacturer,
            ProductName = PreferKnown(current.ProductName, previouslyObserved.ProductName),
            BaseboardProduct = PreferKnown(current.BaseboardProduct, previouslyObserved.BaseboardProduct),
            ProcessorName = PreferKnown(current.ProcessorName, previouslyObserved.ProcessorName),
            BiosVersion = PreferKnown(current.BiosVersion, previouslyObserved.BiosVersion),
            ProcessorCoreCount = current.ProcessorCoreCount > 0 ? current.ProcessorCoreCount : previouslyObserved.ProcessorCoreCount,
            ProcessorThreadCount = current.ProcessorThreadCount > 0 ? current.ProcessorThreadCount : previouslyObserved.ProcessorThreadCount,
            ProcessorIdentity = current.Cpu.Vendor is CpuVendor.Unknown ? previouslyObserved.Cpu : current.Cpu
        };
    }

    private static string PreferKnown(string current, string previous) =>
        string.IsNullOrWhiteSpace(current) ? previous : current;

    private static bool IsKnown(string manufacturer) => !string.IsNullOrWhiteSpace(manufacturer) &&
        !manufacturer.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase);
}
