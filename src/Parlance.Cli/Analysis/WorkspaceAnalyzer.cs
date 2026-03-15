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
        var suppressed = suppressRules?.ToImmutableArray() ?? [];

        // TODO: Wire curation set severity overrides into the compilation.
        // Currently validates the profile exists but discards the content.
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
            LanguageVersionResolver.Resolve(languageVersion));

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
        var analyzers = allAnalyzers.ExceptSuppressed(suppressed);

        var compilationWithAnalyzers = compilation.WithAnalyzers(analyzers);

        var roslynDiagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync(ct);

        var filtered = roslynDiagnostics
            .Where(d => !suppressed.Contains(d.Id))
            .ToList();

        var enrichedDiagnostics = filtered.ToParlanceDiagnostics();
        var summary = IdiomaticScoreCalculator.Calculate(enrichedDiagnostics);

        var fileDiagnostics = filtered.Zip(enrichedDiagnostics, (roslynDiag, parlanceDiag) =>
        {
            var filePath = roslynDiag.Location.SourceTree is not null &&
                           pathMap.TryGetValue(roslynDiag.Location.SourceTree, out var p)
                ? p
                : "unknown";
            return new FileDiagnostic(filePath, parlanceDiag);
        }).ToImmutableList();

        if (maxDiagnostics.HasValue && fileDiagnostics.Count > maxDiagnostics.Value)
            fileDiagnostics = fileDiagnostics.Take(maxDiagnostics.Value).ToImmutableList();

        return new AnalysisOutput(fileDiagnostics, summary, filePaths.Count);
    }

}
