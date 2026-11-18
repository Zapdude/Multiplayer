using System.Collections.Generic;
using System.Linq;
using Multiplayer.Common.Networking.Packet;

namespace Multiplayer.Common;

/// <summary>
/// Which player factions exist in the current world. Seeded from the faction ids uploaded with each snapshot and
/// extended by declarations from the sync reference, so the server never parses the save.
/// </summary>
public sealed class FactionCatalog(WorldData world)
{
    private readonly HashSet<int> factions = new();
    private readonly Dictionary<int, int> declarations = new(); // faction id -> game timer of the declaration
    private readonly Dictionary<int, (ServerPlayer player, int faction)> pending = new();

    private int[] applied;

    private void Refresh()
    {
        var snapshot = world.snapshotFactionIds ?? [];
        if (ReferenceEquals(snapshot, applied)) return;
        applied = snapshot;

        factions.Clear();
        foreach (var id in snapshot)
            if (id >= 0) factions.Add(id);

        // Declarations newer than the snapshot are factions created after it; older ones are stale unless the
        // snapshot already contains them
        foreach (var (id, tick) in declarations.ToArray())
        {
            if (factions.Contains(id) || tick >= world.CompletedJoinPointTick)
                factions.Add(id);
            else
                declarations.Remove(id);
        }
    }

    public bool Contains(int factionId)
    {
        if (factionId < 0) return false;

        Refresh();

        if (factionId == world.hostFactionId) return true;

        if (!world.Server.settings.multifaction) return false;
        if (factionId == world.spectatorFactionId) return true;

        return factions.Contains(factionId);
    }

    /// <summary>A player faction was created and announced by the sync reference.</summary>
    public void Register(ServerPlayer source, int factionId)
    {
        if (!world.Server.settings.multifaction || factionId < 0) return;
        if (source != world.Server.playerManager.SyncReference) return;

        Refresh();
        factions.Add(factionId);
        declarations[factionId] = world.Server.gameTimer;

        ApplyPending();
    }

    /// <summary>A player asks to be moved to a faction; it is queued if that faction is not known yet.</summary>
    public void Request(ServerPlayer player, int factionId)
    {
        // Outside multifaction there are no owner factions to validate against; keep the existing direct switch
        if (!world.Server.settings.multifaction)
        {
            Apply(player, factionId);
            return;
        }

        PrunePending();
        pending.Remove(player.id);

        if (Contains(factionId))
            Apply(player, factionId);
        else if (factionId >= 0)
            pending[player.id] = (player, factionId);
    }

    private void ApplyPending()
    {
        foreach (var (id, request) in pending.ToArray())
        {
            if (!Contains(request.faction)) continue;

            pending.Remove(id);
            Apply(request.player, request.faction);
        }
    }

    private void PrunePending()
    {
        foreach (var id in pending.Keys.ToArray())
        {
            if (world.Server.GetPlayer(id) != pending[id].player)
                pending.Remove(id);
        }
    }

    private void Apply(ServerPlayer player, int factionId)
    {
        if (player.FactionId == factionId) return;

        player.FactionId = factionId;
        world.Server.SendToPlaying(new ServerSetFactionPacket(player.id, factionId));
    }
}
