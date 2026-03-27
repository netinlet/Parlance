using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using ParlanceDiagnostic = Parlance.Abstractions.Diagnostic;
using ParlanceLocation = Parlance.Abstractions.Location;
using ParlanceSeverity = Parlance.Abstractions.DiagnosticSeverity;
using RoslynDiagnostic = Microsoft.CodeAnalysis.Diagnostic;

namespace Parlance.CSharp;

public static class DiagnosticEnricher
{
    public static ImmutableList<ParlanceDiagnostic> ToParlanceDiagnostics(this IEnumerable<RoslynDiagnostic> diagnostics)
    {
        var result = new List<ParlanceDiagnostic>();

        foreach (var d in diagnostics)
        {
            var lineSpan = d.Location.GetLineSpan();
            var start = lineSpan.StartLinePosition;
            var end = lineSpan.EndLinePosition;

            var location = new ParlanceLocation(
                Line: start.Line + 1,
                Column: start.Character + 1,
                EndLine: end.Line + 1,
                EndColumn: end.Character + 1);

            var severity = d.Severity switch
            {
                DiagnosticSeverity.Error => ParlanceSeverity.Error,
                DiagnosticSeverity.Warning => ParlanceSeverity.Warning,
                DiagnosticSeverity.Info => ParlanceSeverity.Suggestion,
                DiagnosticSeverity.Hidden => ParlanceSeverity.Silent,
                _ => ParlanceSeverity.Silent,
            };

            var meta = RuleMetadataProvider.GetMetadata(d.Id);

            result.Add(new ParlanceDiagnostic(
                RuleId: d.Id,
                Category: meta?.Category ?? d.Descriptor.Category,
                Severity: severity,
                Message: d.GetMessage(),
                Location: location,
                Rationale: meta?.Rationale,
                SuggestedFix: meta?.SuggestedFix));
        }

        return result.ToImmutableList();
    }
}
