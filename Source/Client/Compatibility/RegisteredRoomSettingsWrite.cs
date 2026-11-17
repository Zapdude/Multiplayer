using HarmonyLib;
using Verse;

namespace Multiplayer.Client.Compatibility;

// Room settings owned by a registration are applied from the session's copy, never written back
// as the local player's personal settings while a session is running.
[HarmonyPatch(typeof(ModSettings), nameof(ModSettings.Write))]
internal static class RegisteredRoomSettingsWrite
{
    static bool Prefix(ModSettings __instance) => RoomSettings.AllowPersonalWrite(__instance);
}
