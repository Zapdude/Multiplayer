using Multiplayer.Client.Patches;

namespace Tests;

// The map-transition fixer shifts every registered pawn timestamp by the tick delta between
// maps. The sentinel rules are what make it safe: a field holding "never" must stay "never",
// and a field that lands before the map's origin must not become a tick from the past.
[TestFixture]
public class TimestampMathTest
{
    private const int NeverZero = 0;

    [TestCase(int.MinValue, 1000, 0, int.MinValue, TestName = "Never marker at int.MinValue is preserved")]
    [TestCase(-5, 1000, 0, -5, TestName = "Value below minValid is preserved")]
    [TestCase(0, 1000, 0, 1000, TestName = "Unset zero moves with a positive offset")]
    [TestCase(0, -1000, 0, 0, TestName = "Unset zero is clamped, never negative")]
    public void SentinelRules(int field, int offset, int minValid, int expected)
    {
        Assert.That(TimestampMath.Shift(field, offset, minValid), Is.EqualTo(expected));
    }

    [Test]
    public void OrdinaryValueMovesByOffset()
    {
        Assert.That(TimestampMath.Shift(10_000, 2_500, NeverZero), Is.EqualTo(12_500));
        Assert.That(TimestampMath.Shift(10_000, -2_500, NeverZero), Is.EqualTo(7_500));
    }

    [Test]
    public void ZeroOffsetIsIdentity()
    {
        foreach (var field in new[] { 0, 1, 42, 62_500, int.MaxValue })
            Assert.That(TimestampMath.Shift(field, 0, NeverZero), Is.EqualTo(field));
    }

    [Test]
    public void ResultNeverFallsBelowMinValid()
    {
        Assert.That(TimestampMath.Shift(100, -5_000, NeverZero), Is.EqualTo(NeverZero));
        Assert.That(TimestampMath.Shift(100, int.MinValue, NeverZero), Is.EqualTo(NeverZero));
    }

    [Test]
    public void PositiveOverflowSaturates()
    {
        Assert.That(TimestampMath.Shift(int.MaxValue, 1, NeverZero), Is.EqualTo(int.MaxValue));
        Assert.That(TimestampMath.Shift(int.MaxValue - 1, 10, NeverZero), Is.EqualTo(int.MaxValue));
    }

    [Test]
    public void NegativeWrapDoesNotProduceARealTick()
    {
        // Old arithmetic allowed this to wrap straight past minValid; it must clamp instead.
        var result = TimestampMath.Shift(5, int.MinValue, NeverZero);

        Assert.That(result, Is.GreaterThanOrEqualTo(NeverZero));
    }

    [Test]
    public void MinValidOfIntMinValueOnlyProtectsNothing()
    {
        // The default registrations pass int.MinValue, so every int is a real timestamp there.
        Assert.That(TimestampMath.Shift(int.MinValue, 100, int.MinValue), Is.EqualTo(int.MinValue + 100));
    }

    [Test]
    public void ShiftIsMonotonicInOffset()
    {
        var previous = int.MinValue;

        for (var offset = -50_000; offset <= 50_000; offset += 2_500)
        {
            var value = TimestampMath.Shift(1_000_000, offset, NeverZero);
            Assert.That(value, Is.GreaterThanOrEqualTo(previous), $"offset {offset} went backwards");
            previous = value;
        }
    }
}
