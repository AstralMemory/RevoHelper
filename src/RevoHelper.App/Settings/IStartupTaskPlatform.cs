using Microsoft.Win32;

namespace RevoHelper.App.Settings;

public interface IStartupTaskPlatform
{
    string CurrentUserSid { get; }
    string? ReadTaskXml();
    void RegisterTask(string xml);
    void RemoveTask();
    string? ReadLegacyRunCommand();
    bool IsLegacyRunDisabled();
    void RemoveLegacyRun();
    LegacyStartupState CaptureLegacyState();
    void RestoreLegacyState(LegacyStartupState state);
}

public sealed record StartupRegistryValue(RegistryValueKind Kind, object Value)
{
    public bool Matches(StartupRegistryValue? other) => other is not null && Kind == other.Kind &&
        (Value is byte[] bytes && other.Value is byte[] otherBytes ? bytes.SequenceEqual(otherBytes) :
         Value is string[] strings && other.Value is string[] otherStrings ? strings.SequenceEqual(otherStrings) :
         Equals(Value, other.Value));
}

public sealed record LegacyStartupState(
    StartupRegistryValue? Run,
    StartupRegistryValue? ApprovalRun,
    StartupRegistryValue? ApprovalRun32)
{
    public static LegacyStartupState Empty { get; } = new(null, null, null);
    public bool Matches(LegacyStartupState other) => Same(Run, other.Run) &&
        Same(ApprovalRun, other.ApprovalRun) && Same(ApprovalRun32, other.ApprovalRun32);
    public bool CanRestoreFrom(LegacyStartupState current) =>
        (current.Run is null || Same(Run, current.Run)) &&
        (current.ApprovalRun is null || Same(ApprovalRun, current.ApprovalRun)) &&
        (current.ApprovalRun32 is null || Same(ApprovalRun32, current.ApprovalRun32));
    private static bool Same(StartupRegistryValue? a, StartupRegistryValue? b) =>
        a is null ? b is null : a.Matches(b);
}
