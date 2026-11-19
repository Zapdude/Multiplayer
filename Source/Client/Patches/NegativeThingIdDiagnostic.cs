using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Verse;

namespace Multiplayer.Client.Patches;

/// <summary>
/// TEMPORARY diagnostic (2026-11-19). Logs the spawn stack for any spawned Thing whose thingIDNumber is
/// negative. Interface-created things receive local IDs from UniqueIdsPatch and must never reach a live
/// map; VGE2's landing preview (temp map, GeneratePawns) is the current suspect. Remove this file once
/// the spawn path is identified and fixed.
/// </summary>
[HarmonyPatch(typeof(Thing), nameof(Thing.SpawnSetup))]
static class NegativeThingIdDiagnostic
{
    // Flip to false to silence without removing the patch.
    private const bool Enabled = true;

    // One log per distinct spawn stack; a whole station map otherwise logs hundreds of lines.
    private static readonly HashSet<string> seen = new();

    static void Postfix(Thing __instance)
    {
        if (!Enabled || Multiplayer.Client == null || __instance?.def == null)
            return;

        if (!__instance.def.HasThingIDNumber || __instance.thingIDNumber >= 0)
            return;

        string stack = Environment.StackTrace;
        string[] frames = stack.Split('\n');
        string signature = string.Join("|", frames.Skip(3).Take(8).Select(f => f.Trim()));

        if (!seen.Add(signature))
            return;

        string mapId = __instance.Map != null ? __instance.Map.uniqueID.ToString() : "null";
        string contextMap = Multiplayer.MapContext != null ? Multiplayer.MapContext.uniqueID.ToString() : "null";

        Log.Warning(
            $"[MP negative id] {__instance.def.defName} id={__instance.thingIDNumber} " +
            $"map={mapId} pos={__instance.Position} inInterface={Multiplayer.InInterface} " +
            $"ticking={Multiplayer.Ticking} executingCmds={Multiplayer.ExecutingCmds} " +
            $"state={Multiplayer.Client.State} mapContext={contextMap}\n{stack}");
    }
}
