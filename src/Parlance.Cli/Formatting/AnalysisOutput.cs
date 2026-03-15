using System.Collections.Immutable;
using Parlance.Abstractions;

namespace Parlance.Cli.Formatting;

internal sealed record AnalysisOutput(
    ImmutableList<FileDiagnostic> Diagnostics,
    AnalysisSummary Summary,
    int FilesAnalyzed);

internal sealed record FileDiagnostic(
    string FilePath,
    Diagnostic Diagnostic);
