using Parlance.Abstractions;

namespace Parlance.Cli.Formatting;

internal sealed record AnalysisOutput(
    IReadOnlyList<FileDiagnostic> Diagnostics,
    AnalysisSummary Summary,
    int FilesAnalyzed);

internal sealed record FileDiagnostic(
    string FilePath,
    Diagnostic Diagnostic);
