namespace Parlance.Abstractions;

public sealed record AnalysisOptions(
    string[] SuppressRules,
    int? MaxDiagnostics = null,
    bool IncludeFixSuggestions = true)
{
    public AnalysisOptions() : this(SuppressRules: []) { }
}
