using HarmonyLib;
using RimWorld;
using Verse;

namespace Multiplayer.Client;

// While Multiplayer writes its own exposables (join point, session data), shared references are serialized from
// the shared directory. The game's duplicate load-id checker would report those as registered twice.
[HarmonyPatch(typeof(DebugLoadIDsSavingErrorsChecker), "RegisterReferenced")]
internal static class SharedReferenceSavingCheck
{
    static bool Prefix(ILoadReferenceable obj)
    {
        if (!ScribeUtil.writingExposable || Multiplayer.game == null || obj == null)
            return true;

        return !ScribeUtil.sharedCrossRefs.allObjectsByLoadID.TryGetValue(obj.GetUniqueLoadID(), out var existing)
               || obj != existing;
    }
}
