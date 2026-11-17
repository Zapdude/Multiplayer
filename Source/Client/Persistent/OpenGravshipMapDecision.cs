using System.Collections.Generic;
using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace Multiplayer.Client.Persistent;

[HarmonyPatch(typeof(MapParent), "GetGizmos")]
internal static class OpenGravshipMapDecision
{
	private static void Postfix(MapParent __instance, ref IEnumerable<Gizmo> __result)
	{
		__result = GravshipMapDecisions.Gizmos(__instance, __result);
	}
}
