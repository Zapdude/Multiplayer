using System;
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
        var start = buf.isWriting ? buf.Writer.Position : 0;

        if (buf.isWriting)
        {
            // A hand-built opinion may leave the lists null; an opinion with no entries is still valid to send
            commandRandomStates ??= [];
            worldRandomStates ??= [];
            mapRandomStates ??= [];
            traceHashValues ??= [];
            traceHashIndices ??= [];
        }

        buf.Bind(ref startTick);

        buf.Bind(ref commandRandomStates, BinderOf.UInt());
        buf.Bind(ref worldRandomStates, BinderOf.UInt());
        buf.Bind(ref mapRandomStates, BinderOf.Identity<MapRandomState>());
        buf.Bind(ref traceHashValues, BinderOf.Int(), maxLength: MaxTraceHashValues);
        BindTraceHashIndices(buf, start);

        buf.Bind(ref simulating);
        buf.BindEnum(ref roundMode);

        // The packet's own limit would reject this after the whole opinion was written; fail here with the size
        if (buf.isWriting && buf.Writer.Position - start > ClientSyncInfoPacket.MaxLength)
            throw new WriterException(
                $"Sync opinion too big ({buf.Writer.Position - start}>{ClientSyncInfoPacket.MaxLength})");
    }

    public const int MaxTraceHashValues = 1 << 18;
    public const int MaxUShortIndexValues = ushort.MaxValue + 1;
    // Read-side sanity cap; the write path enforces ClientSyncInfoPacket.MaxLength in BindTraceHashIndices.
    public const int MaxTraceHashIndices = 1 << 22;

    /// Reads an opinion in the wire format shared by ClientSyncInfoPacket and ServerSyncInfoPacket. The sync opinions
    /// in the world data use this format too, not the legacy desync report format of ClientSyncOpinion.
    public static SyncOpinion FromBytes(byte[] bytes)
    {
        var opinion = default(SyncOpinion);
        BinderOf.Identity<SyncOpinion>()(new PacketReader(new ByteReader(bytes)), ref opinion);
        return opinion;
    }

    private void BindTraceHashIndices(PacketBuffer buf, int start)
    {
        bool useUShort = false;

        if (buf.isWriting)
        {
            // Every index must address a value: a bad one would silently become a different hash on the far side
            foreach (var index in traceHashIndices)
                if (index < 0 || index >= traceHashValues.Count)
                    throw new WriterException(
                        $"Trace hash index {index} out of range for {traceHashValues.Count} values");

            useUShort = IndicesFitUShort(traceHashIndices);
        }

        buf.Bind(ref useUShort);

        if (buf.isWriting)
        {
            // Leave room for the indices' length prefix, simulating and roundMode. The packet's own limit would throw
            // a generic error after the whole list was written; fail before with the count that doesn't fit
            var width = useUShort ? sizeof(ushort) : sizeof(int);
            var remaining = ClientSyncInfoPacket.MaxLength - (buf.Writer.Position - start) -
                            sizeof(int) - sizeof(bool) - sizeof(short);
            var maxIndices = Math.Max(0, remaining / width);
            if (traceHashIndices.Count > maxIndices)
                throw new WriterException(
                    $"Sync opinion too big: {traceHashIndices.Count} trace indices at {width} bytes each don't fit " +
                    $"in {ClientSyncInfoPacket.MaxLength} bytes (max {maxIndices})");
        }

        if (useUShort)
        {
            if (buf.isWriting)
            {
                var indices = new List<ushort>(traceHashIndices.Count);
                foreach (var index in traceHashIndices)
                    indices.Add((ushort)index);

                buf.Bind(ref indices, BinderOf.UShort(), maxLength: MaxTraceHashIndices);
            }
            else
            {
                var indices = new List<ushort>();
                buf.Bind(ref indices, BinderOf.UShort(), maxLength: MaxTraceHashIndices);

                traceHashIndices = new List<int>(indices.Count);
                foreach (var index in indices)
                    traceHashIndices.Add(index);
            }
        }
        else
        {
            buf.Bind(ref traceHashIndices, BinderOf.Int(), maxLength: MaxTraceHashIndices);
        }
    }

    internal static bool IndicesFitUShort(List<int> indices)
    {
        var maxIndex = -1;
        foreach (var index in indices)
        {
            if (index < 0)
                return false;
            if (index > maxIndex)
                maxIndex = index;
        }

        // More than 65,536 values can still be addressed by ushort indices when the traces don't use the later ones
        return maxIndex < MaxUShortIndexValues;
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
