using System.Collections.Immutable;

namespace Parlance.Abstractions;

public sealed record AnalysisSummary(
    int TotalDiagnostics,
    int Errors,
    int Warnings,
    int Suggestions,
    ImmutableDictionary<string, int> ByCategory,
    double IdiomaticScore);
