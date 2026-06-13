using System.Collections.Concurrent;
using System.Collections.Immutable;
using Parlance.Analyzers.Upstream;

namespace Parlance.Analysis;

public sealed record AnalyzerProviderResult(
    AnalyzerComponents Components,
    ImmutableList<DllLoadFailure> Failures);

/// <summary>
/// Aggregates all <see cref="IAnalyzerSource"/>s into a single merged <see cref="AnalyzerComponents"/>
/// set. Sources are sorted by descending <see cref="IAnalyzerSource.Priority"/> so that higher-priority
/// sources (local=100, global=50) win over lower-priority ones (bundled=20, roslyn-features=10) on
/// type-name collision. Results are cached by <c>(targetFramework, repoPath)</c>.
/// </summary>
public sealed class AnalyzerProvider(IEnumerable<IAnalyzerSource> sources)
{
    private readonly IReadOnlyList<IAnalyzerSource> _sources = [.. sources];
    private readonly ConcurrentDictionary<string, AnalyzerProviderResult> _cache =
        new(StringComparer.Ordinal);

    public AnalyzerProviderResult GetComponents(string targetFramework, string repoPath)
    {
        var key = $"{targetFramework}|{repoPath}";
        return _cache.GetOrAdd(key, _ => Merge(targetFramework, repoPath));
    }

    public ImmutableList<string> GetExternalSourceNotices(string repoPath) =>
        _sources.OfType<ITrustNoticeSource>()
                .SelectMany(s => s.GetTrustNotices(repoPath))
                .ToImmutableList();

    private AnalyzerProviderResult Merge(string targetFramework, string repoPath)
    {
        // Sort descending by priority — higher number wins on type-name collision.
        // We keep the first occurrence per type name, so process highest priority first.
        var ordered = _sources.OrderByDescending(s => s.Priority);

        var seenAnalyzers = new HashSet<string>(StringComparer.Ordinal);
        var seenFixes = new HashSet<string>(StringComparer.Ordinal);
        var seenRefactorings = new HashSet<string>(StringComparer.Ordinal);

        var analyzers = ImmutableArray.CreateBuilder<Microsoft.CodeAnalysis.Diagnostics.DiagnosticAnalyzer>();
        var fixes = ImmutableArray.CreateBuilder<Microsoft.CodeAnalysis.CodeFixes.CodeFixProvider>();
        var refactorings = ImmutableArray.CreateBuilder<Microsoft.CodeAnalysis.CodeRefactorings.CodeRefactoringProvider>();
        var failures = ImmutableList.CreateBuilder<DllLoadFailure>();

        foreach (var source in ordered)
        {
            var result = source.Load(targetFramework, repoPath);
            failures.AddRange(result.Failures);

            foreach (var analyzer in result.Components.Analyzers)
            {
                var typeName = analyzer.GetType().FullName ?? analyzer.GetType().Name;
                if (seenAnalyzers.Add(typeName))
                    analyzers.Add(analyzer);
            }

            foreach (var fix in result.Components.FixProviders)
            {
                var typeName = fix.GetType().FullName ?? fix.GetType().Name;
                if (seenFixes.Add(typeName))
                    fixes.Add(fix);
            }

            foreach (var refactoring in result.Components.RefactoringProviders)
            {
                var typeName = refactoring.GetType().FullName ?? refactoring.GetType().Name;
                if (seenRefactorings.Add(typeName))
                    refactorings.Add(refactoring);
            }
        }

        return new AnalyzerProviderResult(
            new AnalyzerComponents(analyzers.ToImmutable(), fixes.ToImmutable(), refactorings.ToImmutable()),
            failures.ToImmutable());
    }
}
