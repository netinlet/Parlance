using System.Collections.Immutable;
using System.ComponentModel;
using ModelContextProtocol.Server;
using Parlance.Analysis;
using Parlance.CSharp.Workspace;

namespace Parlance.Mcp.Tools;

[McpServerToolType]
public sealed class AnalyzeTool
{
    [McpServerTool(Name = "analyze", ReadOnly = true)]
    [Description("Run diagnostics on C# files. Returns analyzer findings with severity, " +
                 "fix classification, and rationale. Pass absolute file paths.")]
    public static async Task<AnalyzeToolResult> Analyze(
        WorkspaceSessionHolder holder,
        AnalysisService analysis,
        string[] files,
        string? curationSet = null,
        int? maxDiagnostics = null,
        CancellationToken ct = default)
    {
        if (holder.LoadFailure is { } failure)
            return AnalyzeToolResult.LoadFailed(failure.Message);
        if (!holder.IsLoaded)
            return AnalyzeToolResult.NotLoaded();

        try
        {
            var options = new AnalyzeOptions(curationSet, maxDiagnostics);
            var result = await analysis.AnalyzeFilesAsync([.. files], options, ct);

            return AnalyzeToolResult.Success(
                result.CurationSet,
                new AnalyzeSummary(
                    result.Summary.TotalDiagnostics,
                    result.Summary.Errors,
                    result.Summary.Warnings,
                    result.Summary.Suggestions,
                    result.Summary.IdiomaticScore),
                result.Diagnostics.Select(d => new AnalyzeDiagnostic(
                    d.RuleId, d.Severity, d.Message,
                    d.FilePath, d.Line,
                    d.FixClassification, d.Rationale)).ToImmutableList());
        }
        catch (ArgumentException ex)
        {
            return AnalyzeToolResult.Failed(ex.Message);
        }
    }
}

public sealed record AnalyzeToolResult
{
    public string Status { get; init; } = "error";
    public string? Error { get; init; }
    public string? CurationSet { get; init; }
    public AnalyzeSummary? Summary { get; init; }
    public ImmutableList<AnalyzeDiagnostic>? Diagnostics { get; init; }

    public static AnalyzeToolResult LoadFailed(string message) => new()
    {
        Status = "load_failed",
        Error = message
    };

    public static AnalyzeToolResult NotLoaded() => new()
    {
        Status = "not_loaded",
        Error = "Workspace is still loading"
    };

    public static AnalyzeToolResult Success(
        string? curationSet, AnalyzeSummary summary, ImmutableList<AnalyzeDiagnostic> diagnostics) => new()
        {
            Status = "success",
            CurationSet = curationSet,
            Summary = summary,
            Diagnostics = diagnostics
        };

    public static AnalyzeToolResult Failed(string message) => new()
    {
        Status = "error",
        Error = message
    };
}

public sealed record AnalyzeSummary(
    int Total, int Errors, int Warnings, int Suggestions, double Score);

public sealed record AnalyzeDiagnostic(
    string RuleId, string Severity, string Message,
    string File, int Line,
    string? FixClassification, string? Rationale);
