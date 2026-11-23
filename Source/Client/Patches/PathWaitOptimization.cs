using System;
using HarmonyLib;
using Unity.Jobs;
using Verse;

namespace Multiplayer.Client.Patches;

// ForceCompleteScheduledJobs is called every tick on a client. The vanilla implementation allocates an array for
// the handles; complete them from a reused per-thread buffer instead.
//
// 1.6 deviation from the 1.7 copy: that version uses Span<JobHandle> plus ArrayPool<JobHandle>. On net48 those
// names are ambiguous here, because Harmony 2.4.2 carries its own System.Memory polyfills in 0Harmony alongside
// mscorlib. A thread-static array keeps the allocation out of the steady state without either dependency.
[HarmonyPatch(typeof(PathFinder), "ForceCompleteScheduledJobs")]
internal static class PathWaitOptimization
{
    [ThreadStatic] private static JobHandle[] buffer;

    static bool Prefix(PathFinder __instance)
    {
        if (Multiplayer.Client == null) return true;

        CompleteScheduledJobs(__instance);
        return false;
    }

    internal static void CompleteScheduledJobs(PathFinder pathFinder)
    {
        var pathCount = pathFinder.scheduledPathJobs.Count;
        var gridCount = pathFinder.scheduledGridJobs.Count;
        var total = pathCount + gridCount;
        if (total == 0) return;

        if (buffer == null || buffer.Length < total)
            buffer = new JobHandle[total];

        for (var i = 0; i < pathCount; i++)
            buffer[i] = pathFinder.scheduledPathJobs[i].handle;

        for (var i = 0; i < gridCount; i++)
            buffer[pathCount + i] = pathFinder.scheduledGridJobs[i].handle;

        for (var i = 0; i < total; i++)
            buffer[i].Complete();
    }
}
