using HarmonyLib;
using Verse;

namespace Multiplayer.Client.Persistent;

[HarmonyPatch(typeof(DesignatorManager), "Select")]
internal static class TrackOwnedGravshipDesignator
{
	private static void Postfix(Designator des)
	{
		GravshipUiOwnership.TrackLandingDesignator(des);
	}
}
