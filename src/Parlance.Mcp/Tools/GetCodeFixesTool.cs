using System.Collections.Immutable;
using System.ComponentModel;
using ModelContextProtocol.Server;
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
    public static async Task<GetCodeFixesResult> GetCodeFixes(
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
            return GetCodeFixesResult.Error("line must be >= 1 (1-based).");

        if (holder.LoadFailure is { } failure)
            return GetCodeFixesResult.LoadFailed(failure.Message);
        if (!holder.IsLoaded)
            return GetCodeFixesResult.NotLoaded();

        var fixes = await codeActions.GetCodeFixesAsync(filePath, line, diagnosticId, ct);

        if (fixes.IsEmpty)
        {
            // Distinguish "file not found" from "no fixes"
            var docId = holder.Session.CurrentSolution.GetDocumentIdsWithFilePath(filePath).FirstOrDefault();
            if (docId is null)
                return GetCodeFixesResult.NotFound(filePath);
            return GetCodeFixesResult.NoFixes(filePath, line);
        }

        return new GetCodeFixesResult(
            Status: "found",
            FilePath: filePath,
            Line: line,
            Fixes: fixes,
            Message: null);
    }
}

public sealed record GetCodeFixesResult(
    string Status, string? FilePath, int? Line,
    ImmutableList<CodeFixEntry> Fixes,
    string? Message)
{
    public static GetCodeFixesResult NotFound(string filePath) => new(
        "not_found", filePath, null, [],
        $"File '{filePath}' not found in the workspace");
    public static GetCodeFixesResult NoFixes(string filePath, int line) => new(
        "no_fixes", filePath, line, [],
        $"No code fixes available at {filePath}:{line}");
    public static GetCodeFixesResult NotLoaded() => new(
        "not_loaded", null, null, [],
        "Workspace is still loading");
    public static GetCodeFixesResult LoadFailed(string message) => new(
        "load_failed", null, null, [], message);
    public static GetCodeFixesResult Error(string message) => new(
        "error", null, null, [], message);
}
