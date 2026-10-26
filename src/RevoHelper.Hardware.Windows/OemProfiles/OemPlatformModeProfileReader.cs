using System.Text.Json;
using RevoHelper.Core.Control;
using RevoHelper.Core.Hardware;

namespace RevoHelper.Hardware.Windows.OemProfiles;

public sealed class OemPlatformModeProfileReader(string profileDirectory)
{
    public OemRuntimeEvidence ReadEvidence()
    {
        var modes = new HashSet<PlatformMode>();
        if (!Directory.Exists(profileDirectory)) return OemRuntimeEvidence.None;

        foreach (var path in Directory.EnumerateFiles(profileDirectory, "Mode*_Profile*.json"))
        {
            if (!OemProfileFileName.TryParse(path, out _, out var mode)) continue;
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                if (document.RootElement.ValueKind == JsonValueKind.Object)
                    modes.Add(mode);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                // An unreadable or malformed file is not evidence, but it must
                // not suppress independently readable mode profiles.
            }
        }

        // File presence proves a base mode only. No power range, CPU write
        // capability, or Turbo sub-strategy is inferred from this inventory.
        return new(modes, false, false, false, false, false);
    }
}
