using Multiplayer.Client;

namespace Tests;

// The loading screen reads these every frame, including before the server has sent a size. The old
// arithmetic divided by WorldExpectedSize directly: 0/0 gave NaN and x/0 gave infinity, and the int
// cast of either is meaningless - so a progress bar could be handed int.MinValue.
[TestFixture]
public class DownloadMeterTest
{
    [Test]
    public void ProgressBeforeTheSizeIsKnownIsZero()
    {
        Assert.Multiple(() =>
        {
            Assert.That(DownloadMeter.Progress(0, 0), Is.EqualTo(0f));
            Assert.That(DownloadMeter.Progress(1234, 0), Is.EqualTo(0f));
            Assert.That(DownloadMeter.Percent(1234, 0), Is.EqualTo(0));
        });
    }

    [Test]
    public void ProgressNeverExceedsOne()
    {
        Assert.That(DownloadMeter.Progress(2_000, 1_000), Is.EqualTo(1f));
        Assert.That(DownloadMeter.Percent(2_000, 1_000), Is.EqualTo(100));
    }

    [Test]
    public void ProgressReportsTheFractionReceived()
    {
        Assert.Multiple(() =>
        {
            Assert.That(DownloadMeter.Progress(500, 1_000), Is.EqualTo(0.5f));
            Assert.That(DownloadMeter.Percent(500, 1_000), Is.EqualTo(50));
            Assert.That(DownloadMeter.Percent(999, 1_000), Is.EqualTo(99));
        });
    }

    [Test]
    public void PercentIsAlwaysDrawable()
    {
        foreach (var (received, expected) in new[] { (0u, 0u), (0u, 10u), (10u, 0u), (uint.MaxValue, 1u) })
        {
            var percent = DownloadMeter.Percent(received, expected);

            Assert.That(percent, Is.InRange(0, 100), $"received {received} of {expected}");
        }
    }

    [Test]
    public void SpeedNeedsTwoCheckpoints()
    {
        Assert.That(DownloadMeter.SpeedKBps(Array.Empty<(long, uint)>(), 1_000), Is.EqualTo(0));
        Assert.That(DownloadMeter.SpeedKBps(new[] { (0L, 0u) }, 1_000), Is.EqualTo(0));
    }

    [Test]
    public void SpeedIsBytesOverElapsedTime()
    {
        // 2000 kB over 2 s = 1000 kB/s
        (long, uint)[] checkpoints = [(0L, 0u), (2_000L, 2_000_000u)];

        Assert.That(DownloadMeter.SpeedKBps(checkpoints, 2_000L), Is.EqualTo(1_000));
    }

    [Test]
    public void SpeedUsesTheWholeWindowNotTheLastSample()
    {
        (long, uint)[] checkpoints = [(0L, 0u), (1_000L, 500_000u), (2_000L, 1_000_000u)];

        Assert.That(DownloadMeter.SpeedKBps(checkpoints, 2_000L), Is.EqualTo(500));
    }

    [Test]
    public void StalledClockReportsNoSpeed()
    {
        (long, uint)[] checkpoints = [(1_000L, 0u), (1_000L, 1_000u)];

        Assert.That(DownloadMeter.SpeedKBps(checkpoints, 1_000L), Is.EqualTo(0));
    }

    [Test]
    public void OutOfOrderCheckpointsReportNoSpeed()
    {
        // A stale counter must not wrap into a huge unsigned value and read as a fast download.
        (long, uint)[] checkpoints = [(0L, 5_000u), (1_000L, 1_000u)];

        Assert.That(DownloadMeter.SpeedKBps(checkpoints, 1_000L), Is.EqualTo(0));
    }

    [Test]
    public void SpeedIsNeverNegative()
    {
        (long, uint)[] checkpoints = [(0L, 1_000u), (1_000L, 2_000u)];

        Assert.That(DownloadMeter.SpeedKBps(checkpoints, -5_000L), Is.EqualTo(0));
    }

    [Test]
    public void HugeByteCountsDoNotOverflowTheCast()
    {
        (long, uint)[] checkpoints = [(0L, 0u), (1L, uint.MaxValue)];

        Assert.That(DownloadMeter.SpeedKBps(checkpoints, 1L), Is.EqualTo(int.MaxValue));
    }
}
