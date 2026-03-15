using System.Collections.Immutable;
using Parlance.Abstractions;

namespace Parlance.CSharp;

internal static class IdiomaticScoreCalculator
{
    public static AnalysisSummary Calculate(ImmutableList<Diagnostic> diagnostics)
    {
        var errors = 0;
        var warnings = 0;
        var suggestions = 0;
        var byCategory = new Dictionary<string, int>();

        foreach (var d in diagnostics)
        {
            switch (d.Severity)
            {
                case DiagnosticSeverity.Error:
                    errors++;
                    break;
                case DiagnosticSeverity.Warning:
                    warnings++;
                    break;
                case DiagnosticSeverity.Suggestion:
                    suggestions++;
                    break;
            }

            if (!byCategory.TryAdd(d.Category, 1))
                byCategory[d.Category]++;
        }

        var deduction = errors * 10 + warnings * 5 + suggestions * 2;
        var score = Math.Max(0, 100 - deduction);

        return new AnalysisSummary(
            TotalDiagnostics: diagnostics.Count,
            Errors: errors,
            Warnings: warnings,
            Suggestions: suggestions,
            ByCategory: byCategory.ToImmutableDictionary(),
            IdiomaticScore: score);
    }
}
