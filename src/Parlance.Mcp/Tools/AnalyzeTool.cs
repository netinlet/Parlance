using System.Collections.Immutable;
using System.ComponentModel;
using ModelContextProtocol.Server;
using Parlance.Abstractions;
using Parlance.Analysis;
using Parlance.CSharp.Workspace;
using Parlance.Mcp.Serialization;

namespace Parlance.Mcp.Tools;

[McpServerToolType]
public sealed class AnalyzeTool
{
    [McpServerTool(Name = "analyze", ReadOnly = true)]
    [Description("Run diagnostics on C# files. Returns analyzer findings with severity, " +
                 "fix classification, and rationale. Accepts absolute or workspace-relative file paths.")]
    public static Task<AnalyzeToolResult> Analyze(
        WorkspaceSessionHolder holder,
        AnalysisService analysis,
        string[] files,
        string? curationSet = null,
        int? maxDiagnostics = null,
        [Description("If set, returns status 'stale' when the workspace has moved past this snapshot. 0 or omitted = no check.")]
        long? expectedSnapshotVersion = null,
        CancellationToken ct = default)
    {
        return holder.State.Match(
            notLoaded: () => Task.FromResult(AnalyzeToolResult.NotLoaded()),
            loaded: session =>
            {
                // Capture the snapshot once: the staleness verdict and the stamp must come from the
                // same read, or a file-watcher tick between two reads could stamp a "not stale"
                // result with a newer version than the verdict was computed against. This tool's
                // stamp is the one round-tripped via expectedSnapshotVersion, so the agreement matters
                // most here.
                var snapshotVersion = session.SnapshotVersion;
                if (expectedSnapshotVersion is { } expected && expected != 0 && expected != snapshotVersion)
                    return Task.FromResult(AnalyzeToolResult.Stale(snapshotVersion, expected));
                return RunAsync(analysis, session, snapshotVersion, files, curationSet, maxDiagnostics, ct);
            },
            loadFailed: failure => Task.FromResult(AnalyzeToolResult.LoadFailed(failure.Message)),
            disposed: () => Task.FromResult(AnalyzeToolResult.NotLoaded()));
    }

    private static async Task<AnalyzeToolResult> RunAsync(
        AnalysisService analysis,
        CSharpWorkspaceSession session,
        long snapshotVersion,
        string[] files,
        string? curationSet,
        int? maxDiagnostics,
        CancellationToken ct)
    {
        // Resolve through the same single boundary every other file-input tool uses (rooted inputs
        // normalised in place, relative inputs resolved against the root), then reject paths that
        // escape the workspace root. The prefix carries a trailing separator to block sibling-prefix
        // bypass (e.g. workspace-tmp/); comparison is case-sensitive except on Windows, matching the
        // host filesystem so a case-variant sibling dir on Linux isn't treated as in-tree.
        var workspacePrefix = session.Root.Absolute.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var resolvedFiles = ImmutableList.CreateBuilder<string>();
        foreach (var f in files)
        {
            var resolved = session.NormalizeInputPath(f);
            if (!resolved.StartsWith(workspacePrefix, pathComparison))
                return AnalyzeToolResult.Failed($"Path '{f}' resolves outside the workspace root.", snapshotVersion);
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
                    d.FilePath.ToRepoPath(), d.Line,
                    d.FixClassification, d.Rationale)).ToImmutableList(),
                snapshotVersion);
        }
        catch (ArgumentException ex)
        {
            return AnalyzeToolResult.Failed(ex.Message, snapshotVersion);
        }
    }
}

public sealed record AnalyzeToolResult
{
    public string Status { get; init; } = "error";
    public string? Error { get; init; }
    public string? CurationSet { get; init; }
    public AnalyzeSummary? Summary { get; init; }

    // Keep the empty array on a clean analyze: "analyzed, zero diagnostics" is a real signal a
    // client must be able to tell apart from a never-populated/absent field. WhenWritingNull still
    // drops it on the not-loaded/error paths where it is genuinely null.
    [KeepWhenEmpty]
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
        string? curationSet, AnalyzeSummary summary, ImmutableList<AnalyzeDiagnostic> diagnostics,
        long snapshotVersion) => new()
        {
            Status = "success",
            CurationSet = curationSet,
            Summary = summary,
            Diagnostics = diagnostics,
            SnapshotVersion = snapshotVersion
        };

    public static AnalyzeToolResult Failed(string message, long snapshotVersion) => new()
    {
        Status = "error",
        Error = message,
        SnapshotVersion = snapshotVersion
    };

    public static AnalyzeToolResult Stale(long actual, long expected) => new()
    {
        Status = "stale",
        Error = $"Workspace moved past the expected snapshot (expected {expected}, now {actual}). Re-query.",
        SnapshotVersion = actual,
    };
}

public sealed record AnalyzeSummary(
    int Total, int Errors, int Warnings, int Suggestions, double Score);

public sealed record AnalyzeDiagnostic(
    string RuleId, string Severity, string Message,
    RepoPath? File, int Line,
    string? FixClassification, string? Rationale);
