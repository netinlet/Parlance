using System.Collections.Immutable;
using System.ComponentModel;
using ModelContextProtocol.Server;
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
        [Description("Absolute file path")]
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
        var refactorings = await codeActions.GetRefactoringsAsync(
            filePath, line, column, endLine, endColumn, ct);

        if (refactorings.IsEmpty)
        {
            var docId = session.CurrentSolution.GetDocumentIdsWithFilePath(filePath).FirstOrDefault();
            if (docId is null)
                return GetRefactoringsResult.NotFound(filePath);
            return GetRefactoringsResult.NoRefactorings(filePath);
        }

        return new GetRefactoringsResult(
            Status: "found",
            FilePath: filePath,
            Refactorings: refactorings,
            Message: null)
        { SnapshotVersion = session.SnapshotVersion };
    }
}

public sealed record GetRefactoringsResult(
    string Status, string? FilePath,
    ImmutableList<RefactoringEntry> Refactorings,
    string? Message)
{
    public long SnapshotVersion { get; init; }

    public static GetRefactoringsResult NotFound(string filePath) => new(
        "not_found", filePath, [],
        $"File '{filePath}' not found in the workspace");
    public static GetRefactoringsResult NoRefactorings(string filePath) => new(
        "no_refactorings", filePath, [],
        $"No refactorings available at the specified location in {filePath}");
    public static GetRefactoringsResult NotLoaded() => new(
        "not_loaded", null, [],
        "Workspace is still loading");
    public static GetRefactoringsResult LoadFailed(string message) => new(
        "load_failed", null, [], message);
    public static GetRefactoringsResult Error(string message) => new(
        "error", null, [], message);
}
