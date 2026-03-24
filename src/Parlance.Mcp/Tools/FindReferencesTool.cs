using System.Collections.Immutable;
using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Parlance.CSharp.Workspace;

namespace Parlance.Mcp.Tools;

[McpServerToolType]
public sealed class FindReferencesTool
{
    [McpServerTool(Name = "find-references", ReadOnly = true)]
    [Description("Find all references to a symbol (type, method, property, field) across the solution.")]
    public static async Task<FindReferencesResult> FindReferences(
        WorkspaceSessionHolder holder, WorkspaceQueryService query,
        ILogger<FindReferencesTool> logger, string symbolName, CancellationToken ct)
    {
        using var _ = ToolDiagnostics.TimeToolCall(logger, "find-references");

        if (holder.LoadFailure is { } failure)
            return FindReferencesResult.LoadFailed(failure.Message);
        if (!holder.IsLoaded)
            return FindReferencesResult.NotLoaded();

        var symbols = await query.FindSymbolsAsync(symbolName, ct: ct);
        if (symbols.IsEmpty)
            return FindReferencesResult.NotFound(symbolName);

        var targetSymbol = symbols[0].Symbol;
        var referencedSymbols = await query.FindReferencesAsync(targetSymbol, ct);

        var locationsByFile = new Dictionary<string, List<ReferenceLocation>>();
        var totalCount = 0;

        foreach (var refSymbol in referencedSymbols)
        {
            foreach (var location in refSymbol.Locations)
            {
                if (!location.Location.IsInSource) continue;
                var span = location.Location.GetLineSpan();
                var filePath = span.Path;
                if (string.IsNullOrEmpty(filePath)) continue;

                totalCount++;

                string? snippet = null;
                var tree = location.Location.SourceTree;
                if (tree is not null)
                {
                    var text = await tree.GetTextAsync(ct);
                    var line = span.StartLinePosition.Line;
                    if (line >= 0 && line < text.Lines.Count)
                        snippet = text.Lines[line].ToString().Trim();
                }

                if (!locationsByFile.TryGetValue(filePath, out var list))
                {
                    list = [];
                    locationsByFile[filePath] = list;
                }
                list.Add(new ReferenceLocation(span.StartLinePosition.Line + 1, snippet));
            }
        }

        var fileGroups = locationsByFile
            .Select(kvp => new ReferenceFileGroup(kvp.Key, [.. kvp.Value]))
            .ToImmutableList();

        return new FindReferencesResult(
            Status: "found",
            SymbolName: targetSymbol.ToDisplayString(),
            TotalCount: totalCount,
            FileGroups: fileGroups,
            Message: null);
    }
}

public sealed record FindReferencesResult(
    string Status, string? SymbolName, int TotalCount,
    ImmutableList<ReferenceFileGroup> FileGroups, string? Message)
{
    public static FindReferencesResult NotFound(string symbolName) => new(
        "not_found", symbolName, 0, [], $"Symbol '{symbolName}' not found in the workspace");
    public static FindReferencesResult NotLoaded() => new(
        "not_loaded", null, 0, [], "Workspace is still loading");
    public static FindReferencesResult LoadFailed(string message) => new(
        "load_failed", null, 0, [], message);
}

public sealed record ReferenceFileGroup(string FilePath, ImmutableList<ReferenceLocation> Locations);
public sealed record ReferenceLocation(int Line, string? Snippet);
