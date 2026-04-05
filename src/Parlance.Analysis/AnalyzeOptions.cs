using System.Collections.Immutable;

namespace Parlance.Analysis;

public sealed record AnalyzeOptions(
    string? CurationSetName = null,
    int? MaxDiagnostics = null,
    ImmutableArray<string>? SuppressRuleIds = null);
