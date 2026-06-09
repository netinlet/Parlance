using System.Collections.Immutable;
using System.ComponentModel;
using ModelContextProtocol.Server;
using Parlance.Abstractions;
using Parlance.Analysis;
using Parlance.CSharp.Workspace;

namespace Parlance.Mcp.Tools;

[McpServerToolType]
public sealed class GetCodeFixesTool
{
    [McpServerTool(Name = "get-code-fixes", ReadOnly = true)]
    [Description("Get available code fixes for diagnostics at a specific line in a file. " +
                 "Returns fix IDs that can be passed to preview-code-action to see the changes. " +
                 "Use after 'analyze' to see what automated fixes are available.")]
    public static Task<GetCodeFixesResult> GetCodeFixes(
        WorkspaceSessionHolder holder,
        CodeActionService codeActions,
        [Description("Absolute file path")]
        string filePath,
        [Description("1-based line number")]
        int line,
        [Description("Filter to a specific diagnostic ID (e.g., 'CS8600', 'PARL0004')")]
        string? diagnosticId = null,
        CancellationToken ct = default)
    {
        if (line < 1)
            return Task.FromResult(GetCodeFixesResult.Error("line must be >= 1 (1-based)."));

        return holder.State.Match(
            notLoaded: () => Task.FromResult(GetCodeFixesResult.NotLoaded()),
            loaded: session => RunAsync(codeActions, session, filePath, line, diagnosticId, ct),
            loadFailed: failure => Task.FromResult(GetCodeFixesResult.LoadFailed(failure.Message)),
            disposed: () => Task.FromResult(GetCodeFixesResult.NotLoaded()));
    }

    private static async Task<GetCodeFixesResult> RunAsync(
        CodeActionService codeActions, CSharpWorkspaceSession session,
        string filePath, int line, string? diagnosticId, CancellationToken ct)
    {
        // Resolve a workspace-relative input (echoed RepoPath) to absolute, and echo the same form.
        var resolved = session.NormalizeInputPath(filePath);
        var fixes = await codeActions.GetCodeFixesAsync(resolved, line, diagnosticId, ct);

        if (fixes.IsEmpty)
        {
            // Distinguish "file not found" from "no fixes". The workspace is loaded here, so stamp the live
            // snapshot: a negative result still carries the staleness signal the contract promises.
            var docId = session.CurrentSolution.GetDocumentIdsWithFilePath(resolved).FirstOrDefault();
            return docId is null
                ? GetCodeFixesResult.NotFound(resolved) with { SnapshotVersion = session.SnapshotVersion }
                : GetCodeFixesResult.NoFixes(resolved, line) with { SnapshotVersion = session.SnapshotVersion };
        }

        return new GetCodeFixesResult(
            Status: "found",
            FilePath: resolved.ToRepoPath(),
            Line: line,
            Fixes: fixes,
            Message: null)
        { SnapshotVersion = session.SnapshotVersion };
    }
}

public sealed record GetCodeFixesResult(
    string Status, RepoPath? FilePath, int? Line,
    ImmutableList<CodeFixEntry> Fixes,
    string? Message)
{
    public long SnapshotVersion { get; init; }

    public static GetCodeFixesResult NotFound(string filePath) => new(
        "not_found", filePath.ToRepoPath(), null, [],
        $"File '{filePath}' not found in the workspace");
    public static GetCodeFixesResult NoFixes(string filePath, int line) => new(
        "no_fixes", filePath.ToRepoPath(), line, [],
        $"No code fixes available at {filePath}:{line}");
    public static GetCodeFixesResult NotLoaded() => new(
        "not_loaded", null, null, [],
        "Workspace is still loading");
    public static GetCodeFixesResult LoadFailed(string message) => new(
        "load_failed", null, null, [], message);
    public static GetCodeFixesResult Error(string message) => new(
        "error", null, null, [], message);
}
