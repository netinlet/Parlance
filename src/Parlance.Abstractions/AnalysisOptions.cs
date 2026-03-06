namespace Parlance.Abstractions;

public sealed record AnalysisOptions(
    string[] SuppressRules,
    int? MaxDiagnostics = null,
    bool IncludeFixSuggestions = true,
    string? LanguageVersion = null)
{
    public AnalysisOptions() : this(SuppressRules: []) { }
}
