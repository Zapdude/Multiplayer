using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Tests;

// Harmony identifies every patch by its class name, and it fails quietly: a duplicate name
// shows up as an ambiguous log line, a patch class with no entry point simply never runs, and
// a Postfix with a wrong return type is skipped without an error. All three have cost us time,
// and none of them is visible to the compiler.
[TestFixture]
public class ClientPatchManifestTest
{
    private static readonly HashSet<string> EntryPointNames = new(StringComparer.Ordinal)
    {
        "Prefix", "Postfix", "Transpiler", "Finalizer", "Prepare", "Cleanup", "TargetMethod", "TargetMethods"
    };

    private static readonly HashSet<string> EntryPointAttributes = new(StringComparer.Ordinal)
    {
        "HarmonyPrefix", "HarmonyPostfix", "HarmonyTranspiler", "HarmonyFinalizer", "HarmonyPrepare", "HarmonyCleanup"
    };

    [Test]
    public void PatchClassNamesAreUnique()
    {
        var duplicates = DuplicateNames(PatchClasses());

        Assert.That(duplicates, Is.Empty,
            "Two patch classes with the same name make Harmony's output ambiguous. Rename one.");
    }

    [Test]
    public void EveryPatchClassHasAnEntryPoint()
    {
        var missing = MissingEntryPoints(PatchClasses());

        Assert.That(missing, Is.Empty,
            "A [HarmonyPatch] class with no Prefix/Postfix/Transpiler/Finalizer/TargetMethod never runs.");
    }

    [Test]
    public void PostfixesDoNotReturnBool()
    {
        // Harmony lets a Postfix return a replacement for the original result (IEnumerable<T> is used
        // here), but bool is the Prefix convention. On a void-returning method a bool Postfix is
        // silently ignored, which is how a swapped Prefix/Postfix pair hides.
        var offenders = BoolPostfixes(PatchClasses());

        Assert.That(offenders, Is.Empty, "A Postfix returning bool is almost always a mis-typed Prefix.");
    }

    [Test]
    public void TranspilersReturnAnInstructionSequence()
    {
        var offenders = PatchClasses()
            .SelectMany(p => EntryPoints(p.Decl)
                .Where(m => m.Identifier.ValueText == "Transpiler" && IsVoid(m.ReturnType))
                .Select(m => $"{Describe(p)}.{m.Identifier.ValueText} returns void"))
            .ToList();

        Assert.That(offenders, Is.Empty, "A Transpiler must return instructions, not void.");
    }

    [Test]
    public void PatchClassesWereFound()
    {
        // Guards the parser itself: a silent parse failure would make every test above pass.
        Assert.That(PatchClasses().Count(), Is.GreaterThan(100));
    }

    [Test]
    public void ChecksFireOnSyntheticSources()
    {
        // The real tree is clean, so the checks above would also pass if their logic broke.
        // Feed each check a source that must trip it.
        var duplicate = new[]
        {
            ("a.cs", "[HarmonyPatch] public class DuplicatePatch { static bool Prefix() => true; }"),
            ("b.cs", "[HarmonyPatch] public class DuplicatePatch { static void Postfix() { } }")
        };
        Assert.That(DuplicateNames(PatchClasses(duplicate)), Has.Count.EqualTo(1),
            "The duplicate-name check must fire on two patch classes with the same name");

        var missing = new[]
        {
            ("c.cs", "[HarmonyPatch] public class NoEntryPointPatch { static void Helper() { } }")
        };
        Assert.That(MissingEntryPoints(PatchClasses(missing)), Has.Count.EqualTo(1),
            "The entry-point check must fire on a patch class with no entry point");

        var boolPostfix = new[]
        {
            ("d.cs", "[HarmonyPatch] public class BoolPostfixPatch { static bool Postfix() => true; }")
        };
        Assert.That(BoolPostfixes(PatchClasses(boolPostfix)), Has.Count.EqualTo(1),
            "The Postfix check must fire on a bool-returning Postfix");
    }

    private static List<string> DuplicateNames(IEnumerable<(ClassDeclarationSyntax Decl, string File)> patches) =>
        patches.GroupBy(p => p.Decl.Identifier.ValueText)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key}: {string.Join(", ", group.Select(Describe))}")
            .ToList();

    private static List<string> MissingEntryPoints(IEnumerable<(ClassDeclarationSyntax Decl, string File)> patches) =>
        patches.Where(patch => !EntryPoints(patch.Decl).Any()).Select(Describe).ToList();

    private static List<string> BoolPostfixes(IEnumerable<(ClassDeclarationSyntax Decl, string File)> patches) =>
        patches.SelectMany(patch => EntryPoints(patch.Decl)
                .Where(method => method.Identifier.ValueText == "Postfix" && IsBool(method.ReturnType))
                .Select(method => $"{Describe(patch)}.{method.Identifier.ValueText} returns {method.ReturnType}"))
            .ToList();

    private static IEnumerable<(ClassDeclarationSyntax Decl, string File)> PatchClasses() =>
        PatchClasses(ClientSourceFiles().Select(file => (file, File.ReadAllText(file))));

    private static IEnumerable<(ClassDeclarationSyntax Decl, string File)> PatchClasses(
        IEnumerable<(string File, string Text)> sources)
    {
        foreach (var (file, text) in sources)
        {
            var tree = CSharpSyntaxTree.ParseText(text, path: file);

            foreach (var decl in tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
                if (HasHarmonyPatch(decl))
                    yield return (decl, file);
        }
    }

    private static IEnumerable<MethodDeclarationSyntax> EntryPoints(ClassDeclarationSyntax cls) =>
        cls.Members.OfType<MethodDeclarationSyntax>()
            .Where(m => EntryPointNames.Contains(m.Identifier.ValueText) || HasAnyAttribute(m, EntryPointAttributes));

    private static bool HasHarmonyPatch(MemberDeclarationSyntax member) =>
        HasAnyAttribute(member, ["HarmonyPatch"]);

    private static bool HasAnyAttribute(MemberDeclarationSyntax member, IEnumerable<string> names) =>
        member.AttributeLists
            .SelectMany(list => list.Attributes)
            .Any(attr => names.Contains(SimpleName(attr.Name)));

    private static string SimpleName(NameSyntax name) => name switch
    {
        QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
        AliasQualifiedNameSyntax alias => alias.Name.Identifier.ValueText,
        SimpleNameSyntax simple => simple.Identifier.ValueText,
        _ => name.ToString()
    };

    private static bool IsVoid(TypeSyntax type) => type is PredefinedTypeSyntax pre && pre.Keyword.IsKind(SyntaxKind.VoidKeyword);

    private static bool IsBool(TypeSyntax type) => type is PredefinedTypeSyntax pre && pre.Keyword.IsKind(SyntaxKind.BoolKeyword);

    private static string Describe((ClassDeclarationSyntax Decl, string File) patch) =>
        $"{RelativePath(patch.File)}:{patch.Decl.Identifier.ValueText}";

    private static IEnumerable<string> ClientSourceFiles()
    {
        var root = ClientSourceRoot();
        return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsUnderBuildDirectory(path));
    }

    private static bool IsUnderBuildDirectory(string path)
    {
        var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(s => s is "bin" or "obj");
    }

    private static string ClientSourceRoot()
    {
        for (var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir != null; dir = dir.Parent)
        {
            var client = Path.Combine(dir.FullName, "Client");
            if (Directory.Exists(client) && Directory.Exists(Path.Combine(client, "Patches")))
                return client;
        }

        throw new DirectoryNotFoundException(
            $"Could not find Source/Client above {TestContext.CurrentContext.TestDirectory}");
    }

    private static string RelativePath(string fullPath)
    {
        var source = Directory.GetParent(ClientSourceRoot())!.FullName;
        return Path.GetRelativePath(source, fullPath).Replace('\\', '/');
    }
}
