using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Multiplayer.Client.Persistent;

[HarmonyPatch(typeof(GravshipLandingMarker), nameof(GravshipLandingMarker.GravshipRotation), MethodType.Setter)]
internal static class SyncOwnedGravshipRotation
{
	private static bool Prefix(GravshipLandingMarker __instance, Rot4 value)
	{
		if (Multiplayer.Client == null || !Multiplayer.InInterface)
		{
			return true;
		}
		Gravship gravship = __instance.gravship;
		if (GravshipUiOwnership.IsLocalOwner((gravship != null) ? gravship.Engine : null) && GravshipUiOwnership.IsCurrentLanding(Find.GravshipController, __instance))
		{
			GravshipUiOwnership.RequestRotation(__instance, value);
		}
		return false;
	}
}
