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
}
