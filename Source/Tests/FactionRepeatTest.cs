using Multiplayer.Client;

namespace Tests;

// FactionRepeater runs a patched operation once per faction. The repeating flag is the part with no
// visible output: it stops the operation's own call back into the patch from starting a second pass.
// Before it was extracted, the flag was cleared only on the happy path, so one throwing faction left
// it stuck and that patch silently stopped replicating for the rest of the session.
[TestFixture]
public class FactionRepeatTest
{
    private const int Spectator = 99;

    private bool repeating;
    private readonly List<int> pushed = new();
    private readonly List<int> popped = new();
    private readonly List<int> processed = new();
    private readonly List<int> errors = new();

    [SetUp]
    public void Reset()
    {
        repeating = false;
        pushed.Clear();
        popped.Clear();
        processed.Clear();
        errors.Clear();
    }

    [Test]
    public void ProcessesEveryFactionExceptTheSpectator()
    {
        var result = Run(Factions(1, 2, Spectator));

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.False, "driving the factions means the original must not run");
            Assert.That(processed, Is.EquivalentTo(new[] { 1, 2 }));
            Assert.That(pushed, Is.EquivalentTo(new[] { 1, 2 }), "the spectator must never be applied");
        });
    }

    [Test]
    public void LatchIsReleasedAfterTheLoop()
    {
        Run(Factions(1));

        Assert.That(repeating, Is.False);
    }

    [Test]
    public void LatchIsHeldWhileProcessing()
    {
        var nestedOriginalRan = false;

        Run(Factions(1), process: _ =>
        {
            // The patched operation calling back into the same patch while the repeat is running.
            nestedOriginalRan = FactionRepeat.Run(
                Factions(1), _ => { }, Spectator, _ => { }, () => { }, (_, _) => { }, ref repeating);
        });

        Assert.That(nestedOriginalRan, Is.True,
            "a nested call must let the original run instead of starting a second pass");
    }

    [Test]
    public void LatchIsReleasedWhenProcessingThrows()
    {
        Run(Factions(1), process: _ => throw new InvalidOperationException("boom"));

        Assert.That(repeating, Is.False);
    }

    [Test]
    public void LatchIsReleasedWhenPushThrows()
    {
        Assert.Throws<InvalidOperationException>(() =>
            FactionRepeat.Run(Factions(1), _ => { }, Spectator,
                _ => throw new InvalidOperationException("push"), () => { }, (_, _) => { }, ref repeating));

        Assert.That(repeating, Is.False);
    }

    [Test]
    public void AThrowingFactionDoesNotStopTheRest()
    {
        Run(Factions(1, 2, 3), process: id =>
        {
            if (id == 2) throw new InvalidOperationException("boom");
        });

        Assert.Multiple(() =>
        {
            Assert.That(processed, Is.EquivalentTo(new[] { 1, 2, 3 }));
            Assert.That(errors, Is.EqualTo(new[] { 2 }), "the failure is reported once, for its own faction");
        });
    }

    [Test]
    public void EveryPushIsMatchedByAPop()
    {
        Run(Factions(1, 2, Spectator));

        Assert.That(popped.Count, Is.EqualTo(pushed.Count));
    }

    [Test]
    public void PopStillRunsWhenProcessingThrows()
    {
        Run(Factions(1), process: _ => throw new InvalidOperationException("boom"));

        Assert.That(popped, Has.Count.EqualTo(1), "a failed faction must not leave its context applied");
    }

    [Test]
    public void NullFactionDataRunsTheOriginal()
    {
        var result = FactionRepeat.Run<int>(null, _ => { }, Spectator,
            _ => { }, () => { }, (_, _) => { }, ref repeating);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True, "no faction data means singleplayer: the original runs");
            Assert.That(repeating, Is.False);
        });
    }

    [Test]
    public void EmptyFactionDataStillClearsTheLatch()
    {
        var result = Run(Factions());

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.False);
            Assert.That(repeating, Is.False);
            Assert.That(pushed, Is.Empty);
        });
    }

    private static Dictionary<int, int> Factions(params int[] ids) =>
        ids.ToDictionary(id => id, id => id);

    private bool Run(
        IDictionary<int, int> data,
        Action<int>? process = null,
        Action<int>? push = null)
    {
        return FactionRepeat.Run(
            data,
            value =>
            {
                processed.Add(value);
                (process ?? (_ => { }))(value);
            },
            Spectator,
            id =>
            {
                pushed.Add(id);
                (push ?? (_ => { }))(id);
            },
            () => popped.Add(1),
            (id, _) => errors.Add(id),
            ref repeating);
    }
}
