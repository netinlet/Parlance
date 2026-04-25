namespace Parlance.Analysis.Curation;

public sealed record CuratedDiagnostic(
    string RuleId,
    string Category,
    string Severity,
    string Message,
    string FilePath,
    int Line,
    int Column,
    int EndLine,
    int EndColumn,
    string? FixClassification,
    string? Rationale);
