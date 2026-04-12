using System.Collections.Immutable;
using System.ComponentModel;
using ModelContextProtocol.Server;
using Parlance.Analysis;
using Parlance.CSharp.Workspace;

namespace Parlance.Mcp.Tools;

[McpServerToolType]
public sealed class PreviewCodeActionTool
{
    [McpServerTool(Name = "preview-code-action", ReadOnly = true)]
    [Description("Preview the changes a code fix or refactoring would make before applying. " +
                 "Pass an action ID from get-code-fixes or get-refactorings. " +
                 "Returns the exact text edits per file.")]
    public static async Task<PreviewCodeActionResult> PreviewCodeAction(
        WorkspaceSessionHolder holder,
        CodeActionService codeActions,
        [Description("Action ID from get-code-fixes or get-refactorings (e.g., 'fix-1', 'refactor-3')")]
        string actionId,
        CancellationToken ct = default)
    {
        if (holder.LoadFailure is { } failure)
            return PreviewCodeActionResult.LoadFailed(failure.Message);
        if (!holder.IsLoaded)
            return PreviewCodeActionResult.NotLoaded();

        var preview = await codeActions.PreviewAsync(actionId, ct);
        if (preview is null)
            return PreviewCodeActionResult.NotFound(actionId);

        if (preview.ErrorMessage is not null)
            return PreviewCodeActionResult.Error(actionId, preview.ErrorMessage);

        if (preview.IsExpired)
            return PreviewCodeActionResult.Expired(actionId);

        return new PreviewCodeActionResult(
            Status: "found",
            ActionId: preview.ActionId,
            Title: preview.Title,
            Changes: preview.Changes,
            Message: null);
    }
}

public sealed record PreviewCodeActionResult(
    string Status, string? ActionId, string? Title,
    ImmutableList<FileChange> Changes,
    string? Message)
{
    public static PreviewCodeActionResult NotFound(string actionId) => new(
        "not_found", actionId, null, [],
        $"Action '{actionId}' not found. It may have expired or the ID is invalid.");
    public static PreviewCodeActionResult Expired(string actionId) => new(
        "expired", actionId, null, [],
        $"Action '{actionId}' has expired because the workspace changed. Re-query fixes or refactorings.");
    public static PreviewCodeActionResult NotLoaded() => new(
        "not_loaded", null, null, [],
        "Workspace is still loading");
    public static PreviewCodeActionResult LoadFailed(string message) => new(
        "load_failed", null, null, [], message);
    public static PreviewCodeActionResult Error(string actionId, string message) => new(
        "error", actionId, null, [], message);
}
