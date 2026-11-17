using HarmonyLib;
using RimWorld;
using Verse;

namespace Multiplayer.Client.Persistent;

[HarmonyPatch(typeof(GravshipLandingMarker), nameof(GravshipLandingMarker.GravshipRotation), MethodType.Getter)]
internal static class ReadOwnedGravshipRotation
{
	private static bool Prefix(GravshipLandingMarker __instance, ref Rot4 __result)
	{
		if (!GravshipUiOwnership.TryReadRotation(__instance, out var rotation))
		{
			return true;
		}
		__result = rotation;
		return false;
	}
}
