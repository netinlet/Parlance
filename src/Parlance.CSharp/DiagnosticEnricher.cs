using System.Collections.Frozen;
using Microsoft.CodeAnalysis;
using RoslynDiagnostic = Microsoft.CodeAnalysis.Diagnostic;
using ParlanceDiagnostic = Parlance.Abstractions.Diagnostic;
using ParlanceSeverity = Parlance.Abstractions.DiagnosticSeverity;
using ParlanceLocation = Parlance.Abstractions.Location;

namespace Parlance.CSharp;

internal static class DiagnosticEnricher
{
    private static readonly FrozenDictionary<string, RuleMetadata> Metadata =
        new Dictionary<string, RuleMetadata>
        {
            ["PARL0001"] = new(
                "Modernization",
                "Primary constructors (C# 12+) combine type declaration and constructor into a single concise form. When a constructor only assigns parameters to fields or properties, a primary constructor removes the boilerplate.",
                "Convert to a primary constructor by moving parameters to the type declaration."),
            ["PARL0002"] = new(
                "Modernization",
                "Collection expressions (C# 12+) provide a unified syntax for creating collections. They are more concise and let the compiler choose the optimal collection type.",
                "Replace with a collection expression: [element1, element2, ...]."),
            ["PARL0003"] = new(
                "Modernization",
                "The 'required' modifier (C# 11+) enforces that callers set a property during initialization. This is clearer than constructor-only initialization for simple DTOs and reduces constructor boilerplate.",
                "Remove the constructor parameter and add the 'required' modifier to the property."),
            ["PARL0004"] = new(
                "PatternMatching",
                "Pattern matching with 'is' (C# 7+) combines type checking and variable declaration in one expression. It is more concise than separate 'is' check followed by a cast, avoids the double type-check, and is the idiomatic modern C# approach.",
                "Use 'if (obj is Type name)' instead of separate is-check and cast."),
            ["PARL0005"] = new(
                "PatternMatching",
                "Switch expressions (C# 8+) are more concise than switch statements when every branch returns a value. They enforce exhaustiveness and make the data-flow intent clearer.",
                "Convert the switch statement to a switch expression."),
        }.ToFrozenDictionary();

    public static IReadOnlyList<ParlanceDiagnostic> Enrich(
        IReadOnlyList<RoslynDiagnostic> diagnostics)
    {
        var result = new List<ParlanceDiagnostic>(diagnostics.Count);

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

            var hasMetadata = Metadata.TryGetValue(d.Id, out var meta);

            result.Add(new ParlanceDiagnostic(
                RuleId: d.Id,
                Category: hasMetadata ? meta!.Category : d.Descriptor.Category,
                Severity: severity,
                Message: d.GetMessage(),
                Location: location,
                Rationale: hasMetadata ? meta!.Rationale : null,
                SuggestedFix: hasMetadata ? meta!.SuggestedFix : null));
        }

        return result;
    }

    private sealed record RuleMetadata(
        string Category,
        string Rationale,
        string SuggestedFix);
}
