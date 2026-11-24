using System.Collections.Generic;
using System.Linq;

namespace Multiplayer.Client;

// One settings file of one mod. A mod can register several settings classes, so this is a list
// element rather than a single per-mod value.
public record ModConfig(string ModId, string FileName, string Contents)
{
    // Mods write their settings with the platform's line endings, the same settings shouldn't be a mismatch
    public string ComparableContents => (Contents ?? "").Replace("\r\n", "\n").Replace('\r', '\n');

    public ModConfig Comparable() => this with { Contents = ComparableContents };
}

public static class ModConfigs
{
    // Both sides must describe the same set of files. The wire carries every syncable config for a mod,
    // and the local side enumerates every settings class, so the two sets are directly comparable.
    public static bool Match(IEnumerable<ModConfig> remote, IEnumerable<ModConfig> local) =>
        remote.Select(c => c.Comparable()).ToHashSet()
            .SetEquals(local.Select(c => c.Comparable()));
}
