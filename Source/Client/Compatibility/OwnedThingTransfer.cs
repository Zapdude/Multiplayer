using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Multiplayer.Client.Patches;
using Verse;

namespace Multiplayer.Client.Compatibility;

// Captures a thing and the pawns inside it, then moves them to another map while keeping every timestamp on the
// destination clock. Compat mods use this instead of moving the thing themselves, which would leave timestamps on
// the source map's clock.
public static class OwnedThingTransfer
{
    private sealed class CaptureToken
    {
        internal Game Game;
        internal MultiplayerGame MultiplayerGame;
        internal MultiplayerSession Session;
        internal Thing Root;
        internal Map Source;
        internal List<(Pawn pawn, Map source)> Passengers;
        internal int Consumed;
    }

    public const int Version = 1;

    public static object Capture(Thing root)
    {
        var game = Current.Game;
        var source = root?.Map;

        if (Multiplayer.Client == null ||
            game == null ||
            Multiplayer.game == null ||
            root == null ||
            !root.Spawned ||
            root.Destroyed ||
            !ValidMap(source))
        {
            return null;
        }

        var holders = new List<IThingHolder>();
        ThingOwnerUtility.AppendThingHoldersFromThings(holders, new List<Thing> { root });

        var pawns = holders
            .SelectMany(holder => ThingOwnerUtility.GetAllThingsRecursively(holder, true))
            .OfType<Pawn>();

        if (root is Pawn rootPawn)
            pawns = pawns.Append(rootPawn);

        var passengers = pawns
            .Where(p => p.GetComp<MultiplayerPawnComp>() != null)
            .Distinct()
            .OrderByDescending(TimestampFixer.HolderDepth)
            .ThenBy(p => p.thingIDNumber)
            .Select(p => (pawn: p, source: TimestampFixer.GetPawnClockMap(p)))
            .ToList();

        return new CaptureToken
        {
            Game = game,
            MultiplayerGame = Multiplayer.game,
            Session = Multiplayer.session,
            Root = root,
            Source = source,
            Passengers = passengers
        };
    }

    public static bool Move(object capture, Map destination)
    {
        if (capture is not CaptureToken token || Interlocked.Exchange(ref token.Consumed, 1) != 0)
            return false;

        if (Multiplayer.Client == null ||
            Current.Game != token.Game ||
            Multiplayer.game != token.MultiplayerGame ||
            Multiplayer.session != token.Session ||
            token.Root.Destroyed ||
            token.Root.Spawned ||
            !ValidMap(token.Source) ||
            (destination != null && !ValidMap(destination)) ||
            token.Source == destination)
        {
            return false;
        }

        TimestampFixer.MoveThings(token.Passengers, destination, new[] { token.Root }, token.Source);
        return true;
    }

    private static bool ValidMap(Map map)
    {
        return map != null &&
               !map.Disposed &&
               Current.Game != null &&
               Find.Maps.Contains(map) &&
               map.AsyncTime() != null;
    }
}
