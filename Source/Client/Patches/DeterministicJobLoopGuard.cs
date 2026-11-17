using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace Multiplayer.Client.Patches;

// Vanilla stops a pawn from starting more than ten jobs in one frame. Frames differ between peers, so the guard
// trips at different moments and the job streams diverge. Count job starts per simulation event instead: the
// same event has the same count on every peer. The original frame marker is cleared so vanilla never trips it.
[HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
internal static class DeterministicJobLoopGuard
{
    private sealed class Counter
    {
        public long key = long.MinValue;
        public int count;
    }

    private const int MaxJobsPerEvent = 10;

    private static readonly ConditionalWeakTable<Pawn_JobTracker, Counter> counters = new();
    private static long outOfTickKey = long.MinValue + 1;

    [HarmonyPriority(-2)]
    static bool Prefix(Pawn_JobTracker __instance, Job newJob, ThinkNode jobGiver, bool addToJobsThisTick)
    {
        if (Multiplayer.Client == null || !addToJobsThisTick)
            return true;

        __instance.lastJobGivenAtFrame = int.MinValue;

        var counter = counters.GetValue(__instance, _ => new Counter());
        var key = CurrentEventKey();

        if (counter.key == key)
            counter.count++;
        else
        {
            counter.key = key;
            counter.count = 0;
        }

        if (counter.count <= MaxJobsPerEvent)
            return true;

        counter.count = 0;

        var pawn = __instance.pawn;
        pawn.ClearReservationsForJob(newJob);
        JobUtility.TryStartErrorRecoverJob(
            pawn,
            $"{Gen.ToStringSafe(pawn)} started {MaxJobsPerEvent} jobs in one tick. " +
            $"newJob={Gen.ToStringSafe(newJob)} jobGiver={Gen.ToStringSafe(jobGiver)}");

        return false;
    }

    private static long CurrentEventKey()
    {
        if (!Multiplayer.Ticking && !Multiplayer.ExecutingCmds)
            return outOfTickKey++;

        var map = Multiplayer.MapContext;
        if (map != null)
            return ((long)map.uniqueID << 32) | (uint)map.AsyncTime().eventCount;

        return -4294967296L | (uint)Multiplayer.AsyncWorldTime.worldTicks;
    }
}
