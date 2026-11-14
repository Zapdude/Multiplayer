using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;

namespace Tests;

// Trace hashes repeat heavily so the opinion sends each distinct value once plus an index per trace.
[TestFixture]
public class SyncOpinionCompressionTest
{
    private static SyncOpinion Opinion(params int[] hashes)
    {
        var (values, indices) = SyncOpinion.CompressTraceHashes(hashes.ToList());
        return new SyncOpinion
        {
            startTick = 100,
            commandRandomStates = [],
            worldRandomStates = [],
            mapRandomStates = [],
            traceHashValues = values,
            traceHashIndices = indices,
            simulating = false,
            roundMode = RoundModeEnum.ToNearest
        };
    }

    private static List<int> RoundTrip(SyncOpinion opinion)
    {
        var packet = new ClientSyncInfoPacket { SyncOpinion = opinion };
        var restored = packet.SyncOpinion;
        return SyncOpinion.ExpandTraceHashes(restored.traceHashValues, restored.traceHashIndices);
    }

    [Test]
    public void RepeatedHashesAreStoredOnce()
    {
        var (values, indices) = SyncOpinion.CompressTraceHashes([7, 7, 9, 7, 9, 11]);

        Assert.That(values, Is.EqualTo(new[] { 7, 9, 11 }));
        Assert.That(indices, Is.EqualTo(new[] { 0, 0, 1, 0, 1, 2 }));
    }

    [Test]
    public void CompressionRoundTripsThroughThePacket()
    {
        var hashes = new[] { 5, 5, 5, 6, 5, 6, 7 };
        Assert.That(RoundTrip(Opinion(hashes)), Is.EqualTo(hashes));
    }

    [Test]
    public void EmptyTracesRoundTrip()
    {
        Assert.That(RoundTrip(Opinion()), Is.Empty);
    }

    [Test]
    public void OutOfRangeIndexIsRejected()
    {
        Assert.Throws<ReaderException>(() => SyncOpinion.ExpandTraceHashes([1, 2], [0, 2]));
        Assert.Throws<ReaderException>(() => SyncOpinion.ExpandTraceHashes([1, 2], [-1]));
    }

    [Test]
    public void ManyRepeatedHashesSerializeFarSmallerThanTheFlatEncoding()
    {
        const int traceCount = 200_000;
        const int distinctCount = 1_000;
        var hashes = Enumerable.Range(0, traceCount).Select(i => i % distinctCount).ToArray();

        var serialized = new ClientSyncInfoPacket { SyncOpinion = Opinion(hashes) }.Serialize().data;
        var flatSize = sizeof(int) + traceCount * sizeof(int);

        Assert.That(serialized.Length, Is.LessThan(flatSize * 0.55));
        Assert.That(RoundTrip(Opinion(hashes)), Is.EqualTo(hashes));
    }

    [Test]
    public void IndicesWidenWhenTheDistinctCountExceedsUShort()
    {
        var hashes = Enumerable.Range(0, SyncOpinion.MaxUShortIndexValues + 10).ToArray();
        var opinion = Opinion(hashes);

        Assert.That(RoundTrip(opinion), Is.EqualTo(hashes));
    }

    [Test]
    public void LargeTraceCountSerializes()
    {
        const int traceCount = 2_532_642;
        const int distinctCount = 1_000;
        var hashes = Enumerable.Range(0, traceCount).Select(i => i % distinctCount).ToArray();

        // Guards the input size: the old reader rejected anything above 1<<21 entries
        var flatCount = traceCount;
        Assert.That(flatCount, Is.GreaterThan(1 << 21), "the old encoding would have thrown on this input");

        var packet = new ClientSyncInfoPacket { SyncOpinion = Opinion(hashes) };
        var serialized = packet.Serialize().data;

        Assert.That(serialized.Length, Is.LessThan(ClientSyncInfoPacket.MaxLength));
        Assert.That(RoundTrip(Opinion(hashes)), Is.EqualTo(hashes));
    }
}
