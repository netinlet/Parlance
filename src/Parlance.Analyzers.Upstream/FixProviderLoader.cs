using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CodeFixes;

namespace Parlance.Analyzers.Upstream;

public static class FixProviderLoader
{
    public static ImmutableArray<CodeFixProvider> LoadAll(string targetFramework)
    {
        if (!AnalyzerDllScanner.SupportedFrameworks.Contains(targetFramework))
        {
            throw new ArgumentException(
                $"Unsupported target framework '{targetFramework}'. Supported: {string.Join(", ", AnalyzerDllScanner.SupportedFrameworks)}",
                nameof(targetFramework));
        }

        return AnalyzerDllScanner.ScanAssemblies(targetFramework)
            .SelectMany(a => a.DiscoverInstances<CodeFixProvider>())
            .ToImmutableArray();
    }

    // Enumerates code-fix providers from explicit DLL files or directories, bypassing the
    // bundled analyzer set. Point at the directory (not just the analyzer DLL) so split
    // CodeFixes assemblies are included.
    public static ImmutableArray<CodeFixProvider> LoadFromPaths(IEnumerable<string> paths) =>
        AnalyzerDllScanner.ScanAssembliesFromPaths(paths)
            .SelectMany(a => a.DiscoverInstances<CodeFixProvider>())
            .ToImmutableArray();
}
