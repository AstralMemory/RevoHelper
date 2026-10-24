using RevoHelper.Core.Telemetry;

namespace RevoHelper.Core.Observation;

public sealed record ModeObservationSnapshot(
    string Label,
    DateTimeOffset CapturedAt,
    string? ActivePowerScheme,
    bool? IsAcOnline,
    int? OemOperatingMode,
    IReadOnlyDictionary<string, int?> OemSelections,
    IReadOnlyDictionary<string, string> ProfileHashes,
    IReadOnlyDictionary<string, string> ServiceStates,
    IReadOnlyDictionary<string, string> DeviceStates,
    HardwareTelemetrySnapshot? Telemetry);
