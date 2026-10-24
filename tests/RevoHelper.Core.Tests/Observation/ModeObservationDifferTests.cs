using RevoHelper.Core.Observation;

namespace RevoHelper.Core.Tests.Observation;

public sealed class ModeObservationDifferTests
{
    [Fact]
    public void Diff_OmitsUnchangedFieldsAndOrdersChangesDeterministically()
    {
        var before = Snapshot(
            label: "before",
            powerScheme: "Balanced",
            operatingMode: 1,
            services: new Dictionary<string, string> { ["GCUBridge"] = "Running" },
            hashes: new Dictionary<string, string> { ["MainOption.json"] = "AAA" });
        var after = Snapshot(
            label: "after",
            powerScheme: "High performance",
            operatingMode: 3,
            services: new Dictionary<string, string> { ["GCUBridge"] = "Running" },
            hashes: new Dictionary<string, string> { ["MainOption.json"] = "BBB" });

        var diff = ModeObservationDiffer.Diff(before, after);

        Assert.Equal(3, diff.Changes.Count);
        Assert.Collection(
            diff.Changes,
            change => Assert.Equal(("OEM", "OperatingMode", "1", "3"), Values(change)),
            change => Assert.Equal(("Power", "ActiveScheme", "Balanced", "High performance"), Values(change)),
            change => Assert.Equal(("ProfileHash", "MainOption.json", "AAA", "BBB"), Values(change)));
    }

    [Fact]
    public void Diff_ReportsAddedAndRemovedDictionaryValues()
    {
        var before = Snapshot(
            label: "before",
            powerScheme: null,
            operatingMode: null,
            services: new Dictionary<string, string> { ["Old"] = "Stopped" },
            hashes: new Dictionary<string, string>());
        var after = Snapshot(
            label: "after",
            powerScheme: null,
            operatingMode: null,
            services: new Dictionary<string, string> { ["New"] = "Running" },
            hashes: new Dictionary<string, string>());

        var diff = ModeObservationDiffer.Diff(before, after);

        Assert.Contains(diff.Changes, change =>
            change.Category == "Service" && change.Key == "New" && change.Before is null);
        Assert.Contains(diff.Changes, change =>
            change.Category == "Service" && change.Key == "Old" && change.After is null);
    }

    private static ModeObservationSnapshot Snapshot(
        string label,
        string? powerScheme,
        int? operatingMode,
        IReadOnlyDictionary<string, string> services,
        IReadOnlyDictionary<string, string> hashes) =>
        new(
            label,
            DateTimeOffset.UtcNow,
            powerScheme,
            IsAcOnline: true,
            operatingMode,
            OemSelections: new Dictionary<string, int?>(),
            ProfileHashes: hashes,
            ServiceStates: services,
            DeviceStates: new Dictionary<string, string>(),
            Telemetry: null);

    private static (string, string, string?, string?) Values(ObservationChange change) =>
        (change.Category, change.Key, change.Before, change.After);
}
