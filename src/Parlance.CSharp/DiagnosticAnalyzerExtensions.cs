using System.Collections.Immutable;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Parlance.CSharp;

internal static class DiagnosticAnalyzerExtensions
{
    /// <summary>
    /// Returns analyzers that don't produce any of the specified suppressed rule IDs.
    /// </summary>
    public static ImmutableArray<DiagnosticAnalyzer> ExceptSuppressed(
        this IEnumerable<DiagnosticAnalyzer> analyzers,
        ImmutableArray<string> suppressRules)
    {
        if (suppressRules.IsDefaultOrEmpty)
            return analyzers.ToImmutableArray();

        return analyzers
            .Where(a => !a.SupportedDiagnostics.Any(d => suppressRules.Contains(d.Id)))
            .ToImmutableArray();
    }
}
