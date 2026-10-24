using RevoHelper.Core.Control;

namespace RevoHelper.App.Settings;

/// <summary>Safe default for non-runtime composition; imports/exports remain explicit file operations.</summary>
public sealed class MemoryLightingProfileStore : ILightingProfileStore
{
    private LightingProfile? profile;
    public LightingProfile? Load() => profile;
    public void Save(LightingProfile value) => profile = value;
    public LightingProfile Import(string path) => new JsonLightingProfileStore(path).Import(path);
    public void Export(string path, LightingProfile value) => new JsonLightingProfileStore(path).Export(path, value);
}
