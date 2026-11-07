namespace RevoHelper.App.Settings;

public interface IStartupRegistration
{
    bool IsEnabled();

    bool HasEnabledRegistration() => IsEnabled();

    /// <summary>True when the registered program exists and is a newer version than this one.</summary>
    bool RegisteredExecutableIsNewer() => false;

    void SetEnabled(bool enabled);

    StartupRegistrationState CaptureState() => new(IsEnabled());

    void RestoreState(StartupRegistrationState state) => SetEnabled(state.Enabled);
}

public record StartupRegistrationState(bool Enabled);
