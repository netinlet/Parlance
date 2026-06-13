using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CodeRefactorings;

namespace Parlance.Analyzers.Upstream;

public static class RefactoringProviderLoader
{
    public static ImmutableArray<CodeRefactoringProvider> LoadAll(string targetFramework)
    {
        if (!AnalyzerDllScanner.SupportedFrameworks.Contains(targetFramework))
        {
            throw new ArgumentException(
                $"Unsupported target framework '{targetFramework}'. Supported: {string.Join(", ", AnalyzerDllScanner.SupportedFrameworks)}",
                nameof(targetFramework));
        }

        return AnalyzerDllScanner.ScanAssemblies(targetFramework)
            .SelectMany(a => a.DiscoverInstances<CodeRefactoringProvider>())
            .ToImmutableArray();
    }

    // Enumerates refactoring providers from explicit DLL files or directories, bypassing the
    // bundled analyzer set. Point at the directory (not just the analyzer DLL) so split
    // CodeRefactorings assemblies are included.
    public static ImmutableArray<CodeRefactoringProvider> LoadFromPaths(IEnumerable<string> paths) =>
        AnalyzerDllScanner.ScanAssembliesFromPaths(paths)
            .SelectMany(a => a.DiscoverInstances<CodeRefactoringProvider>())
            .ToImmutableArray();
}
