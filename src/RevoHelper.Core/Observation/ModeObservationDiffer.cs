using System.Globalization;

namespace RevoHelper.Core.Observation;

public static class ModeObservationDiffer
{
    public static ModeObservationDiff Diff(
        ModeObservationSnapshot before,
        ModeObservationSnapshot after)
    {
        var changes = new List<ObservationChange>();
        Add(changes, "Power", "ActiveScheme", before.ActivePowerScheme, after.ActivePowerScheme);
        Add(changes, "Power", "ACOnline", Format(before.IsAcOnline), Format(after.IsAcOnline));
        Add(changes, "OEM", "OperatingMode", Format(before.OemOperatingMode), Format(after.OemOperatingMode));
        AddDictionary(changes, "OEM", before.OemSelections, after.OemSelections, Format);
        AddDictionary(changes, "ProfileHash", before.ProfileHashes, after.ProfileHashes, value => value);
        AddDictionary(changes, "Service", before.ServiceStates, after.ServiceStates, value => value);
        AddDictionary(changes, "Device", before.DeviceStates, after.DeviceStates, value => value);

        return new(
            before.Label,
            after.Label,
            changes
                .OrderBy(change => change.Category, StringComparer.Ordinal)
                .ThenBy(change => change.Key, StringComparer.Ordinal)
                .ToArray());
    }

    private static void Add(
        ICollection<ObservationChange> changes,
        string category,
        string key,
        string? before,
        string? after)
    {
        if (!string.Equals(before, after, StringComparison.Ordinal))
        {
            changes.Add(new(category, key, before, after));
        }
    }

    private static void AddDictionary<T>(
        ICollection<ObservationChange> changes,
        string category,
        IReadOnlyDictionary<string, T> before,
        IReadOnlyDictionary<string, T> after,
        Func<T, string?> format)
    {
        foreach (var key in before.Keys.Union(after.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var hasBefore = before.TryGetValue(key, out var beforeValue);
            var hasAfter = after.TryGetValue(key, out var afterValue);
            Add(
                changes,
                category,
                key,
                hasBefore ? format(beforeValue!) : null,
                hasAfter ? format(afterValue!) : null);
        }
    }

    private static string? Format(bool? value) => value?.ToString(CultureInfo.InvariantCulture);

    private static string? Format(int? value) => value?.ToString(CultureInfo.InvariantCulture);
}
