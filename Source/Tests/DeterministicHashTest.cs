using Multiplayer.Client.Util;

namespace Tests;

// These values are a contract, not an implementation detail: two clients hash the same world
// state and compare the result. Changing a constant or a shift silently desyncs players running
// different builds, and the only symptom is a desync report much later.
[TestFixture]
public class DeterministicHashTest
{
    [Test]
    public void CombinersAreStable()
    {
        Assert.Multiple(() =>
        {
            Assert.That(DeterministicHash.HashCombineInt(1, 2, 3), Is.EqualTo(-1100237272), "3-arg");
            Assert.That(DeterministicHash.HashCombineInt(1, 2, 3, 4, 5), Is.EqualTo(994301476), "5-arg");
            Assert.That(DeterministicHash.HashCombineInt(1, 2, 3, 4, 5, 6), Is.EqualTo(-652882213), "6-arg");
            Assert.That(DeterministicHash.HashCombineInt(1, 2, 3, 4, 5, 6, 7), Is.EqualTo(-1500735495), "7-arg");
            Assert.That(DeterministicHash.HashCombineInt(1, 2, 3, 4, 5, 6, 7, 8), Is.EqualTo(-1636360492), "8-arg");
        });
    }

    [Test]
    public void CombinersDependOnEveryValue()
    {
        var baseline = DeterministicHash.HashCombineInt(1, 2, 3, 4, 5, 6, 7, 8);

        Assert.Multiple(() =>
        {
            Assert.That(DeterministicHash.HashCombineInt(0, 2, 3, 4, 5, 6, 7, 8), Is.Not.EqualTo(baseline), "first");
            Assert.That(DeterministicHash.HashCombineInt(1, 0, 3, 4, 5, 6, 7, 8), Is.Not.EqualTo(baseline), "second");
            Assert.That(DeterministicHash.HashCombineInt(1, 2, 3, 4, 5, 6, 7, 0), Is.Not.EqualTo(baseline), "last");
        });
    }

    [Test]
    public void SeedsAreUnchanged()
    {
        // The seeds are part of the contract; the combiners are meaningless without them.
        Assert.Multiple(() =>
        {
            Assert.That(DeterministicHash.DefaultSeed, Is.EqualTo(352654597));
            Assert.That(DeterministicHash.DefaultSeed2, Is.EqualTo(1566083941));
        });
    }
}
