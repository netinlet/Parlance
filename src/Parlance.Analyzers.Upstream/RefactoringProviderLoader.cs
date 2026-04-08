using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CodeRefactorings;

namespace Parlance.Analyzers.Upstream;

public static class RefactoringProviderLoader
{
    public static ImmutableArray<CodeRefactoringProvider> LoadAll(string targetFramework)
    {
        if (!AnalyzerDllScanner.SupportedFrameworks.Contains(targetFramework))
            throw new ArgumentException(
                $"Unsupported target framework '{targetFramework}'. Supported: {string.Join(", ", AnalyzerDllScanner.SupportedFrameworks)}",
                nameof(targetFramework));

        return AnalyzerDllScanner.ScanAssemblies(targetFramework)
            .SelectMany(a => a.DiscoverInstances<CodeRefactoringProvider>())
            .ToImmutableArray();
    }
}
