using System.Collections.Immutable;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Parlance.Analyzers.Upstream;

public static class AnalyzerLoader
{
    public static ImmutableArray<DiagnosticAnalyzer> LoadAll(string targetFramework)
    {
        if (!AnalyzerDllScanner.SupportedFrameworks.Contains(targetFramework))
            throw new ArgumentException(
                $"Unsupported target framework '{targetFramework}'. Supported: {string.Join(", ", AnalyzerDllScanner.SupportedFrameworks)}",
                nameof(targetFramework));

        return AnalyzerDllScanner.ScanAssemblies(targetFramework)
            .SelectMany(a => a.DiscoverInstances<DiagnosticAnalyzer>())
            .ToImmutableArray();
    }
}
