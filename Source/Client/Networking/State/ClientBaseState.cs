using System.Linq;
using Multiplayer.Client.Networking;
using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;

namespace Multiplayer.Client;

public abstract class ClientBaseState(ConnectionBase connection) : MpConnectionState(connection)
{
    protected MultiplayerSession Session => Multiplayer.session;

    [TypedPacketHandler]
    public void HandleKeepAlive(ServerKeepAlivePacket packet)
    {
        int ticksBehind = TickPatch.tickUntil - TickPatch.Timer;

        connection.Send(new ClientKeepAlivePacket(packet.id, ticksBehind, TickPatch.Simulating, TickPatch.workTicks),
            false);
    }

    [TypedPacketHandler]
    public void HandleTimeControl(ServerTimeControlPacket packet)
    {
        if (Multiplayer.session.remoteTickUntil >= packet.tickUntil) return;

        TickPatch.serverTimePerTick = packet.serverTimePerTick;
        Multiplayer.session.remoteTickUntil = packet.tickUntil;
        Multiplayer.session.remoteSentCmds = packet.sentCmds;
        Multiplayer.session.ProcessTimeControl();
    }

    // Handled in every state: it can arrive while rejoining
    [TypedPacketHandler]
    public void HandleSyncReference(ServerSyncReferencePacket packet)
    {
        Multiplayer.session.isSyncReference = packet.isReference;

        // The sync reference is the authority for the factions in the world; announce them when it changes
        if (packet.isReference)
            Factions.FactionCatalogAnnouncements.AnnounceAll();
    }

    // The server can send the player list before the world data finishes downloading. Applying it
    // from the base state lets the loading/joining/playing states all see it; it only touches the
    // session player list, so it is safe before the game is loaded.
    [TypedPacketHandler]
    public void HandlePlayerList(ServerPlayerListPacket packet) => ApplyPlayerList(packet);

    public static void ApplyPlayerList(ServerPlayerListPacket packet)
    {
        if (Multiplayer.session == null)
        {
            ServerLog.Error($"[MP] Player list ({packet.action}) received with no session; dropping it.");
            return;
        }

        if (packet.players == null &&
            (packet.action == PlayerListAction.Add || packet.action == PlayerListAction.List))
        {
            ServerLog.Error($"[MP] Player list ({packet.action}) has no player array; dropping it.");
            return;
        }

        if (packet.action == PlayerListAction.Add)
        {
            foreach (var info in packet.players)
            {
                if (!Multiplayer.session.players.Any(p => p.id == info.id || p.username == info.username))
                {
                    ServerLog.Log($"PlayerList: Adding player {info.id}:{info.username}");
                    Multiplayer.session.players.Add(PlayerInfo.FromNet(info));
                }
                else
                {
                    ServerLog.Error($"PlayerList: Adding player {info.id}:{info.username} - player already exists");
                }
            }
        }
        else if (packet.action == PlayerListAction.Remove)
        {
            ServerLog.Log($"PlayerList: Removing player with id {packet.playerId}");
            var matches = Multiplayer.session.players.RemoveAll(p => p.id == packet.playerId);
            if (matches > 1)
            {
                ServerLog.Error($"PlayerList: Removing player with id {packet.playerId} -- occurred {matches} times. This should not happen");
            }
        }
        else if (packet.action == PlayerListAction.List)
        {
            ServerLog.Log($"PlayerList: Received player list with {packet.players.Length} entries");

            Multiplayer.session.players.Clear();
            foreach (var info in packet.players)
            {
                ServerLog.Log($"PlayerList: Adding player from list {info.id}:{info.username}");
                Multiplayer.session.players.Add(PlayerInfo.FromNet(info));
            }
        }
        else if (packet.action == PlayerListAction.Latencies)
        {
            if (packet.latencies == null)
            {
                ServerLog.Error("[MP] Player list (Latencies) has no latency array; dropping it.");
                return;
            }

            foreach (var latency in packet.latencies)
            {
                var player = Multiplayer.session.GetPlayerInfo(latency.playerId);
                if (player == null)
                {
                    ServerLog.Log($"PlayerList: Received latency info for unknown player with id {latency.playerId}");
                    continue;
                }
                player.latency = latency.latency;
                player.ticksBehind = latency.ticksBehind;
                player.simulating = latency.simulating;
                player.frameTime = latency.frameTime;
            }
        }
        else if (packet.action == PlayerListAction.Status)
        {
            var player = Multiplayer.session.GetPlayerInfo(packet.playerId);
            if (player == null)
            {
                ServerLog.Log($"PlayerList: Received player status ({packet.status}) for unknown player with id {packet.playerId}");
            }
            else
            {
                player.status = packet.status;
            }
        }
    }

    // Currently handles disconnection only for Steam connections. See comment in ConnectionBase.Close for more info.
    [TypedPacketHandler]
    public virtual void HandleDisconnected(ServerDisconnectPacket packet)
    {
        ConnectionStatusListeners.TryNotifyAll_Disconnected(SessionDisconnectInfo.From(packet));
        Multiplayer.StopMultiplayer();
    }
}
