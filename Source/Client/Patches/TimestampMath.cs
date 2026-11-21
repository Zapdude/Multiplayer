namespace Multiplayer.Client.Patches;

/// <summary>
/// How a timestamp field moves when the game clock is shifted.
/// Values below <paramref name="minValid"/> are the game's "never" sentinels: they are
/// preserved untouched, because shifting them would turn "never" into a real tick.
/// Everything else moves by the offset and is clamped so it stays a valid timestamp.
/// </summary>
public static class TimestampMath
{
    public static int Shift(int field, int offset, int minValid)
    {
        if (field < minValid)
            return field;

        // Wide arithmetic: a shifted timestamp that leaves the int range saturates
        // instead of wrapping into a nonsense tick.
        long shifted = (long)field + offset;
        if (shifted > int.MaxValue)
            return int.MaxValue;
        if (shifted < minValid)
            return minValid;
        return (int)shifted;
    }
}
