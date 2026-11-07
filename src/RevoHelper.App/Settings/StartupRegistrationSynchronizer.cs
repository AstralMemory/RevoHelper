namespace RevoHelper.App.Settings;

/// <summary>
/// Keeps an enabled startup entry on the newest copy after an update, so it neither keeps
/// launching an old version nor breaks when the old version is deleted. An entry the user
/// turned off stays off, and opening an older copy does not take the entry back.
/// </summary>
public static class StartupRegistrationSynchronizer
{
    public static bool Synchronize(IStartupRegistration registration)
    {
        if (registration.IsEnabled()) return true;
        if (!registration.HasEnabledRegistration()) return false;
        if (registration.RegisteredExecutableIsNewer()) return false;

        registration.SetEnabled(true);
        return registration.IsEnabled();
    }
}
