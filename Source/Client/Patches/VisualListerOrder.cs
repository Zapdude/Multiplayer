using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace Multiplayer.Client.Patches;

// ListerThings.Remove swaps the removed thing with the last entry for speed. Local-only visual motes sit in the
// same list, so their position differs per client and the swapped element (and therefore the list order other
// code iterates) can differ too. Keep local-only motes out of the swap.
[HarmonyPatch(typeof(ListerThings), "Remove")]
internal static class VisualListerOrder
{
    private static readonly MethodInfo NativeRemoveSwap = AccessTools.DeclaredMethod(
        typeof(ListerThings), "RemoveSwap", new[] { typeof(List<Thing>), typeof(Thing) });

    private static readonly Action<List<Thing>, Thing> RemoveNative =
        AccessTools.MethodDelegate<Action<List<Thing>, Thing>>(NativeRemoveSwap, null, true);

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var calls = 0;

        foreach (var instruction in instructions)
        {
            if (instruction.Calls(NativeRemoveSwap))
            {
                calls++;
                if (calls == 2)
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0)
                        .MoveLabelsFrom(instruction)
                        .MoveBlocksFrom(instruction);
                    yield return new CodeInstruction(OpCodes.Call,
                        AccessTools.DeclaredMethod(typeof(VisualListerOrder), nameof(RemoveFromGroup)));
                    continue;
                }
            }

            yield return instruction;
        }

        if (calls != 2)
            throw new InvalidOperationException($"ListerThings.Remove changed: expected two RemoveSwap sites, got {calls}");
    }

    internal static bool IsLocalVisualMote(Thing thing)
    {
        if (thing is Mote && !thing.def.isSaveable)
            return !thing.def.HasThingIDNumber;

        return false;
    }

    internal static void RemoveFromGroup(List<Thing> list, Thing thing, ListerThings owner)
    {
        if (Multiplayer.Client == null || list != owner.AllThings)
        {
            RemoveNative(list, thing);
            return;
        }

        var index = list.IndexOf(thing);
        if (index < 0) return;

        if (IsLocalVisualMote(thing))
        {
            list.RemoveAt(index);
            return;
        }

        var last = list.Count - 1;
        while (last > index && IsLocalVisualMote(list[last]))
            last--;

        list[index] = list[last];
        list.RemoveAt(last);
    }
}
