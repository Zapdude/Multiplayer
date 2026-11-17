using HarmonyLib;

namespace Multiplayer.Client.Persistent;

[HarmonyPatch(typeof(OnMainThread), "Update")]
internal static class PresentOwnedGravshipUi
{
	private static void Postfix()
	{
		GravshipUiOwnership.Pump();
	}
}
