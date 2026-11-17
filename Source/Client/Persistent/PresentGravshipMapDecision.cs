using HarmonyLib;

namespace Multiplayer.Client.Persistent;

[HarmonyPatch(typeof(OnMainThread), "Update")]
internal static class PresentGravshipMapDecision
{
	private static void Postfix()
	{
		GravshipMapDecisions.Pump();
	}
}
