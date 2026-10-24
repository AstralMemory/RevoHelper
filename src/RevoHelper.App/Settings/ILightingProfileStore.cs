using RevoHelper.Core.Control;

namespace RevoHelper.App.Settings;

public interface ILightingProfileStore
{
    LightingProfile? Load();
    void Save(LightingProfile profile);
    LightingProfile Import(string path);
    void Export(string path, LightingProfile profile);
}
