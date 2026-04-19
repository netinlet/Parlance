using System.Collections.Immutable;
using System.ComponentModel;
using ModelContextProtocol.Server;
using Parlance.Analysis;
using Parlance.CSharp.Workspace;

namespace Parlance.Mcp.Tools;

[McpServerToolType]
public sealed class AnalyzeProjectTool
{
    [McpServerTool(Name = "analyze-project", ReadOnly = true)]
    [Description("Run diagnostics on all C# files in a loaded project, or across the full loaded solution when projectName is omitted.")]
    public static async Task<AnalyzeToolResult> AnalyzeProject(
        WorkspaceSessionHolder holder,
        AnalysisService analysis,
        string? projectName = null,
        string? curationSet = null,
        int? maxDiagnostics = null,
        CancellationToken ct = default)
    {
        if (holder.LoadFailure is { } failure)
            return AnalyzeToolResult.LoadFailed(failure.Message);
        if (!holder.IsLoaded)
            return AnalyzeToolResult.NotLoaded();

        var session = holder.Session;
        var projects = session.CurrentSolution.Projects;
        if (!string.IsNullOrWhiteSpace(projectName))
        {
            projects = projects.Where(p => string.Equals(
                p.Name, projectName, StringComparison.OrdinalIgnoreCase));
        }

        var files = projects
            .SelectMany(p => p.Documents)
            .Select(d => d.FilePath)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => Path.GetFullPath(p!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToImmutableList();

        if (!string.IsNullOrWhiteSpace(projectName) && files.IsEmpty)
            return AnalyzeToolResult.Failed($"Project '{projectName}' was not found.");

        try
        {
            var options = new AnalyzeOptions(curationSet, maxDiagnostics);
            var result = await analysis.AnalyzeFilesAsync(files, options, ct);
            return ToToolResult(result);
        }
        catch (ArgumentException ex)
        {
            return AnalyzeToolResult.Failed(ex.Message);
        }
    }

    private static AnalyzeToolResult ToToolResult(FileAnalysisResult result) =>
        AnalyzeToolResult.Success(
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
