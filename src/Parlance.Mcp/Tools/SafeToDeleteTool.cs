using System.Collections.Immutable;
using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Parlance.CSharp.Workspace;

namespace Parlance.Mcp.Tools;

[McpServerToolType]
public sealed class SafeToDeleteTool
{
    [McpServerTool(Name = "safe-to-delete", ReadOnly = true)]
    [Description("Checks whether a symbol (type, method, property, field) is safe to delete by counting references to it. " +
                 "Returns safe=true only if there are zero references.")]
    public static async Task<SafeToDeleteResult> CheckSafeToDelete(
        WorkspaceSessionHolder holder, WorkspaceQueryService query,
        ILogger<SafeToDeleteTool> logger, string symbolName, CancellationToken ct)
    {
        using var _ = ToolDiagnostics.TimeToolCall(logger, "safe-to-delete");

        if (holder.LoadFailure is { } failure)
            return SafeToDeleteResult.LoadFailed(failure.Message);
        if (!holder.IsLoaded)
            return SafeToDeleteResult.NotLoaded();

        var symbols = await query.FindSymbolsAsync(symbolName, ct: ct);
        if (symbols.IsEmpty)
            return SafeToDeleteResult.NotFound(symbolName);

        var symbol = symbols[0].Symbol;
        var references = await query.FindReferencesAsync(symbol, ct);

        var locations = new List<DeleteReferenceLocation>();
        int totalCount = 0;

        foreach (var refSymbol in references)
        {
            foreach (var location in refSymbol.Locations)
            {
                if (!location.Location.IsInSource) continue;
                totalCount++;

                if (locations.Count < 5)
                {
                    var span = location.Location.GetLineSpan();
                    locations.Add(new DeleteReferenceLocation(span.Path, span.StartLinePosition.Line + 1));
                }
            }
        }

        return new SafeToDeleteResult(
            Status: "found",
            SymbolName: symbol.ToDisplayString(),
            Safe: totalCount == 0,
            ReferenceCount: totalCount,
            SampleLocations: [.. locations],
            Message: null);
    }
}

public sealed record SafeToDeleteResult(
    string Status, string? SymbolName, bool Safe, int ReferenceCount,
    ImmutableList<DeleteReferenceLocation> SampleLocations, string? Message)
{
    public static SafeToDeleteResult NotFound(string symbolName) => new(
        "not_found", symbolName, false, 0, [], $"Symbol '{symbolName}' not found");
    public static SafeToDeleteResult NotLoaded() => new(
        "not_loaded", null, false, 0, [], "Workspace is still loading");
    public static SafeToDeleteResult LoadFailed(string message) => new(
        "load_failed", null, false, 0, [], message);
}

public sealed record DeleteReferenceLocation(string? FilePath, int Line);
