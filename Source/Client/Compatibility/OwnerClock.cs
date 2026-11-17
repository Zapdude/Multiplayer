using System;
using System.Collections.Generic;
using Multiplayer.Client.AsyncTime;
using Multiplayer.Client.Patches;
using Verse;

namespace Multiplayer.Client.Compatibility;

// Reads a saved timestamp the way its owner's clock sees it. Compat mods use this to serialize values that
// belong to a map, the world, or the map a pawn currently belongs to, instead of whichever clock happens to be
// set on this peer.
public static class OwnerClock
{
    private readonly record struct Frame(
        Game Game,
        TickManager Manager,
        MultiplayerSession Session,
        ITickable Owner,
        TimeSnapshot Previous,
        long Token);

    public readonly struct ReadScope : IDisposable
    {
        private readonly long token;

        internal ReadScope(long token) => this.token = token;

        public void Dispose() => EndRead(token);
    }

    public const int Version = 1;

    [ThreadStatic] private static List<Frame> frames;
    [ThreadStatic] private static long nextToken;

    static OwnerClock()
    {
        SessionLifecycle.Register("rwmt.owner-clock", null, Stopping);
    }

    private static void PruneStaleFrames()
    {
        if (frames == null) return;

        for (var i = frames.Count - 1; i >= 0; i--)
        {
            var frame = frames[i];
            if (frame.Game != Current.Game ||
                frame.Session != Multiplayer.session ||
                frame.Manager != Current.Game?.tickManager)
            {
                frames.RemoveAt(i);
            }
        }
    }

    private static void Stopping()
    {
        PruneStaleFrames();

        if (frames is { Count: > 0 })
            frames[0].Previous.Set();

        frames?.Clear();
    }

    private static ITickable OwnerOf(Map map)
    {
        if (map == null || map.Disposed || Current.Game == null || !Find.Maps.Contains(map))
            return null;

        return map.AsyncTime();
    }

    private static ITickable OwnerOfPawn(Pawn pawn)
    {
        if (pawn == null || Current.Game?.tickManager == null || Multiplayer.game == null)
            return null;

        var clockMap = TimestampFixer.GetPawnClockMap(pawn);
        return clockMap != null ? clockMap.AsyncTime() : OwnerOfWorld();
    }

    private static ITickable OwnerOfWorld() => Multiplayer.game?.asyncWorldTimeComp;

    public static bool TryReadMap(
        Map map,
        out int ticks,
        out TimeSpeed speed,
        out TimeSlower slower,
        out int gameStartAbsTick)
    {
        return TryRead(OwnerOf(map), out ticks, out speed, out slower, out gameStartAbsTick);
    }

    public static bool TryReadWorld(
        out int ticks,
        out TimeSpeed speed,
        out TimeSlower slower,
        out int gameStartAbsTick)
    {
        return TryRead(OwnerOfWorld(), out ticks, out speed, out slower, out gameStartAbsTick);
    }

    public static bool TryReadPawn(
        Pawn pawn,
        out int ticks,
        out TimeSpeed speed,
        out TimeSlower slower,
        out int gameStartAbsTick)
    {
        return TryRead(OwnerOfPawn(pawn), out ticks, out speed, out slower, out gameStartAbsTick);
    }

    private static bool TryRead(
        ITickable owner,
        out int ticks,
        out TimeSpeed speed,
        out TimeSlower slower,
        out int gameStartAbsTick)
    {
        PruneStaleFrames();

        if (owner == null || Current.Game?.tickManager == null)
        {
            ticks = 0;
            speed = TimeSpeed.Paused;
            slower = null;
            gameStartAbsTick = 0;
            return false;
        }

        // An outer read scope already put this owner's clock in place: return what it saw, not the modified values
        if (frames != null)
        {
            for (var i = frames.Count - 1; i >= 0; i--)
            {
                var frame = frames[i];
                if (frame.Game != Current.Game || frame.Owner != owner) continue;

                ticks = frame.Previous.ticks;
                speed = frame.Previous.speed;
                slower = frame.Previous.slower;
                gameStartAbsTick = frame.Previous.gameStartAbsTick;
                return true;
            }
        }

        switch (owner)
        {
            case AsyncTimeComp map:
                ticks = map.mapTicks;
                speed = map.DesiredTimeSpeed;
                slower = map.slower;
                gameStartAbsTick = map.GameStartAbsTick;
                return true;

            case AsyncWorldTimeComp world:
                ticks = world.worldTicks;
                speed = world.DesiredTimeSpeed;
                slower = Find.TickManager.slower;
                gameStartAbsTick = Find.TickManager.gameStartAbsTick;
                return true;

            default:
                ticks = 0;
                speed = TimeSpeed.Paused;
                slower = null;
                gameStartAbsTick = 0;
                return false;
        }
    }

    public static ReadScope EnterMapRead(Map map)
    {
        return BeginMapRead(map, out var token) ? new ReadScope(token) : default;
    }

    public static ReadScope EnterWorldRead()
    {
        return BeginWorldRead(out var token) ? new ReadScope(token) : default;
    }

    public static ReadScope EnterPawnRead(Pawn pawn)
    {
        return BeginPawnRead(pawn, out var token) ? new ReadScope(token) : default;
    }

    public static bool BeginMapRead(Map map, out long token)
    {
        return Begin(
            OwnerOf(map),
            TryReadMap(map, out var ticks, out var speed, out var slower, out var start),
            ticks, speed, slower, start, out token);
    }

    public static bool BeginWorldRead(out long token)
    {
        return Begin(
            OwnerOfWorld(),
            TryReadWorld(out var ticks, out var speed, out var slower, out var start),
            ticks, speed, slower, start, out token);
    }

    public static bool BeginPawnRead(Pawn pawn, out long token)
    {
        return Begin(
            OwnerOfPawn(pawn),
            TryReadPawn(pawn, out var ticks, out var speed, out var slower, out var start),
            ticks, speed, slower, start, out token);
    }

    private static bool Begin(
        ITickable owner,
        bool valid,
        int ticks,
        TimeSpeed speed,
        TimeSlower slower,
        int start,
        out long token)
    {
        token = 0;
        if (!valid) return false;

        var list = frames ??= new List<Frame>();

        token = ++nextToken;
        if (token == 0) token = ++nextToken;

        list.Add(new Frame(
            Current.Game,
            Current.Game.tickManager,
            Multiplayer.session,
            owner,
            TimeSnapshot.Current(),
            token));

        var snapshot = default(TimeSnapshot);
        snapshot.ticks = ticks;
        snapshot.speed = speed;
        snapshot.slower = slower;
        snapshot.gameStartAbsTick = start;
        snapshot.Set();

        return true;
    }

    public static void EndRead(long token)
    {
        PruneStaleFrames();

        var list = frames;
        if (token == 0 || list == null || list.Count == 0) return;

        var frame = list[list.Count - 1];
        if (frame.Token != token)
        {
            foreach (var other in list)
                if (other.Token == token && other.Game == Current.Game)
                    throw new InvalidOperationException("Owner clock read scopes must end in LIFO order");
            return;
        }

        list.RemoveAt(list.Count - 1);

        if (Current.Game == frame.Game && frame.Game.tickManager == frame.Manager)
            frame.Previous.Set();
    }
}
