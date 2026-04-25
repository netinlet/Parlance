using System.Collections.Immutable;
using Parlance.Abstractions;

namespace Parlance.Analysis;

public sealed record FileAnalysisResult(
    string CurationSet,
    AnalysisSummary Summary,
    ImmutableList<FileDiagnostic> Diagnostics);

public sealed record FileDiagnostic(
    string RuleId,
    string Category,
    string Severity,
    string Message,
    string FilePath,
    int Line,
    int EndLine,
    int Column,
    int EndColumn,
    string? FixClassification,
    string? Rationale);
