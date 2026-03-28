using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CodeFixes;

namespace Parlance.Analyzers.Upstream;

public static class FixProviderLoader
{
    public static ImmutableArray<CodeFixProvider> LoadAll(string targetFramework)
    {
        if (!AnalyzerDllScanner.SupportedFrameworks.Contains(targetFramework))
            throw new ArgumentException(
                $"Unsupported target framework '{targetFramework}'. Supported: {string.Join(", ", AnalyzerDllScanner.SupportedFrameworks)}",
                nameof(targetFramework));

        return AnalyzerDllScanner.ScanAssemblies(targetFramework)
            .SelectMany(a => a.DiscoverInstances<CodeFixProvider>())
            .ToImmutableArray();
    }
}
