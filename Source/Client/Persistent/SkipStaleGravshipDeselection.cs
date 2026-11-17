using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Multiplayer.Client.Persistent;

[HarmonyPatch(typeof(Designator_MoveGravship), "Deselected")]
internal static class SkipStaleGravshipDeselection
{
	private static bool Prefix(Designator_MoveGravship __instance)
	{
		if (Multiplayer.Client != null && Multiplayer.InInterface)
		{
			GravshipLandingMarker marker = __instance.marker;
			object engine;
			if (marker == null)
			{
				engine = null;
			}
			else
			{
				Gravship gravship = marker.gravship;
				engine = ((gravship != null) ? gravship.Engine : null);
			}
			if (GravshipUiOwnership.IsLocalOwner((Building_GravEngine)engine))
			{
				return GravshipUiOwnership.IsCurrentLanding(Find.GravshipController, __instance.marker);
			}
			return false;
		}
		return true;
	}
}
