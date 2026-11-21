using System.Reflection;
using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace Multiplayer.Client.Patches;

// Vanilla regenerates dirty world layers in a "GeneratingPlanet" long event and keeps asynchronousRegenerationActive
// set until that event finishes. Multiplayer clears queued long events when a session stops, so a regeneration queued
// right before hosting (for example from the main menu's world background) can be dropped, leaving the flag set.
// Every later DrawWorldLayers then logs "Called DrawWorldLayers() but already regenerating" (#641), which is what
// happens when loading a gravship save: its map renders the world in the background.
//
// While an event is pending or running, skip the draw; vanilla would only log the error. If no event is left, finish
// the regeneration synchronously instead of logging on every frame.
[HarmonyPatch(typeof(WorldRenderer), nameof(WorldRenderer.DrawWorldLayers))]
static class WorldRendererStaleRegenerationPatch
{
    private static readonly FieldInfo RegenerationActiveField =
        AccessTools.Field(typeof(WorldRenderer), "asynchronousRegenerationActive");

    private static readonly AccessTools.FieldRef<WorldRenderer, bool> RegenerationActive =
        RegenerationActiveField == null ? null : AccessTools.FieldRefAccess<WorldRenderer, bool>(RegenerationActiveField);

    static bool Prefix(WorldRenderer __instance)
    {
        // A different game version without the field: leave vanilla alone rather than failing to start
        if (RegenerationActive == null || !RegenerationActive(__instance))
            return true;

        if (LongEventHandler.AnyEventNowOrWaiting)
            return false;

        RegenerationActive(__instance) = false;
        __instance.RegenerateAllLayersNow();
        return true;
    }
}
