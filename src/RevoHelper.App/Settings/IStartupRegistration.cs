namespace RevoHelper.App.Settings;

public interface IStartupRegistration
{
    bool IsEnabled();

    bool HasEnabledRegistration() => IsEnabled();

    void SetEnabled(bool enabled);

    StartupRegistrationState CaptureState() => new(IsEnabled());

    void RestoreState(StartupRegistrationState state) => SetEnabled(state.Enabled);
}

public record StartupRegistrationState(bool Enabled);
