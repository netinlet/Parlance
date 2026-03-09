using System.Collections.Immutable;

namespace Parlance.Abstractions;

public sealed record AnalysisOptions(
    ImmutableArray<string> SuppressRules,
    int? MaxDiagnostics = null,
    bool IncludeFixSuggestions = true,
    string? LanguageVersion = null)
{
    public AnalysisOptions() : this(SuppressRules: []) { }
}
