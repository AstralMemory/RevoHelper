namespace RevoHelper.App.Settings;

public static class StartupRegistrationSynchronizer
{
    public static bool Synchronize(IStartupRegistration registration)
    {
        if (registration.IsEnabled()) return true;
        if (!registration.HasEnabledRegistration()) return false;

        registration.SetEnabled(true);
        return registration.IsEnabled();
    }
}
