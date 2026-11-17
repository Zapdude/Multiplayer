using System;
using System.Buffers;
using HarmonyLib;
using Unity.Jobs;
using Verse;

namespace Multiplayer.Client.Patches;

// ForceCompleteScheduledJobs is called every tick on a client. The vanilla implementation allocates a native
// array for the handles; complete them from a span instead.
[HarmonyPatch(typeof(PathFinder), "ForceCompleteScheduledJobs")]
internal static class PathWaitOptimization
{
    private const int StackHandleLimit = 128;

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

        JobHandle[] rented = null;
        Span<JobHandle> handles = total > StackHandleLimit
            ? (rented = ArrayPool<JobHandle>.Shared.Rent(total)).AsSpan(0, total)
            : stackalloc JobHandle[total];

        try
        {
            for (var i = 0; i < pathCount; i++)
                handles[i] = pathFinder.scheduledPathJobs[i].handle;

            for (var i = 0; i < gridCount; i++)
                handles[pathCount + i] = pathFinder.scheduledGridJobs[i].handle;

            for (var i = 0; i < handles.Length; i++)
                handles[i].Complete();
        }
        finally
        {
            if (rented != null)
                ArrayPool<JobHandle>.Shared.Return(rented);
        }
    }
}
