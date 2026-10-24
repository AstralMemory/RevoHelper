namespace RevoHelper.Core.Observation;

public sealed record ObservationChange(
    string Category,
    string Key,
    string? Before,
    string? After);

public sealed record ModeObservationDiff(
    string BeforeLabel,
    string AfterLabel,
    IReadOnlyList<ObservationChange> Changes);
