using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Parlance.Abstractions;
using Parlance.CSharp.Analyzers.Rules;

using AnalysisResult = Parlance.Abstractions.AnalysisResult;

namespace Parlance.CSharp;

public sealed class CSharpAnalysisEngine : IAnalysisEngine
{
    public string Language { get; } = "csharp";

    private static readonly DiagnosticAnalyzer[] Analyzers =
    [
        new PARL0001_PreferPrimaryConstructors(),
        new PARL0002_PreferCollectionExpressions(),
        new PARL0003_PreferRequiredProperties(),
        new PARL0004_UsePatternMatchingOverIsCast(),
        new PARL0005_UseSwitchExpression(),
        new PARL9001_UseSimpleUsingDeclaration(),
        new PARL9002_UseImplicitObjectCreation(),
        new PARL9003_UseDefaultLiteral(),
    ];

    public async Task<AnalysisResult> AnalyzeSourceAsync(
        string sourceCode,
        AnalysisOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new AnalysisOptions();

        var parseOptions = new CSharpParseOptions(LanguageVersionResolver.Resolve(options.LanguageVersion));
        var tree = CSharpSyntaxTree.ParseText(sourceCode, parseOptions, cancellationToken: ct);
        var compilation = CompilationFactory.Create(tree);

        var analyzersToRun = Analyzers.ExceptSuppressed(options.SuppressRules);

        var compilationWithAnalyzers = compilation.WithAnalyzers(analyzersToRun);

        var roslynDiagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync(ct);

        // Filter out suppressed rules (belt and suspenders)
        var enriched = roslynDiagnostics
            .Where(d => !options.SuppressRules.Contains(d.Id))
            .ToParlanceDiagnostics();

        // Score reflects all diagnostics so the score represents true code quality
        var summary = IdiomaticScoreCalculator.Calculate(enriched);

        // Cap the diagnostics list for presentation after scoring
        if (options.MaxDiagnostics.HasValue && enriched.Count > options.MaxDiagnostics.Value)
            enriched = enriched.Take(options.MaxDiagnostics.Value).ToImmutableList();

        return new AnalysisResult(enriched, summary);
    }

}
