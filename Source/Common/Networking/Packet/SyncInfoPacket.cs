using System.Collections.Generic;
using System.Linq;

namespace Multiplayer.Common.Networking.Packet;

[PacketDefinition(Packets.Client_SyncInfo, allowFragmented: true)]
public record struct ClientSyncInfoPacket : IPacket
{
    // 8MiB. For some big saves, the sync opinion can reach ~2.5MiB, so this includes some leeway.
    public const int MaxLength = 1 << 23;
    public byte[] rawSyncOpinion;

    public SyncOpinion SyncOpinion
    {
        get => SyncOpinionBinder.Deserialize(rawSyncOpinion);
        set => rawSyncOpinion = SyncOpinionBinder.Serialize(value);
    }

    public void Bind(PacketBuffer buf)
    {
        buf.BindRemaining(ref rawSyncOpinion, maxLength: MaxLength);
    }

    private static readonly Binder<SyncOpinion> SyncOpinionBinder = BinderOf.Identity<SyncOpinion>();
}

[PacketDefinition(Packets.Server_SyncInfo, allowFragmented: true)]
public record struct ServerSyncInfoPacket : IPacket
{
    public byte[] rawSyncOpinion;
    public SyncOpinion SyncOpinion
    {
        get => SyncOpinionBinder.Deserialize(rawSyncOpinion);
        set => rawSyncOpinion = SyncOpinionBinder.Serialize(value);
    }

    public void Bind(PacketBuffer buf)
    {
        buf.BindRemaining(ref rawSyncOpinion, maxLength: ClientSyncInfoPacket.MaxLength);
    }

    private static readonly Binder<SyncOpinion> SyncOpinionBinder = BinderOf.Identity<SyncOpinion>();
}

// Standalone only: tells a client whether it is the sync reference, the client whose opinions the server uses.
// The host and arbiter already know their role, so they never get this.
[PacketDefinition(Packets.Server_SyncReference)]
public record struct ServerSyncReferencePacket(bool isReference) : IPacket
{
    public bool isReference = isReference;

    public void Bind(PacketBuffer buf)
    {
        buf.Bind(ref isReference);
    }
}

public record struct SyncOpinion : IPacketBufferable
{
    public int startTick;
    public List<uint> commandRandomStates;
    public List<uint> worldRandomStates;
    public List<MapRandomState> mapRandomStates;
    // The hash only covers the top stack frames, so a large colony produces millions of traces but few distinct
    // hashes. Sending each distinct value once plus an index per trace keeps the opinion well below the packet limit.
    public List<int> traceHashValues;
    public List<int> traceHashIndices;
    public bool simulating;
    public RoundModeEnum roundMode;

    public void Bind(PacketBuffer buf)
    {
        buf.Bind(ref startTick);

        buf.Bind(ref commandRandomStates, BinderOf.UInt());
        buf.Bind(ref worldRandomStates, BinderOf.UInt());
        buf.Bind(ref mapRandomStates, BinderOf.Identity<MapRandomState>());
        buf.Bind(ref traceHashValues, BinderOf.Int(), maxLength: MaxTraceHashValues);
        BindTraceHashIndices(buf);

        buf.Bind(ref simulating);
        buf.BindEnum(ref roundMode);
    }

    public const int MaxTraceHashValues = 1 << 18;
    public const int MaxUShortIndexValues = ushort.MaxValue + 1;
    // One index per trace, so this bounds the trace count rather than the hash count. At 2 bytes per ushort index,
    // this many still fit inside ClientSyncInfoPacket.MaxLength.
    public const int MaxTraceHashIndices = 1 << 22;

    private void BindTraceHashIndices(PacketBuffer buf)
    {
        var useUShort = traceHashValues is { Count: <= MaxUShortIndexValues };
        buf.Bind(ref useUShort);

        if (useUShort)
        {
            var indices = traceHashIndices?.Select(i => (ushort)i).ToList();
            buf.Bind(ref indices, BinderOf.UShort(), maxLength: MaxTraceHashIndices);
            traceHashIndices = indices?.Select(i => (int)i).ToList();
        }
        else
        {
            buf.Bind(ref traceHashIndices, BinderOf.Int(), maxLength: MaxTraceHashIndices);
        }
    }

    // First-seen order keeps the encoding deterministic, so the same traces always produce the same bytes
    public static (List<int> values, List<int> indices) CompressTraceHashes(List<int> hashes)
    {
        var values = new List<int>();
        var indices = new List<int>(hashes.Count);
        var seen = new Dictionary<int, int>();

        foreach (var hash in hashes)
        {
            if (!seen.TryGetValue(hash, out var index))
            {
                index = values.Count;
                seen.Add(hash, index);
                values.Add(hash);
            }

            indices.Add(index);
        }

        return (values, indices);
    }

    // An out-of-range index means a corrupt packet. Throwing is the only safe answer here: substituting a value
    // would turn a bad packet into a false desync.
    public static List<int> ExpandTraceHashes(List<int> values, List<int> indices)
    {
        var hashes = new List<int>(indices.Count);

        foreach (var index in indices)
        {
            if (index < 0 || index >= values.Count)
                throw new ReaderException($"Trace hash index {index} out of range for {values.Count} values");

            hashes.Add(values[index]);
        }

        return hashes;
    }
}

public record struct MapRandomState : IPacketBufferable
{
    public int mapId;
    public List<uint> randomStates;

    public void Bind(PacketBuffer buf)
    {
        buf.Bind(ref mapId);
        buf.Bind(ref randomStates, BinderOf.UInt());
    }
}
