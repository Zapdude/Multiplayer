using System;
using System.Collections.Generic;
using System.Linq;
using Multiplayer.Common.Networking.Packet;

namespace Multiplayer.Common;

/// <summary>
/// Validates a client's diagnostics batch and relays it to the host. The host is the only peer that stores it;
/// the server itself never parses the payload.
/// </summary>
public static class ClientDiagnosticsRelay
{
    private const int MinimumIntervalMs = 2_000;

    private static readonly Dictionary<int, long> lastAccepted = new();

    public static void Handle(ConnectionBase connection, ClientDiagnosticsPacket packet)
    {
        var server = MultiplayerServer.instance;
        var player = connection.serverPlayer;

        if (server == null || player == null) return;
        if (!server.settings.receiveClientDiagnostics) return;
        if (player.IsHost || player.IsArbiter || !player.hasJoined) return;

        if (packet.payload == null || packet.payload.Length == 0 ||
            packet.payload.Length > ClientDiagnosticsLimits.MaxPayload)
            return;

        var now = Environment.TickCount64;
        if (lastAccepted.TryGetValue(player.id, out var last) && now - last < MinimumIntervalMs)
            return;
        lastAccepted[player.id] = now;

        var host = server.PlayingPlayers.FirstOrDefault(p => p.IsHost && !p.IsArbiter && p.hasJoined);
        if (host == null) return;

        host.SendPacket(new ServerDiagnosticsPacket(player.id, packet.sequence, packet.payload));
    }

    internal static void Reset() => lastAccepted.Clear();
}
