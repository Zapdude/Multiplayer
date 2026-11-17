using Verse;

namespace Multiplayer.Client.Compatibility;

// Surface compat mods use to participate in background joinpoints. This fork has no checkpoint system, so the
// surface is present but inert: values are never captured or restored, and the local peer behaves as a normal
// session participant.
public static class CheckpointState
{
    public const int Version = 1;
    public const int WorkerRoleVersion = 1;

    public static bool Active => false;

    public static bool IsWorker => false;

    public static bool CanPerformExternalSideEffects => true;

    public static void RegisterLocalMote(ThingDef def, string contract)
    {
    }

    // No-op mirror of the exact checkpoint field look. Compat mods call it only when Active is true.
    public static void Look(ref int value, string label)
    {
    }
}
