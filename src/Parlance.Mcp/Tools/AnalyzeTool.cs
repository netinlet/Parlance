using System.Collections.Immutable;
using System.ComponentModel;
using Microsoft.Extensions.Logging;
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
        ILogger<AnalyzeTool> logger,
        string[] files,
        string? curationSet = null,
        int? maxDiagnostics = null,
        CancellationToken ct = default)
    {
        using var _ = ToolDiagnostics.TimeToolCall(logger, "analyze");

        if (holder.LoadFailure is { } failure)
            return AnalyzeToolResult.LoadFailed(failure.Message);
        if (!holder.IsLoaded)
            return AnalyzeToolResult.NotLoaded();

        try
        {
            var options = new AnalyzeOptions(curationSet, maxDiagnostics);
            var result = await analysis.AnalyzeFilesAsync([.. files], options, ct);

            return new AnalyzeToolResult
            {
                Status = "success",
                CurationSet = result.CurationSet,
                Summary = new AnalyzeSummary(
                    result.Summary.TotalDiagnostics,
                    result.Summary.Errors,
                    result.Summary.Warnings,
                    result.Summary.Suggestions,
                    result.Summary.IdiomaticScore),
                Diagnostics = result.Diagnostics.Select(d => new AnalyzeDiagnostic(
                    d.RuleId, d.Severity, d.Message,
                    d.FilePath, d.Line,
                    d.FixClassification, d.Rationale)).ToImmutableList()
            };
        }
        catch (ArgumentException ex)
        {
            return new AnalyzeToolResult { Status = "error", Error = ex.Message };
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
}

public sealed record AnalyzeSummary(
    int Total, int Errors, int Warnings, int Suggestions, double Score);

public sealed record AnalyzeDiagnostic(
    string RuleId, string Severity, string Message,
    string File, int Line,
    string? FixClassification, string? Rationale);
