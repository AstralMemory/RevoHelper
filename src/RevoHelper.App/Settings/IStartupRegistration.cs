namespace RevoHelper.App.Settings;

public interface IStartupRegistration
{
    bool IsEnabled();

    bool HasEnabledRegistration() => IsEnabled();

    void SetEnabled(bool enabled);
}
