using System;
using Verse;

namespace Multiplayer.Client.Compatibility;

// Owner/revision state for choices a single player makes (targets, tools, pending decisions).
// Compat mods serialize one of these next to their own state and check it before acting.
public static class DecisionOwnership
{
    private sealed class State : IExposable
    {
        internal int Owner = -1;
        internal int Revision;

        public void ExposeData()
        {
            Scribe_Values.Look(ref Revision, "revision", 0);
            Scribe_Values.Look(ref Owner, "owner", -1);
        }
    }

    public const int Version = 1;

    private static State Require(IExposable state)
    {
        return state as State ?? throw new ArgumentException("Unknown ownership state", nameof(state));
    }

    public static IExposable Create() => new State();

    public static int GetOwner(IExposable state) => Require(state).Owner;

    public static int GetRevision(IExposable state) => Require(state).Revision;

    public static void SetOwner(IExposable state, int owner) => Require(state).Owner = owner;

    public static void SetRevision(IExposable state, int revision) => Require(state).Revision = revision;

    public static bool Matches(IExposable state, int owner, int revision)
    {
        var s = Require(state);
        return s.Owner == owner && s.Revision == revision;
    }

    public static bool CanChoose(IExposable state, int owner, int revision)
    {
        return owner >= 0 && SessionLifecycle.ExecutingPlayerId == owner && Matches(state, owner, revision);
    }

    public static bool TryClaim(IExposable state, int owner, int revision)
    {
        var executing = SessionLifecycle.ExecutingPlayerId;
        if (executing < 0 || !Matches(state, owner, revision)) return false;

        var s = Require(state);
        s.Owner = executing;
        s.Revision++;
        return true;
    }

    public static void Retire(IExposable state, int playerId)
    {
        var s = Require(state);
        if (s.Owner != playerId) return;

        s.Owner = -1;
        s.Revision++;
    }
}
