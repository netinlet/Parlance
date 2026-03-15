using System.Collections.Immutable;

namespace Parlance.Abstractions;

public sealed record AnalysisResult(
    ImmutableList<Diagnostic> Diagnostics,
    AnalysisSummary Summary);
