using System.Collections.Immutable;
using System.ComponentModel;
using ModelContextProtocol.Server;
using Parlance.Abstractions;
using Parlance.Analysis;
using Parlance.CSharp.Workspace;

namespace Parlance.Mcp.Tools;

[McpServerToolType]
public sealed class GetRefactoringsTool
{
    [McpServerTool(Name = "get-refactorings", ReadOnly = true)]
    [Description("Get available refactoring actions at a code location or range. " +
                 "Returns refactoring IDs that can be passed to preview-code-action. " +
                 "Use when doing structural work like extracting methods or introducing variables.")]
    public static Task<GetRefactoringsResult> GetRefactorings(
        WorkspaceSessionHolder holder,
        CodeActionService codeActions,
        [Description("Absolute or workspace-relative file path")]
        string filePath,
        [Description("1-based line number")]
        int line,
        [Description("1-based column number")]
        int column,
        [Description("1-based end line for range selection (optional)")]
        int? endLine = null,
        [Description("1-based end column for range selection (optional)")]
        int? endColumn = null,
        CancellationToken ct = default)
    {
        if (line < 1 || column < 1)
            return Task.FromResult(GetRefactoringsResult.Error("line and column must be >= 1 (1-based)."));
        if (endLine is not null != endColumn is not null)
            return Task.FromResult(GetRefactoringsResult.Error("endLine and endColumn must both be provided for range selection."));
        if (endLine is not null && (endLine.Value < 1 || endColumn!.Value < 1))
            return Task.FromResult(GetRefactoringsResult.Error("endLine and endColumn must be >= 1 (1-based)."));

        return holder.State.Match(
            notLoaded: () => Task.FromResult(GetRefactoringsResult.NotLoaded()),
            loaded: session => RunAsync(codeActions, session, filePath, line, column, endLine, endColumn, ct),
            loadFailed: failure => Task.FromResult(GetRefactoringsResult.LoadFailed(failure.Message)),
            disposed: () => Task.FromResult(GetRefactoringsResult.NotLoaded()));
    }

    private static async Task<GetRefactoringsResult> RunAsync(
        CodeActionService codeActions, CSharpWorkspaceSession session,
        string filePath, int line, int column, int? endLine, int? endColumn, CancellationToken ct)
    {
        // Capture the version the operation begins against (see FindReferencesTool for the rationale).
        var snapshotVersion = session.SnapshotVersion;

        // Resolve a workspace-relative input (echoed RepoPath) to absolute, and echo the same form.
        var resolved = session.NormalizeInputPath(filePath);
        var refactorings = await codeActions.GetRefactoringsAsync(
            resolved, line, column, endLine, endColumn, ct);

        if (refactorings.IsEmpty)
        {
            var docId = session.CurrentSolution.GetDocumentIdsWithFilePath(resolved).FirstOrDefault();
            if (docId is null)
                return GetRefactoringsResult.NotFound(resolved, snapshotVersion);
            return GetRefactoringsResult.NoRefactorings(resolved, snapshotVersion);
        }

        return new GetRefactoringsResult(
            Status: "found",
            FilePath: resolved.ToRepoPath(),
            Refactorings: refactorings,
            Message: null)
        { SnapshotVersion = snapshotVersion };
    }
}

public sealed record GetRefactoringsResult(
    string Status, RepoPath? FilePath,
    ImmutableList<RefactoringEntry> Refactorings,
    string? Message)
{
    public long SnapshotVersion { get; init; }

    public static GetRefactoringsResult NotFound(string filePath, long snapshotVersion) => new(
        "not_found", filePath.ToRepoPath(), [],
        $"File '{filePath}' not found in the workspace")
    { SnapshotVersion = snapshotVersion };
    public static GetRefactoringsResult NoRefactorings(string filePath, long snapshotVersion) => new(
        "no_refactorings", filePath.ToRepoPath(), [],
        $"No refactorings available at the specified location in {filePath}")
    { SnapshotVersion = snapshotVersion };
    public static GetRefactoringsResult NotLoaded() => new(
        "not_loaded", null, [],
        "Workspace is still loading");
    public static GetRefactoringsResult LoadFailed(string message) => new(
        "load_failed", null, [], message);
    public static GetRefactoringsResult Error(string message) => new(
        "error", null, [], message);
}
