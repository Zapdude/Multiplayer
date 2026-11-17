using System;
using System.Collections.Generic;
using Verse;

namespace Multiplayer.Client.Compatibility;

// Session lifecycle hooks for compat mods. A retired player's per-player UI/state has to be forgotten on
// every peer in the same order, and local session cleanup runs once when the session stops.
public static class SessionLifecycle
{
    private sealed record Hooks(Action<int> Retired, Action Stopping);

    public const int Version = 1;

    private static readonly SortedDictionary<string, Hooks> hooks = new(StringComparer.Ordinal);

    // The player whose ordered command is executing, or -1 outside command execution.
    public static int ExecutingPlayerId =>
        !Multiplayer.ExecutingCmds ? -1 : TickPatch.currentExecutingCmdPlayerId;

    public static int LocalPlayerId => Multiplayer.session?.playerId ?? -1;

    public static void Register(string key, Action<int> orderedPlayerRetired, Action localSessionStopping)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("A stable registration key is required", nameof(key));
        if (orderedPlayerRetired == null && localSessionStopping == null)
            throw new ArgumentException("At least one callback is required");

        hooks.Add(key, new Hooks(orderedPlayerRetired, localSessionStopping));
    }

    internal static void PlayerRetired(int playerId)
    {
        if (Multiplayer.session == null || !Multiplayer.ExecutingCmds || playerId < 0)
            throw new InvalidOperationException("Player retirement requires an ordered multiplayer command");

        foreach (var hook in hooks.Values)
            hook.Retired?.Invoke(playerId);
    }

    internal static void Stopping()
    {
        RoomSettings.Stopping();

        foreach (var hook in hooks)
        {
            try
            {
                hook.Value.Stopping?.Invoke();
            }
            catch (Exception e)
            {
                Log.Error($"Multiplayer local cleanup failed for {hook.Key}: {e}");
            }
        }
    }
}
