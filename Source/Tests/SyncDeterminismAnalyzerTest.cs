using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Multiplayer.SourceGen;

namespace Tests;

// The analyzer is the only thing that stops a wall-clock read or an unseeded Random from entering
// synchronized code, and it only runs during a client build. A test that proves it reports MP0001
// on a violating snippet is what separates a working gate from a rule that silently stopped firing.
[TestFixture]
public class SyncDeterminismAnalyzerTest
{
    private const string SyncMethodStub = """
        namespace Multiplayer.API;
        public sealed class SyncMethodAttribute : System.Attribute { }
        """;

    [Test]
    public async Task FlagsWallClockInsideSyncMethod()
    {
        var diagnostics = await Analyze("""
            using Multiplayer.API;

            public class Probe
            {
                [SyncMethod]
                public int Value() => System.DateTime.Now.Second;
            }
            """);

        Assert.That(Ids(diagnostics), Does.Contain(SyncDeterminismAnalyzer.DiagnosticId));
    }

    [Test]
    public async Task FlagsUnseededRandomInsideSyncMethod()
    {
        var diagnostics = await Analyze("""
            using Multiplayer.API;

            public class Probe
            {
                [SyncMethod]
                public int Value() => new System.Random().Next();
            }
            """);

        Assert.That(Ids(diagnostics), Does.Contain(SyncDeterminismAnalyzer.DiagnosticId));
    }

    [Test]
    public async Task IgnoresSeededRandom()
    {
        var diagnostics = await Analyze("""
            using Multiplayer.API;

            public class Probe
            {
                [SyncMethod]
                public int Value() => new System.Random(1234).Next();
            }
            """);

        Assert.That(Ids(diagnostics), Is.Empty);
    }

    [Test]
    public async Task IgnoresCodeOutsideSyncMethod()
    {
        var diagnostics = await Analyze("""
            public class Probe
            {
                public int Value() => System.DateTime.Now.Second;
            }
            """);

        Assert.That(Ids(diagnostics), Is.Empty);
    }

    private static async Task<ImmutableArray<Diagnostic>> Analyze(string source)
    {
        var compilation = CSharpCompilation.Create(
            "AnalyzerProbe",
            new[] { CSharpSyntaxTree.ParseText(source), CSharpSyntaxTree.ParseText(SyncMethodStub) },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var errors = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.That(errors, Is.Empty, "The probe must compile before the analyzer result means anything: "
            + string.Join(" | ", errors));

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new SyncDeterminismAnalyzer());
        return await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
    }

    private static IEnumerable<string> Ids(IEnumerable<Diagnostic> diagnostics) =>
        diagnostics.Select(diagnostic => diagnostic.Id);
}
