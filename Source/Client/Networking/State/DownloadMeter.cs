using System;
using System.Collections.Generic;

namespace Multiplayer.Client;

/// <summary>
/// Download progress is read on every frame by the loading UI, including before the server has
/// announced a size. Once announced the size is a fact, but a zero or a stale counter must still
/// produce a number the UI can draw instead of NaN, infinity or int.MinValue.
/// </summary>
public static class DownloadMeter
{
    public static float Progress(uint received, uint expected) =>
        expected == 0 ? 0f : Math.Min(1f, (float)received / expected);

    public static int Percent(uint received, uint expected) =>
        (int)(Progress(received, expected) * 100);

    /// <param name="checkpoints">(milliseconds, total bytes received) samples, oldest first.</param>
    public static int SpeedKBps(IReadOnlyList<(long TimeMs, uint Bytes)> checkpoints, long nowMs)
    {
        if (checkpoints.Count < 2)
            return 0;

        var first = checkpoints[0];
        var last = checkpoints[checkpoints.Count - 1];

        var elapsedMs = nowMs - first.TimeMs;
        if (elapsedMs <= 0)
            return 0;

        // Signed: a checkpoint that arrives out of order must read as no progress, not as a wrap.
        var bytes = (long)last.Bytes - first.Bytes;
        if (bytes <= 0)
            return 0;

        var kilobytes = bytes / 1000d;
        var seconds = elapsedMs / 1000d;
        return (int)Math.Min(int.MaxValue, kilobytes / seconds);
    }
}
