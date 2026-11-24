using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace Multiplayer.Client.Desyncs;

/// <summary>
/// Serialises every applied Harmony patch once per session. Patching is static after mods load, so the
/// result is cached; the point is to answer "which patches did each peer have, from which mod" in a
/// report, not to track changes over time.
/// </summary>
public static class PatchInventory
{
    private static string cached;

    public static string Generate()
    {
        if (cached != null)
            return cached;

        try
        {
            cached = Collect();
        }
        catch (Exception e)
        {
            // A report must still be written when the inventory cannot be built.
            cached = "{\"error\":" + PatchInventoryFormat.Quote($"{e.GetType().Name}: {e.Message}") + "}";
        }

        return cached;
    }

    private static string Collect()
    {
        var packages = PackageByAssembly();
        var rows = new List<PatchRow>();
        var owners = new SortedSet<string>(StringComparer.Ordinal);
        var patchedMethods = 0;

        foreach (var target in Harmony.GetAllPatchedMethods())
        {
            var info = Harmony.GetPatchInfo(target);
            if (info == null) continue;

            patchedMethods++;
            Add(target, info.Prefixes, "Prefix");
            Add(target, info.Postfixes, "Postfix");
            Add(target, info.Transpilers, "Transpiler");
            Add(target, info.Finalizers, "Finalizer");
        }

        // Sorted so two peers with the same patch set produce the same file, byte for byte. Patch
        // application order is deliberately left out: it can differ between peers without meaning
        // anything, and would turn the inventory into a false desync signal.
        rows.Sort(static (a, b) =>
        {
            var c = string.CompareOrdinal(a.Target, b.Target);
            if (c != 0) return c;
            c = string.CompareOrdinal(a.Kind, b.Kind);
            if (c != 0) return c;
            c = string.CompareOrdinal(a.Owner, b.Owner);
            if (c != 0) return c;
            c = a.Priority.CompareTo(b.Priority);
            return c != 0 ? c : string.CompareOrdinal(a.PatchMethod, b.PatchMethod);
        });

        return PatchInventoryFormat.Json(
            rows,
            omittedBefore: 0,
            harmonyVersion: typeof(Harmony).Assembly.GetName().Version?.ToString() ?? "unknown",
            patchedMethodCount: patchedMethods,
            patchCount: rows.Count,
            owners: owners);

        void Add(MethodBase target, IEnumerable<Patch> patches, string kind)
        {
            foreach (var patch in patches)
            {
                var method = patch.PatchMethod;
                var assembly = method?.DeclaringType?.Assembly;

                owners.Add(patch.owner ?? "");
                rows.Add(new PatchRow(
                    Target: Describe(target),
                    TargetAssembly: target?.DeclaringType?.Assembly.GetName().Name ?? "",
                    TargetToken: TokenOf(target),
                    Kind: kind,
                    Owner: patch.owner ?? "",
                    Priority: patch.priority,
                    PatchMethod: Describe(method),
                    PatchAssembly: assembly?.GetName().Name ?? "",
                    PatchToken: TokenOf(method),
                    Package: assembly != null && packages.TryGetValue(assembly, out var package) ? package : ""));
            }
        }
    }

    private static Dictionary<Assembly, string> PackageByAssembly()
    {
        var map = new Dictionary<Assembly, string>();

        try
        {
            foreach (var mod in LoadedModManager.RunningMods)
            foreach (var assembly in mod.assemblies.loadedAssemblies)
                if (!map.ContainsKey(assembly))
                    map[assembly] = mod.PackageIdPlayerFacing;
        }
        catch (Exception e)
        {
            Log.Warning($"[MP] Patch inventory could not resolve mod packages: {e.Message}");
        }

        return map;
    }

    private static string Describe(MethodBase method)
    {
        if (method == null) return "";

        var parameters = string.Join(",", method.GetParameters().Select(p => p.ParameterType.Name));
        return $"{method.DeclaringType?.FullName}.{method.Name}({parameters})";
    }

    private static int TokenOf(MethodBase method)
    {
        try
        {
            return method?.MetadataToken ?? 0;
        }
        catch (InvalidOperationException)
        {
            // Dynamic methods have no metadata token.
            return 0;
        }
    }
}
