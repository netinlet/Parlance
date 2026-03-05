namespace Parlance.Abstractions;

public sealed record Diagnostic(
    string RuleId,
    string Category,
    DiagnosticSeverity Severity,
    string Message,
    Location Location,
    string? Rationale = null,
    string? SuggestedFix = null);
