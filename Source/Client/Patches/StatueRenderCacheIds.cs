using HarmonyLib;
using RimWorld;
using Verse;

namespace Multiplayer.Client.Patches;

// Statue fake-pawn generation consumes RNG and unique ids for rendering data. Keep it on a local scope so it
// neither advances the shared RNG stream nor assigns synced ids.
[HarmonyPatch(typeof(CompStatue), nameof(CompStatue.InitFakePawn))]
public static class StatueRenderCacheIds
{
    public struct CacheState
    {
        public bool active;
        public bool previousLocalIds;
        public bool previousDontSync;
    }

    [HarmonyPriority(800)]
    private static void Prefix(out CacheState __state)
    {
        __state = default;
        if (Multiplayer.Client == null) return;

        __state.previousLocalIds = UniqueIdsPatch.useLocalIdsOverride;
        __state.previousDontSync = Multiplayer.dontSync;
        Rand.PushState();
        __state.active = true;
        UniqueIdsPatch.useLocalIdsOverride = true;
        Multiplayer.dontSync = true;
    }

    [HarmonyPriority(0)]
    private static void Finalizer(CacheState __state)
    {
        if (!__state.active) return;

        try
        {
            Rand.PopState();
        }
        finally
        {
            UniqueIdsPatch.useLocalIdsOverride = __state.previousLocalIds;
            Multiplayer.dontSync = __state.previousDontSync;
        }
    }
}
