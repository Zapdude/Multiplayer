using System.Runtime.CompilerServices;
using HarmonyLib;

namespace Tests;

// Probes the question the TransporterContentsDropGuard backport raised: RimWorld 1.6 declares
// OnDropThing(Thing t, int count) by value, while the patch uses `ref int count`. Harmony turns out to
// tolerate that - the prefix runs, mutation reaches the original, and __args works too - so the 1.6
// transporter patch needed no change. These tests record that, so the next reader does not re-derive it
// or "fix" a patch that already works.
[TestFixture]
[NonParallelizable]
public class RefParameterBindingProbeTest
{
    private static bool prefixRan;
    private static int seen;

    [Test]
    public void RefPrefixBindsToAByValueParameter()
    {
        var harmony = new Harmony("Tests.RefPrefixByValue");
        string observed;

        try
        {
            harmony.Patch(
                AccessTools.Method(typeof(RefParameterBindingProbeTest), nameof(TargetWithCount)),
                prefix: new HarmonyMethod(typeof(RefPrefixOnByValue), nameof(RefPrefixOnByValue.Prefix)));
            observed = "Harmony accepted the ref prefix";
        }
        catch (Exception e)
        {
            observed = $"Harmony threw {e.GetType().Name}: {e.Message}";
        }

        try
        {
            prefixRan = false;
            TargetWithCount(5);
        }
        finally
        {
            harmony.UnpatchAll("Tests.RefPrefixByValue");
        }

        Assert.That(prefixRan, Is.True,
            $"A `ref int` prefix did not run against a by-value `int` parameter. Observed: {observed}");
    }

    [Test]
    public void ArgsPrefixCanChangeAByValueParameter()
    {
        var harmony = new Harmony("Tests.ArgsByValue");

        try
        {
            harmony.Patch(
                AccessTools.Method(typeof(RefParameterBindingProbeTest), nameof(TargetWithCount)),
                prefix: new HarmonyMethod(typeof(ArgsPrefix), nameof(ArgsPrefix.Prefix)));
        }
        finally
        {
            // asserted below, after the call
        }

        try
        {
            seen = 0;
            TargetWithCount(5);
        }
        finally
        {
            harmony.UnpatchAll("Tests.ArgsByValue");
        }

        Assert.That(seen, Is.EqualTo(99), "__args mutation did not reach the original body");
    }

    [Test]
    public void RefPrefixMutationReachesAByValueParameter()
    {
        var harmony = new Harmony("Tests.RefPrefixMutation");

        try
        {
            harmony.Patch(
                AccessTools.Method(typeof(RefParameterBindingProbeTest), nameof(TargetWithCount)),
                prefix: new HarmonyMethod(typeof(RefPrefixMutating), nameof(RefPrefixMutating.Prefix)));
            seen = 0;
            TargetWithCount(5);
        }
        finally
        {
            harmony.UnpatchAll("Tests.RefPrefixMutation");
        }

        Assert.That(seen, Is.EqualTo(99),
            "A `ref` prefix parameter bound to a by-value argument, but mutating it did not reach the original. " +
            "The TransporterContentsDropGuard clamp is a silent no-op on 1.6 if this is 5.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void TargetWithCount(int count) => seen = count;

    private static class RefPrefixMutating
    {
        public static void Prefix(ref int count) => count = 99;
    }

    private static class RefPrefixOnByValue
    {
        public static bool Prefix(ref int count)
        {
            prefixRan = true;
            return true;
        }
    }

    private static class ArgsPrefix
    {
        public static void Prefix(object[] __args) => __args[0] = 99;
    }
}
