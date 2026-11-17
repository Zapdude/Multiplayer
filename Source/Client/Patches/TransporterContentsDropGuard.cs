using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Multiplayer.Client;

// The transporter contents tab drops things by index into the container. A synced command can arrive with a
// stale selection or stack count, which would drop the wrong thing or more than exists. Reject the drop.
[HarmonyPatch(typeof(ITab_ContentsTransporter), "OnDropThing")]
public static class TransporterContentsDropGuard
{
    static bool Prefix(Thing t, ref int count)
    {
        if (Multiplayer.Client == null || !Multiplayer.ExecutingCmds) return true;

        var transporter = Find.Selector.SingleSelectedThing != null
            ? Find.Selector.SingleSelectedThing.TryGetComp<CompTransporter>()
            : null;

        if (!CanDrop(transporter, t, count, Find.CurrentMap))
            return false;

        count = Math.Min(count, t.stackCount);
        return true;
    }

    public static bool CanDrop(CompTransporter transporter, Thing thing, int count, Map selectedMap)
    {
        var parent = transporter?.parent;
        if (parent == null || !parent.Spawned || parent.Destroyed || parent.Map != selectedMap) return false;
        if (selectedMap == null || thing == null || thing.Destroyed || thing.stackCount <= 0 || count <= 0)
            return false;

        var container = transporter.innerContainer;
        return container != null && container.Contains(thing);
    }
}
