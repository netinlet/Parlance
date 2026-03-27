namespace Parlance.Analysis.Curation;

public sealed record CurationRule(
    string? RuleId,
    string? Category,
    string? Severity,
    string? FixClassification,
    string? RationaleId);
