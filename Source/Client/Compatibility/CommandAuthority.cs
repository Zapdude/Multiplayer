using Multiplayer.Client.Patches;
using RimWorld;
using Verse;

namespace Multiplayer.Client.Compatibility;

// Tells a compat mod whether the local peer may act for a faction, pawn or player right now.
// Outside command execution (ticking, simulating) the answer is yes: the caller is already acting for
// the faction the tick belongs to.
public static class CommandAuthority
{
    public const int Version = 1;

    public static bool CanActForFaction(Faction owner)
    {
        if (Multiplayer.Client == null) return true;

        if (!Multiplayer.ExecutingCmds && (Multiplayer.Ticking || SimLongEventContext.InSimulation))
            return true;

        if (owner == null || !owner.IsPlayer || owner == Multiplayer.game?.worldComp?.spectatorFaction)
            return false;

        var acting = Multiplayer.ExecutingCmds ? Faction.OfPlayer : Multiplayer.RealPlayerFaction;
        return acting != null && acting == owner;
    }

    public static bool CanActForPawn(Pawn pawn)
    {
        return pawn != null && !pawn.Destroyed && CanActForFaction(pawn.Faction);
    }

    public static bool CanSetMedicalCare(Pawn pawn)
    {
        if (pawn == null || pawn.Destroyed || pawn.playerSettings == null) return false;

        // Prisoners and slaves are the host faction's responsibility for medical care
        return CanActForFaction(pawn.IsPrisoner || pawn.IsSlave ? pawn.HostFaction : pawn.Faction);
    }

    public static bool CanActForPlayer(int playerId)
    {
        if (Multiplayer.Client == null) return true;
        if (playerId < 0) return false;

        return playerId == (Multiplayer.ExecutingCmds
            ? TickPatch.currentExecutingCmdPlayerId
            : Multiplayer.session.playerId);
    }
}
