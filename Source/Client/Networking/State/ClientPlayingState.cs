using System;
using System.Collections.Generic;
using System.Linq;
using Ionic.Zlib;
using Multiplayer.Client.Desyncs;
using Multiplayer.Client.Saving;
using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;
using RimWorld;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    [PacketHandlerClass(inheritHandlers: true)]
    public class ClientPlayingState(ConnectionBase connection) : ClientBaseState(connection)
    {
        [TypedPacketHandler]
        public void HandleCommand(ServerCommandPacket packet)
        {
            Session.ScheduleCommand(packet.ToCommand());
            Multiplayer.session.receivedCmds++;
            Multiplayer.session.ProcessTimeControl();
        }

        [TypedPacketHandler]
        public void HandleChat(ServerChatPacket packet) => Multiplayer.session.AddMsg(packet.msg, rawMessage: packet.rawMessage);

        [TypedPacketHandler]
        public void HandleCursor(ServerCursorPacket packet)
        {
            var player = Multiplayer.session.GetPlayerInfo(packet.playerId);
            if (player == null) return;

            var data = packet.data;
            if (data.seq < player.cursorSeq && player.cursorSeq - data.seq < 128) return;

            player.map = data.map;
            if (data.map == byte.MaxValue) return;

            player.cursorSeq = data.seq;
            player.lastCursor = player.cursor;
            player.lastDelta = Multiplayer.clock.ElapsedMillisDouble() - player.updatedAt;
            player.cursor = new Vector3(data.x, 0, data.z);
            player.updatedAt = Multiplayer.clock.ElapsedMillisDouble();
            player.cursorIcon = data.icon;

            player.dragStart = data.HasDrag ? new Vector3(data.dragX, 0, data.dragZ) : PlayerInfo.Invalid;
        }

        [TypedPacketHandler]
        public void HandleSelected(ServerSelectedPacket packet)
        {
            var player = Multiplayer.session.GetPlayerInfo(packet.playerId);
            if (player == null) return;

            var data = packet.data;
            if (data.reset) player.selectedThings.Clear();

            foreach (var id in data.newlySelectedIds)
                player.selectedThings[id] = Time.realtimeSinceStartup;

            foreach (var id in data.unselectedIds)
                player.selectedThings.Remove(id);
        }

        [TypedPacketHandler]
        public void HandlePing(ServerPingLocPacket packet) => Session.locationPings.ReceivePing(packet);

        [TypedPacketHandler]
        public void HandleNotification(ServerNotificationPacket packet)
        {
            var namedArgs = Array.ConvertAll(packet.args, s => (NamedArgument)s);
            var msg = packet.key.Translate(namedArgs);
            Messages.Message(msg, MessageTypeDefOf.SilentInput, false);
            ServerLog.Log($"Notification: {msg} ({packet.key}, {packet.args.Join(", ")})");
        }

        [TypedPacketHandler]
        public void HandleDesyncCheck(ServerSyncInfoPacket packet) =>
            Multiplayer.game?.sync.AddClientOpinionAndCheckDesync(ClientSyncOpinion.FromNet(packet.SyncOpinion));

        [TypedPacketHandler]
        public void HandleFreeze(ServerFreezePacket packet)
        {
            TickPatch.serverFrozen = packet.frozen;
            TickPatch.frozenAt = packet.gameTimer;
        }

        [TypedPacketHandler]
        public void HandleTraces(ServerTracesPacket packet)
        {
            if (packet.mode == ServerTracesPacket.Mode.Request)
            {
                var info = Multiplayer.game.sync.knownClientOpinions.FirstOrDefault(b => b.startTick == packet.tick);
                var response = info?.GetFormattedStackTracesForRange(packet.diffAt) ?? "Traces not available";

                // Fragmented: the traces and jitted methods can exceed the single packet limit, which would throw
                // in this handler and disconnect this client (on standalone, the sync reference)
                connection.SendFragmented(new ClientTracesPacket
                    {
                        playerId = packet.playerId,
                        rawTraces = GZipStream.CompressString(response),
                        rawJittedMethods = GZipStream.CompressString(JittedMethods.GetJittedMethodsString())
                    }.Serialize());
            }
            else if (packet.mode == ServerTracesPacket.Mode.Transfer)
            {
                var traces = GZipStream.UncompressString(packet.rawTraces);
                var jittedMethods = GZipStream.UncompressString(packet.rawJittedMethods);
                var hostInfo = new SaveableDesyncInfo.HostInfo(traces, jittedMethods);
                Find.WindowStack.WindowOfType<DesyncedWindow>()?.HandleHostDesyncInfo(hostInfo);
            }
        }

        [TypedPacketHandler]
        public void HandleDebug(ServerDebugPacket _) => Rejoiner.DoRejoin();

        [TypedPacketHandler]
        public void HandleRequestRejoin(ServerRequestRejoinPacket _) => Rejoiner.DoRejoin();

        [TypedPacketHandler]
        public void HandleSetFaction(ServerSetFactionPacket packet)
        {
            var playerId = packet.playerId;
            var factionId = packet.factionId;
            Session.GetPlayerInfo(playerId).factionId = factionId;

            if (Session.playerId == playerId)
            {
                Multiplayer.game.ChangeRealPlayerFaction(factionId);
                Session.myFactionId = factionId;
            }
        }
    }

}
