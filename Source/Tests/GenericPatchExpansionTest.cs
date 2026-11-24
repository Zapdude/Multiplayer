using System.Reflection;
using HarmonyLib;

namespace Tests;

// A1: a generic method definition cannot be patched as written, and our generator - which just runs
// Harmony's CreateClassProcessor per [HarmonyPatch] type - expands nothing itself. This test pins the
// current behaviour so the answer is not re-derived: today the patch throws, our generator logs
// "FAIL: <type>" at load, and closed instantiations are never patched.
//
// If this ever fails because prefixRan became true, Harmony started expanding and A1 can be closed as
// already handled. If it fails because the generator stopped logging, the hole became silent.
[TestFixture]
[NonParallelizable]
public class GenericPatchExpansionTest
{
    private static bool prefixRan;

    [Test]
    public void OpenGenericTargetIsNotExpandedIntoItsClosedInstantiations()
    {
        var harmony = new Harmony("Tests.GenericPatchExpansion");
        string observed;

        try
        {
            harmony.CreateClassProcessor(typeof(OpenGenericPatch)).Patch();
            observed = "accepted the open generic target";
        }
        catch (Exception e)
        {
            observed = $"threw {e.GetType().Name}";
        }

        try
        {
            prefixRan = false;
            _ = Echo(7);
        }
        finally
        {
            harmony.UnpatchAll("Tests.GenericPatchExpansion");
        }

        Assert.That(prefixRan, Is.False,
            $"CreateClassProcessor {observed}, and the closed instantiation was not intercepted. " +
            "That is the current state: A1 is a feature (add expansion, ordered by type name), not a test. " +
            "If prefixRan is now true, Harmony expands by itself and A1 should be closed.");
    }

    public static T Echo<T>(T value) => value;

    [HarmonyPatch]
    private static class OpenGenericPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return typeof(GenericPatchExpansionTest).GetMethod(
                            nameof(Echo), BindingFlags.Public | BindingFlags.Static)
                        ?? throw new InvalidOperationException("Echo was removed");
        }

        private static bool Prefix()
        {
            prefixRan = true;
            return true;
        }
    }
}
