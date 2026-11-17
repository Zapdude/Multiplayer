using HarmonyLib;
using Verse;

namespace Multiplayer.Client.AsyncTime;

// Region and room rebuilds triggered from the interface run on the local player's frame, not inside a synced tick.
// They change shared map state at a moment other peers don't reproduce, so skip them and let the next tick do it.
[HarmonyPatch(typeof(RegionAndRoomUpdater), nameof(RegionAndRoomUpdater.TryRebuildDirtyRegionsAndRooms))]
internal static class NoRegionRebuildFromInterface
{
    static bool Prefix()
    {
        if (Multiplayer.Client == null) return true;
        return !Multiplayer.InInterface;
    }
}
