using System.Collections.Immutable;
using System.ComponentModel;
using Microsoft.Extensions.Logging;
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
    public static async Task<GetRefactoringsResult> GetRefactorings(
        WorkspaceSessionHolder holder,
        CodeActionService codeActions,
        ILogger<GetRefactoringsTool> logger,
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
        using var _ = ToolDiagnostics.TimeToolCall(logger, "get-refactorings");

        if (line < 1 || column < 1)
            return GetRefactoringsResult.Error("line and column must be >= 1 (1-based).");
        if (endLine is not null != endColumn is not null)
            return GetRefactoringsResult.Error("endLine and endColumn must both be provided for range selection.");
        if (endLine is not null && (endLine.Value < 1 || endColumn!.Value < 1))
            return GetRefactoringsResult.Error("endLine and endColumn must be >= 1 (1-based).");

        if (holder.LoadFailure is { } failure)
            return GetRefactoringsResult.LoadFailed(failure.Message);
        if (!holder.IsLoaded)
            return GetRefactoringsResult.NotLoaded();

        var refactorings = await codeActions.GetRefactoringsAsync(
            filePath, line, column, endLine, endColumn, ct);

        if (refactorings.IsEmpty)
        {
            var docId = holder.Session.CurrentSolution.GetDocumentIdsWithFilePath(filePath).FirstOrDefault();
            if (docId is null)
                return GetRefactoringsResult.NotFound(filePath);
            return GetRefactoringsResult.NoRefactorings(filePath);
        }

        return new GetRefactoringsResult(
            Status: "found",
            FilePath: filePath,
            Refactorings: refactorings,
            Message: null);
    }
}

public sealed record GetRefactoringsResult(
    string Status, string? FilePath,
    ImmutableList<RefactoringEntry> Refactorings,
    string? Message)
{
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
