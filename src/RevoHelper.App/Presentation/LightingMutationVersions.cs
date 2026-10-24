using RevoHelper.Core.Control;

namespace RevoHelper.App.Presentation;

/// <summary>Latest-user-choice ordering within a physical zone, never across zones.</summary>
public sealed class LightingMutationVersions
{
    private readonly object sync = new();
    private readonly Dictionary<LightingZone, long> versions = [];
    private long sequence;

    public long Reserve(LightingZone zone)
    {
        lock (sync) return versions[zone] = ++sequence;
    }

    public bool IsCurrent(LightingZone zone, long version)
    {
        lock (sync) return versions.TryGetValue(zone, out var current) && current == version;
    }
}
