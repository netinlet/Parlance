namespace Parlance.ManifestGenerator;

internal sealed record RuleManifest(
    string TargetFramework,
    string GeneratedAt,
    List<RuleEntry> Rules);

internal sealed record RuleEntry(
    string Id,
    string Title,
    string Category,
    string DefaultSeverity,
    string Description,
    string? HelpUrl,
    string Source,
    bool HasRationale,
    bool HasSuggestedFix);
