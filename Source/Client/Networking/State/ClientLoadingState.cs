using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Ionic.Zlib;
using Multiplayer.Client.Desyncs;
using Multiplayer.Client.Patches;
using Multiplayer.Client.Saving;
using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;
using Verse;

namespace Multiplayer.Client;

public enum LoadingState
{
    Waiting,
    Downloading
}

[PacketHandlerClass(inheritHandlers: true)]
public class ClientLoadingState(ConnectionBase connection) : ClientBaseState(connection)
{
    public LoadingState subState = LoadingState.Waiting;
    public uint WorldExpectedSize { get; private set; }
    public uint WorldReceivedSize { get; private set; }
    public float DownloadProgress => (float)WorldReceivedSize / WorldExpectedSize;
    public int DownloadProgressPercent => (int)(DownloadProgress * 100);
    public int DownloadSpeedKBps
    {
        get
        {
            if (downloadCheckpoints.Count < 2) return 0;
            var firstCheckpoint = downloadCheckpoints.First();
            var lastCheckpoint = downloadCheckpoints.Last();

            var timeTakenMs = Utils.MillisNow - firstCheckpoint.Item1;
            if (timeTakenMs <= 0) return 0;
            var timeTakenSecs = timeTakenMs / 1000f;

            var downloadedBytes = lastCheckpoint.Item2 - firstCheckpoint.Item2;
            var downloadedKBytes = downloadedBytes / 1000;
            return (int)(downloadedKBytes / timeTakenSecs);
        }
    }

    private List<(long, uint)> downloadCheckpoints = new(capacity: 64);
    private Stopwatch downloadTimeStopwatch = new();

    [PacketHandler(Packets.Server_WorldDataStart)]
    public void HandleWorldDataStart(ByteReader data)
    {
        subState = LoadingState.Downloading;
        connection.Lenient = false; // Lenient is set while rejoining
        downloadTimeStopwatch.Start();
    }

    // The server sends the full roster right after the world data. On a rejoin that arrives while this state is
    // still active; without a handler here the packet is dropped instead of reaching the playing state later.
    [TypedPacketHandler]
    public void HandlePlayerList(ServerPlayerListPacket packet) => ClientPlayingState.ApplyPlayerList(packet);

    [TypedPacketHandler]
    public void HandleClientDiagnostics(ServerDiagnosticsPacket packet) =>
        ClientDiagnosticsStore.Append(packet);

    // The server sends sync opinions to every playing connection, which includes clients still loading. The join
    // path already applies the initial opinions from the world data, and a live opinion cannot be compared before
    // the game is loaded, so accept and drop it instead of rejecting the packet (#691).
    [TypedPacketHandler]
    public void HandleDesyncCheck(ServerSyncInfoPacket packet)
    {
    }

    [FragmentedPacketHandler(Packets.Server_WorldData)]
    public void HandleWorldDataFragment(FragmentedPacket packet)
    {
        WorldExpectedSize = packet.ExpectedSize;
        WorldReceivedSize = packet.ReceivedSize;
        if (downloadCheckpoints.Count == downloadCheckpoints.Capacity)
            downloadCheckpoints.RemoveAt(0);
        downloadCheckpoints.Add((Utils.MillisNow, packet.ReceivedSize));
    }

    [PacketHandler(Packets.Server_WorldData, allowFragmented: true)]
    public void HandleWorldData(ByteReader data)
    {
        // Preserve the last reported map on rejoin, but don't report temporary
        // loading views or views encountered while replaying catch-up commands.
        VTRSync.Suspend();
        var downloadMs = downloadTimeStopwatch.ElapsedMilliseconds;
        downloadTimeStopwatch.Reset();
        Log.Message($"Game data size: {data.Length}. Took {downloadMs}ms to receive.");

        int factionId = data.ReadInt32();
        Multiplayer.session.myFactionId = factionId;

        // The server knows this connection's view from scratch (also after a rejoin, which keeps the session)
        VTRSync.Reset();

        // The server only picks caught up players as the sync reference
        Multiplayer.session.isSyncReference = false;
        // Also on a rejoin, which keeps the session
        SimulationFailures.Reset(Multiplayer.session);

        int tickUntil = data.ReadInt32();
        int remoteSentCmds = data.ReadInt32();
        bool serverFrozen = data.ReadBool();

        byte[] worldData = GZipStream.UncompressBuffer(data.ReadPrefixedBytes());
        byte[] sessionData = GZipStream.UncompressBuffer(data.ReadPrefixedBytes());

        var mapDataDict = new Dictionary<int, byte[]>();
        List<int> mapsToLoad = new List<int>();

        // World and map cmds in the order live clients executed them
        int cmdsCount = data.ReadInt32();
        var cmds = new List<ScheduledCommand>(cmdsCount);
        for (int i = 0; i < cmdsCount; i++)
            cmds.Add(ScheduledCommand.Deserialize(new ByteReader(data.ReadPrefixedBytes())));

        int mapDataCount = data.ReadInt32();
        for (int i = 0; i < mapDataCount; i++)
        {
            int mapId = data.ReadInt32();
            byte[] rawMapData = data.ReadPrefixedBytes();

            byte[] mapData = GZipStream.UncompressBuffer(rawMapData);
            mapDataDict[mapId] = mapData;
            mapsToLoad.Add(mapId);
        }

        Session.dataSnapshot = new GameDataSnapshot(
            0,
            worldData,
            sessionData,
            mapDataDict,
            cmds
        );

        TickPatch.tickUntil = tickUntil;
        Multiplayer.session.receivedCmds = remoteSentCmds;
        Multiplayer.session.remoteTickUntil = tickUntil;
        TickPatch.serverFrozen = serverFrozen;

        int syncInfos = data.ReadInt32();
        if (syncInfos < 0 || syncInfos > data.Left / sizeof(int))
            throw new ReaderException($"Invalid sync opinion count ({syncInfos}) for {data.Left} remaining bytes");

        var initialOpinions = new List<ClientSyncOpinion>(syncInfos);
        for (int i = 0; i < syncInfos; i++)
        {
            // The server forwards the opinions verbatim, in the SyncOpinion wire format
            var raw = data.ReadPrefixedBytes();
            if (raw == null)
                throw new ReaderException("Missing sync opinion");

            initialOpinions.Add(ClientSyncOpinion.FromNet(SyncOpinion.FromBytes(raw)));
        }

        Log.Message(syncInfos > 0
            ? $"Initial sync opinions: {initialOpinions.First().startTick}...{initialOpinions.Last().startTick}"
            : "No initial sync opinions");

        TickPatch.SetSimulation(
            toTickUntil: true,
            onFinish: () =>
            {
                Multiplayer.Client.Send(Packets.Client_WorldReady);
                VTRSync.Start();
            },
            cancelButtonKey: "Quit",
            onCancel: GenScene.GoToMainMenu // Calls StopMultiplayer through a patch
        );

        Stopwatch watch = Stopwatch.StartNew();
        Loader.ReloadGame(mapsToLoad, true, () =>
        {
            foreach (var opinion in initialOpinions)
            {
                Multiplayer.game.sync.AddClientOpinionAndCheckDesync(opinion);
            }
        });
        var loadingMs = watch.ElapsedMilliseconds;
        Log.Message($"Loaded game in {loadingMs}ms");
        connection.ChangeState(ConnectionStateEnum.ClientPlaying);
    }
}
