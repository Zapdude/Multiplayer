using System;
using System.Collections.Generic;
using Multiplayer.Common;
using RimWorld;
using Verse;

namespace Multiplayer.Client.Compatibility;

// Startup settings some mods read once while loading. Their loaded value is part of the def fingerprint, so the
// host has to confirm the current value still matches what this instance started with before anyone joins.
public static class StartupSettings
{
    private sealed record Entry(Func<int> Loaded, Func<int> Current);

    public const int Version = 1;

    private static readonly SortedDictionary<string, Entry> entries = new(StringComparer.Ordinal);

    public static void Register(string name, Func<int> loaded, Func<int> current)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A name is required", nameof(name));
        if (loaded == null) throw new ArgumentNullException(nameof(loaded));
        if (current == null) throw new ArgumentNullException(nameof(current));

        entries.Add(name, new Entry(loaded, current));
    }

    internal static void AddDefInfos(Dictionary<string, DefInfo> infos)
    {
        foreach (var (name, entry) in entries)
            infos.Add("Startup settings: " + name, new DefInfo { count = 1, hash = entry.Loaded() });
    }

    internal static bool HostCanUseLoadedSettings()
    {
        foreach (var (name, entry) in entries)
        {
            if (entry.Loaded() == entry.Current()) continue;

            Messages.Message(
                "MpStartupSettingsRestart".Translate(name),
                MessageTypeDefOf.RejectInput,
                historical: false);
            return false;
        }

        return true;
    }
}
