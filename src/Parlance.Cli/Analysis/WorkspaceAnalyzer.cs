using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Parlance.Analyzers.Upstream;
using Parlance.Cli.Formatting;
using Parlance.CSharp;

namespace Parlance.Cli.Analysis;

internal static class WorkspaceAnalyzer
{
    public static async Task<AnalysisOutput> AnalyzeAsync(
        IReadOnlyList<string> filePaths,
        string[]? suppressRules = null,
        int? maxDiagnostics = null,
        string? languageVersion = null,
        string targetFramework = "net10.0",
        string profile = "default",
        CancellationToken ct = default)
    {
        suppressRules ??= [];

        // Validate and load the profile (severity overrides deferred to a follow-up)
        try
        {
            _ = ProfileProvider.GetProfileContent(targetFramework, profile);
        }
        catch (ArgumentException ex)
        {
            await Console.Error.WriteLineAsync(ex.Message);
            Environment.ExitCode = 2;
            return new AnalysisOutput([], new Abstractions.AnalysisSummary(
                0, 0, 0, 0, ImmutableDictionary<string, int>.Empty, 100), 0);
        }

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

        var allAnalyzers = AnalyzerLoader.LoadAll(targetFramework);

        var analyzers = suppressRules.Length > 0
            ? allAnalyzers.Where(a => !a.SupportedDiagnostics.Any(d => suppressRules.Contains(d.Id))).ToImmutableArray()
            : allAnalyzers;

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
