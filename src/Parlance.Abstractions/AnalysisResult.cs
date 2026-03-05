namespace Parlance.Abstractions;

public sealed record AnalysisResult(
    IReadOnlyList<Diagnostic> Diagnostics,
    AnalysisSummary Summary);
