using System;
using System.Collections.Generic;

namespace Multiplayer.Client;

/// <summary>
/// Runs a patched operation once per faction, with that faction applied as the context.
/// The repeating flag is what keeps the operation's own call back into the patch from starting a
/// second pass, so it must stay held for the whole loop and be released even when a faction throws -
/// otherwise that patch silently stops replicating for the rest of the session.
/// </summary>
public static class FactionRepeat
{
    /// <returns>true when the original should still run, false when this call drove the factions.</returns>
    public static bool Run<T>(
        IDictionary<int, T>? factionData,
        Action<T> process,
        int spectatorId,
        Action<int> push,
        Action pop,
        Action<int, Exception> onError,
        ref bool repeating)
    {
        if (repeating || factionData == null)
            return true;

        repeating = true;
        try
        {
            foreach (var (id, data) in factionData)
            {
                if (id == spectatorId)
                    continue;

                // A push that fails leaves nothing to pop and cannot be recovered per faction.
                push(id);
                try
                {
                    process(data);
                }
                catch (Exception e)
                {
                    onError(id, e);
                }
                finally
                {
                    pop();
                }
            }
        }
        finally
        {
            repeating = false;
        }

        return false;
    }
}
