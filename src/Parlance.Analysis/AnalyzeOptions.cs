namespace Parlance.Analysis;

public sealed record AnalyzeOptions(
    string? CurationSetName = null,
    int? MaxDiagnostics = null,
    RuleSuppression? Suppress = null);
