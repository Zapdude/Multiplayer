using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Multiplayer.Client.AsyncTime;

// The incident queue is global but each map has its own clock. Vanilla fires whatever is due at the current
// TickManager value, so a map's tick can fire incidents queued for another map (or the world) at the wrong time.
// Only give vanilla the incidents that belong to the clock being ticked; the rest are put back in their original
// positions afterwards, with vanilla's own firing and retry logic intact.
[HarmonyPatch(typeof(IncidentQueue), nameof(IncidentQueue.IncidentQueueTick))]
internal static class IncidentQueueClock
{
    private static List<QueuedIncident> original;
    private static Map filteredFor;

    static bool Prefix(IncidentQueue __instance)
    {
        if (Multiplayer.Client == null || Multiplayer.game == null || !Multiplayer.GameComp.asyncTime)
            return true;

        var tickingMap = AsyncTimeComp.tickingMap;
        if (tickingMap == null && !AsyncWorldTimeComp.tickingWorld)
            return false;

        // A nested call would lose the outer stash; let vanilla run unfiltered instead
        if (original != null) return true;

        var before = __instance.queuedIncidents.ToList();
        var forThisClock = before.Where(qi => TargetMap(qi) == tickingMap).ToList();
        if (forThisClock.Count == 0) return false;

        original = before;
        filteredFor = tickingMap;

        __instance.queuedIncidents.Clear();
        __instance.queuedIncidents.AddRange(forThisClock);
        return true;
    }

    static void Finalizer(IncidentQueue __instance)
    {
        if (original == null) return;

        var before = original;
        var forMap = filteredFor;
        original = null;
        filteredFor = null;

        // Vanilla may have removed fired incidents from the filtered list, keep that and re-insert the others
        var remaining = new HashSet<QueuedIncident>(__instance.queuedIncidents);
        var merged = new List<QueuedIncident>(before.Count);

        foreach (var qi in before)
        {
            if (TargetMap(qi) == forMap)
            {
                if (remaining.Contains(qi))
                    merged.Add(qi);
            }
            else
            {
                merged.Add(qi);
            }
        }

        __instance.queuedIncidents.Clear();
        __instance.queuedIncidents.AddRange(merged);
    }

    internal static Map TargetMap(QueuedIncident qi)
    {
        if (qi == null || qi.FiringIncident == null || qi.FiringIncident.parms?.target is not Map map)
            return null;

        return map.AsyncTime() != null ? map : null;
    }

    internal static int ClockOf(QueuedIncident qi)
    {
        return TargetMap(qi)?.AsyncTime().mapTicks ?? Multiplayer.AsyncWorldTime.worldTicks;
    }
}
