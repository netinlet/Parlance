using System.Collections.Immutable;
using System.ComponentModel;
using ModelContextProtocol.Server;
using Parlance.Analysis;
using Parlance.CSharp.Workspace;

namespace Parlance.Mcp.Tools;

[McpServerToolType]
public sealed class AnalyzeTool
{
    private const string Description =
        "Analyze C# files loaded in the current Roslyn workspace. " +
        "Pass absolute paths or relative paths to .cs files, .csproj files, or .sln files. " +
        "Relative paths are resolved from the workspace root. " +
        "Shell examples: analyze files from the current directory with `ls *.cs`; " +
        "analyze files changed on the current branch vs main with " +
        "`git diff --name-only main...HEAD -- '*.cs'`, then pass those paths as files. " +
        "This tool does not run git or discover changed files.";

    [McpServerTool(Name = "analyze", ReadOnly = true)]
    [Description(Description)]
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
            var workspaceRoot = Path.GetDirectoryName(holder.Session.WorkspacePath) ?? holder.Session.WorkspacePath;
            var resolvedFiles = AnalysisFileResolver.ResolveTargets(holder.Session, files, workspaceRoot);
            var options = new AnalyzeOptions(curationSet, maxDiagnostics);
            var result = await analysis.AnalyzeFilesAsync(resolvedFiles, options, ct);

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

public readonly record struct AnalyzeToolResult(
    string Status,
    string? Error,
    string? CurationSet,
    AnalyzeSummary? Summary,
    ImmutableList<AnalyzeDiagnostic>? Diagnostics)
{
    public static AnalyzeToolResult LoadFailed(string message) => new(
        "load_failed", message, null, null, null);

    public static AnalyzeToolResult NotLoaded() => new(
        "not_loaded", "Workspace is still loading", null, null, null);

    public static AnalyzeToolResult Success(
        string? curationSet, AnalyzeSummary summary, ImmutableList<AnalyzeDiagnostic> diagnostics) => new(
        "success", null, curationSet, summary, diagnostics);

    public static AnalyzeToolResult Failed(string message) => new(
        "error", message, null, null, null);
}

public readonly record struct AnalyzeSummary(
    int Total, int Errors, int Warnings, int Suggestions, double Score);

public readonly record struct AnalyzeDiagnostic(
    string RuleId, string Severity, string Message,
    string File, int Line,
    string? FixClassification, string? Rationale);
