using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Multiplayer.SourceGen;

/// <summary>
/// Synchronized methods run on every client and their results are compared. A value that differs
/// between processes - wall clock, a fresh GUID, an unseeded random, a tick counter - produces a
/// desync that only shows up with two machines connected, which makes it expensive to find later.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SyncDeterminismAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "MP0001";

    private const string Category = "Multiplayer.Determinism";

    private static readonly DiagnosticDescriptor NondeterministicApi = new(
        DiagnosticId,
        "Nondeterministic API in synchronized code",
        "'{0}' differs between clients; synchronized code must produce the same result everywhere",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly ImmutableArray<string> SyncAttributes =
        ["SyncWorker", "SyncMethod", "SyncWorkerAttribute", "SyncMethodAttribute"];

    // Members whose value is not the same on two machines. Properties matter as much as methods:
    // DateTime.Now and Environment.TickCount never appear as invocations.
    private static readonly HashSet<string> ForbiddenMembers = new(System.StringComparer.Ordinal)
    {
        "System.DateTime.Now",
        "System.DateTime.UtcNow",
        "System.DateTimeOffset.Now",
        "System.DateTimeOffset.UtcNow",
        "System.Guid.NewGuid",
        "System.Environment.TickCount",
        "System.Environment.TickCount64"
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [NondeterministicApi];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
        context.RegisterSyntaxNodeAction(AnalyzeObjectCreation, SyntaxKind.ObjectCreationExpression);
    }

    private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
    {
        var access = (MemberAccessExpressionSyntax)context.Node;

        if (!IsInsideSynchronizedMethod(access))
            return;

        if (context.SemanticModel.GetSymbolInfo(access).Symbol is not { ContainingType: { } type } member)
            return;

        var qualified = $"{type.ToDisplayString()}.{member.Name}";

        if (ForbiddenMembers.Contains(qualified))
            context.ReportDiagnostic(Diagnostic.Create(NondeterministicApi, access.GetLocation(), qualified));
    }

    private static void AnalyzeObjectCreation(SyntaxNodeAnalysisContext context)
    {
        var creation = (ObjectCreationExpressionSyntax)context.Node;

        if (!IsInsideSynchronizedMethod(creation))
            return;

        if (context.SemanticModel.GetSymbolInfo(creation).Symbol is not IMethodSymbol constructor)
            return;

        // new Random() seeds from the clock, so two clients get different sequences.
        if (constructor.ContainingType?.ToDisplayString() == "System.Random" && creation.ArgumentList is not { Arguments.Count: > 0 })
            context.ReportDiagnostic(Diagnostic.Create(NondeterministicApi, creation.GetLocation(), "new Random()"));
    }

    private static bool IsInsideSynchronizedMethod(SyntaxNode node)
    {
        foreach (var ancestor in node.Ancestors())
        {
            if (ancestor is not BaseMethodDeclarationSyntax method)
                continue;

            foreach (var list in method.AttributeLists)
            foreach (var attribute in list.Attributes)
                if (SyncAttributes.Contains(SimpleName(attribute.Name)))
                    return true;

            return false;
        }

        return false;
    }

    private static string SimpleName(NameSyntax name) => name switch
    {
        QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
        AliasQualifiedNameSyntax alias => alias.Name.Identifier.ValueText,
        SimpleNameSyntax simple => simple.Identifier.ValueText,
        _ => name.ToString()
    };
}
