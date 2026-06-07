using System.Collections.Immutable;
using System.ComponentModel;
using ModelContextProtocol.Server;
using Parlance.Abstractions;
using Parlance.Analysis;
using Parlance.CSharp.Workspace;

namespace Parlance.Mcp.Tools;

[McpServerToolType]
public sealed class AnalyzeTool
{
    [McpServerTool(Name = "analyze", ReadOnly = true)]
    [Description("Run diagnostics on C# files. Returns analyzer findings with severity, " +
                 "fix classification, and rationale. Pass absolute file paths.")]
    public static Task<AnalyzeToolResult> Analyze(
        WorkspaceSessionHolder holder,
        AnalysisService analysis,
        string[] files,
        string? curationSet = null,
        int? maxDiagnostics = null,
        CancellationToken ct = default) =>
        holder.State.Match(
            notLoaded: () => Task.FromResult(AnalyzeToolResult.NotLoaded()),
            loaded: session => RunAsync(analysis, session, files, curationSet, maxDiagnostics, ct),
            loadFailed: failure => Task.FromResult(AnalyzeToolResult.LoadFailed(failure.Message)),
            disposed: () => Task.FromResult(AnalyzeToolResult.NotLoaded()));

    private static async Task<AnalyzeToolResult> RunAsync(
        AnalysisService analysis,
        CSharpWorkspaceSession session,
        string[] files,
        string? curationSet,
        int? maxDiagnostics,
        CancellationToken ct)
    {
        // Resolve workspace-root-relative paths; reject paths that escape the workspace root.
        // GetFullPath normalises .. segments for both relative and rooted inputs.
        // Trailing separator on the prefix prevents sibling-prefix bypass (e.g. workspace-tmp/).
        var workspaceRoot = Path.GetDirectoryName(session.WorkspacePath)!;
        var workspacePrefix = workspaceRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var resolvedFiles = ImmutableList.CreateBuilder<string>();
        foreach (var f in files)
        {
            var resolved = Path.IsPathRooted(f) ? Path.GetFullPath(f) : Path.GetFullPath(f, workspaceRoot);
            if (!resolved.StartsWith(workspacePrefix, StringComparison.OrdinalIgnoreCase))
                return AnalyzeToolResult.Failed($"Path '{f}' resolves outside the workspace root.");
            resolvedFiles.Add(resolved);
        }

        try
        {
            var options = new AnalyzeOptions(curationSet, maxDiagnostics);
            var result = await analysis.AnalyzeFilesAsync(resolvedFiles.ToImmutable(), options, ct);

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
                    d.FixClassification, d.Rationale)).ToImmutableList())
            with { SnapshotVersion = session.SnapshotVersion };
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
    public long SnapshotVersion { get; init; }

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
    RepoPath File, int Line,
    string? FixClassification, string? Rationale);
