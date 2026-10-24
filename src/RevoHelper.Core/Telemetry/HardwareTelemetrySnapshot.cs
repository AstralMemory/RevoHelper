namespace RevoHelper.Core.Telemetry;

public sealed record HardwareTelemetrySnapshot(
    DateTimeOffset CapturedAt,
    IReadOnlyList<DeviceTelemetry> Devices);
