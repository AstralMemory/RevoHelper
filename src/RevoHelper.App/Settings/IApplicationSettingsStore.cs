namespace RevoHelper.App.Settings;

public interface IApplicationSettingsStore
{
    ApplicationSettings Load();

    void Save(ApplicationSettings settings);
}
