using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Parlance.Cli.Formatting;
using Parlance.CSharp;
using Parlance.CSharp.Analyzers.Rules;

namespace Parlance.Cli.Analysis;

internal static class WorkspaceAnalyzer
{
    private static readonly DiagnosticAnalyzer[] AllAnalyzers =
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

    public static async Task<AnalysisOutput> AnalyzeAsync(
        IReadOnlyList<string> filePaths,
        string[]? suppressRules = null,
        int? maxDiagnostics = null,
        string? languageVersion = null,
        CancellationToken ct = default)
    {
        suppressRules ??= [];

        var parseOptions = new CSharpParseOptions(
            ResolveLanguageVersion(languageVersion));

        var trees = new List<SyntaxTree>(filePaths.Count);
        var pathMap = new Dictionary<SyntaxTree, string>();

        foreach (var path in filePaths)
        {
            var source = await File.ReadAllTextAsync(path, ct);
            var tree = CSharpSyntaxTree.ParseText(source, parseOptions, path, cancellationToken: ct);
            trees.Add(tree);
            pathMap[tree] = path;
        }

        var references = CompilationFactory.LoadReferences();
        var compilation = CSharpCompilation.Create(
            assemblyName: "ParlanceCliAnalysis",
            syntaxTrees: trees,
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzers = suppressRules.Length > 0
            ? AllAnalyzers.Where(a => !a.SupportedDiagnostics.Any(d => suppressRules.Contains(d.Id))).ToArray()
            : AllAnalyzers;

        var compilationWithAnalyzers = compilation.WithAnalyzers(
            analyzers.ToImmutableArray());

        var roslynDiagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync(ct);

        var filtered = roslynDiagnostics
            .Where(d => !suppressRules.Contains(d.Id))
            .ToList();

        var enriched = DiagnosticEnricher.Enrich(filtered);
        var summary = IdiomaticScoreCalculator.Calculate(enriched);

        var fileDiagnostics = new List<FileDiagnostic>();
        for (var i = 0; i < enriched.Count; i++)
        {
            var roslynDiag = filtered[i];
            var filePath = roslynDiag.Location.SourceTree is not null &&
                           pathMap.TryGetValue(roslynDiag.Location.SourceTree, out var p)
                ? p
                : "unknown";
            fileDiagnostics.Add(new FileDiagnostic(filePath, enriched[i]));
        }

        if (maxDiagnostics.HasValue && fileDiagnostics.Count > maxDiagnostics.Value)
            fileDiagnostics = fileDiagnostics.Take(maxDiagnostics.Value).ToList();

        return new AnalysisOutput(fileDiagnostics, summary, filePaths.Count);
    }

    private static LanguageVersion ResolveLanguageVersion(string? version)
    {
        if (version is null)
            return LanguageVersion.Latest;

        if (LanguageVersionFacts.TryParse(version, out var parsed))
            return parsed;

        return LanguageVersion.Latest;
    }
}
