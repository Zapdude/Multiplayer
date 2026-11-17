using HarmonyLib;
using Verse;

namespace Multiplayer.Client.Persistent;

[HarmonyPatch(typeof(WindowStack), "Add")]
internal static class TrackOwnedGravshipWindow
{
	private static void Postfix(Window window)
	{
		GravshipUiOwnership.TrackWindow(window);
	}
}
