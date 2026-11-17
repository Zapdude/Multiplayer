using HarmonyLib;
using RimWorld;
using Verse;

namespace Multiplayer.Client.AsyncTime;

// Vanilla schedules an incident relative to the current TickManager value, but the queue is global. Rebase the
// due tick onto the target's own clock so it fires when that map (or the world) reaches it.
[HarmonyPatch(typeof(IncidentQueue), nameof(IncidentQueue.Add), new[] { typeof(QueuedIncident) })]
internal static class IncidentQueueAddOnTargetClock
{
    static void Prefix(QueuedIncident qi)
    {
        if (Multiplayer.Client == null || Multiplayer.game == null || !Multiplayer.GameComp.asyncTime)
            return;
        if (qi?.FiringIncident?.parms == null)
            return;

        qi.fireTick += IncidentQueueClock.ClockOf(qi) - Find.TickManager.TicksGame;
    }
}
