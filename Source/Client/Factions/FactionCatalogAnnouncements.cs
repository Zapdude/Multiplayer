using Multiplayer.Common.Networking.Packet;
using RimWorld;
using Verse;

namespace Multiplayer.Client.Factions;

/// <summary>
/// Tells the server which player factions this client's world contains. The server only knows factions that were
/// in the last snapshot or were declared; a faction created after the join point has to be announced by the sync
/// reference for other players to be able to select it.
/// </summary>
internal static class FactionCatalogAnnouncements
{
    public static void Announce(Faction faction)
    {
        if (Multiplayer.Client == null || Multiplayer.IsReplay || TickPatch.Simulating) return;

        var game = Multiplayer.game;
        if (game?.gameComp?.multifaction != true) return;
        if (faction == null || !faction.IsPlayer) return;

        var worldComp = Multiplayer.WorldComp;
        if (worldComp == null || !worldComp.factionData.ContainsKey(faction.loadID)) return;

        Multiplayer.Client.Send(new ClientSetFactionPacket(-1, faction.loadID));
    }

    public static void AnnounceAll()
    {
        if (Find.FactionManager == null) return;

        foreach (var faction in Find.FactionManager.AllFactionsListForReading)
            Announce(faction);
    }
}
