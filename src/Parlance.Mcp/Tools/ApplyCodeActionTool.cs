using System.Collections.Immutable;
using System.ComponentModel;
using ModelContextProtocol.Server;
using Parlance.Abstractions;
using Parlance.Analysis;
using Parlance.CSharp.Workspace;

namespace Parlance.Mcp.Tools;

[McpServerToolType]
public sealed class ApplyCodeActionTool
{
    [McpServerTool(Name = "apply-code-action", ReadOnly = true)]
    [Description("Compute the complete, machine-applyable edit (an LSP WorkspaceEdit) for a code fix or " +
                 "refactoring WITHOUT writing to disk. Pass an action ID from get-code-fixes or " +
                 "get-refactorings. Returns ordered per-file text edits plus any create/delete/rename file " +
                 "operations, stamped with the snapshot it was computed against. The agent applies and saves " +
                 "the edit with its own tools — Parlance never persists it. Pass expectedSnapshotVersion to " +
                 "get status 'stale' instead of an outdated edit when the workspace has moved.")]
    public static Task<ApplyCodeActionResult> ApplyCodeAction(
        WorkspaceSessionHolder holder,
        CodeActionService codeActions,
        [Description("Action ID from get-code-fixes or get-refactorings (e.g., 'fix-1', 'refactor-3')")]
        string actionId,
        [Description("If set, returns status 'stale' when the workspace has moved past this snapshot. 0 or omitted = no check.")]
        long? expectedSnapshotVersion = null,
        CancellationToken ct = default) =>
        holder.State.Match(
            notLoaded: () => Task.FromResult(ApplyCodeActionResult.NotLoaded()),
            loaded: session =>
            {
                if (holder.IsStale(expectedSnapshotVersion))
                {
                    var actual = holder.CurrentSnapshotVersion();
                    return Task.FromResult(ApplyCodeActionResult.Stale(actual,
                        StalenessMessage.ExpectedMismatch(expectedSnapshotVersion!.Value, actual)));
                }
                return RunAsync(codeActions, session, actionId, ct);
            },
            loadFailed: failure => Task.FromResult(ApplyCodeActionResult.LoadFailed(failure.Message)),
            disposed: () => Task.FromResult(ApplyCodeActionResult.NotLoaded()));

    private static async Task<ApplyCodeActionResult> RunAsync(
        CodeActionService codeActions, CSharpWorkspaceSession session, string actionId, CancellationToken ct)
    {
        var edit = await codeActions.ApplyAsync(actionId, ct);
        if (edit is null)
            return ApplyCodeActionResult.NotFound(actionId);
        if (edit.IsExpired)
            return ApplyCodeActionResult.Stale(session.SnapshotVersion,
                StalenessMessage.ActionSuperseded(actionId));
        if (edit.ErrorMessage is not null)
            return ApplyCodeActionResult.NotApplicable(actionId, edit.ErrorMessage);

        // Map the analysis-layer edit (absolute-string paths) onto the MCP DTO whose paths are
        // workspace-relative RepoPaths — the same path contract every other tool result follows.
        var documentEdits = edit.DocumentEdits
            .Select(d => new WorkspaceDocumentEdit(
                d.FilePath.ToRepoPath(), d.DocumentVersion, d.Newline, d.Encoding, d.HasBom, d.Edits))
            .ToImmutableList();

        var resourceOperations = edit.ResourceOperations
            .Select(MapResourceOperation)
            .ToImmutableList();

        // Stamp the version the edit was actually computed against (captured at resolution), not a
        // post-await re-read of the live session, which could have advanced under a concurrent refresh.
        return ApplyCodeActionResult.Success(
            edit.ActionId, edit.Title, documentEdits, resourceOperations, edit.SnapshotVersion);
    }

    private static WorkspaceResourceOperation MapResourceOperation(ResourceOperation op) => op switch
    {
        ResourceOperation.CreateFile c =>
            WorkspaceResourceOperation.Create(c.FilePath, c.Content, c.Newline, c.Encoding, c.HasBom),
        ResourceOperation.DeleteFile d => WorkspaceResourceOperation.Delete(d.FilePath),
        ResourceOperation.RenameFile r => WorkspaceResourceOperation.Rename(r.OldFilePath, r.NewFilePath),
        _ => throw new InvalidOperationException($"Unknown resource operation: {op.GetType().Name}"),
    };
}

/// <summary>Ordered text edits against one existing file. <see cref="FilePath"/> is workspace-relative.</summary>
public sealed record WorkspaceDocumentEdit(
    RepoPath? FilePath, long? DocumentVersion, string Newline, string Encoding, bool HasBom,
    ImmutableList<TextEdit> Edits);

/// <summary>An LSP-style file resource operation: <c>kind</c> is "create", "delete", or "rename".</summary>
public sealed record WorkspaceResourceOperation(
    string Kind,
    RepoPath? FilePath,
    string? Content,
    string? Newline,
    string? Encoding,
    bool? HasBom,
    RepoPath? OldFilePath,
    RepoPath? NewFilePath)
{
    public static WorkspaceResourceOperation Create(
        string filePath, string content, string newline, string encoding, bool hasBom) =>
        new("create", filePath.ToRepoPath(), content, newline, encoding, hasBom, null, null);

    public static WorkspaceResourceOperation Delete(string filePath) =>
        new("delete", filePath.ToRepoPath(), null, null, null, null, null, null);

    public static WorkspaceResourceOperation Rename(string oldFilePath, string newFilePath) =>
        new("rename", null, null, null, null, null, oldFilePath.ToRepoPath(), newFilePath.ToRepoPath());
}

public sealed record ApplyCodeActionResult
{
    public string Status { get; init; } = "error";
    public string? ActionId { get; init; }
    public string? Title { get; init; }
    public ImmutableList<WorkspaceDocumentEdit> DocumentEdits { get; init; } = [];
    public ImmutableList<WorkspaceResourceOperation> ResourceOperations { get; init; } = [];
    public long SnapshotVersion { get; init; }
    public string? Message { get; init; }

    public static ApplyCodeActionResult Success(
        string actionId, string title,
        ImmutableList<WorkspaceDocumentEdit> documentEdits,
        ImmutableList<WorkspaceResourceOperation> resourceOperations,
        long snapshotVersion) => new()
        {
            Status = "success",
            ActionId = actionId,
            Title = title,
            DocumentEdits = documentEdits,
            ResourceOperations = resourceOperations,
            SnapshotVersion = snapshotVersion,
        };

    public static ApplyCodeActionResult Stale(long actual, string message) => new()
    {
        Status = "stale",
        SnapshotVersion = actual,
        Message = message,
    };

    public static ApplyCodeActionResult NotFound(string actionId) => new()
    {
        Status = "not_found",
        ActionId = actionId,
        Message = $"Action '{actionId}' not found. It may have expired or the ID is invalid.",
    };

    public static ApplyCodeActionResult NotApplicable(string actionId, string message) => new()
    {
        Status = "not_applicable",
        ActionId = actionId,
        Message = message,
    };

    public static ApplyCodeActionResult NotLoaded() => new()
    {
        Status = "not_loaded",
        Message = "Workspace is still loading",
    };

    public static ApplyCodeActionResult LoadFailed(string message) => new()
    {
        Status = "load_failed",
        Message = message,
    };
}
