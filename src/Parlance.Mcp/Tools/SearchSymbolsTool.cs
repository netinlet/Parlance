using System.Collections.Frozen;
using System.Collections.Immutable;
using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Parlance.CSharp.Workspace;

namespace Parlance.Mcp.Tools;

[McpServerToolType]
public sealed class SearchSymbolsTool
{
    private static readonly FrozenDictionary<string, SymbolFilter> KindMap =
        new Dictionary<string, SymbolFilter>(StringComparer.OrdinalIgnoreCase)
        {
            ["class"] = SymbolFilter.Type,
            ["struct"] = SymbolFilter.Type,
            ["interface"] = SymbolFilter.Type,
            ["enum"] = SymbolFilter.Type,
            ["method"] = SymbolFilter.Member,
            ["property"] = SymbolFilter.Member,
            ["field"] = SymbolFilter.Member,
            ["event"] = SymbolFilter.Member,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<string, TypeKind> TypeKindMap =
        new Dictionary<string, TypeKind>(StringComparer.OrdinalIgnoreCase)
        {
            ["class"] = TypeKind.Class,
            ["struct"] = TypeKind.Struct,
            ["interface"] = TypeKind.Interface,
            ["enum"] = TypeKind.Enum,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<string, SymbolKind> MemberKindMap =
        new Dictionary<string, SymbolKind>(StringComparer.OrdinalIgnoreCase)
        {
            ["method"] = SymbolKind.Method,
            ["property"] = SymbolKind.Property,
            ["field"] = SymbolKind.Field,
            ["event"] = SymbolKind.Event,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    [McpServerTool(Name = "search-symbols", ReadOnly = true)]
    [Description("Fuzzy search for symbols by name across the workspace. " +
                 "Returns matching types, methods, properties, and other symbols. " +
                 "Use this to discover symbols when you don't know the exact name.")]
    public static async Task<SearchSymbolsResult> SearchSymbols(
        WorkspaceSessionHolder holder, WorkspaceQueryService query,
        ILogger<SearchSymbolsTool> logger,
        [Description("Substring to search for (e.g., 'Handler', 'Parse')")]
        string searchQuery,
        [Description("Filter by symbol kind: class, method, property, interface, enum, struct, field, event")]
        string? kind = null,
        [Description("Maximum number of results to return (default 25)")]
        int maxResults = 25,
        CancellationToken ct = default)
    {
        using var _ = ToolDiagnostics.TimeToolCall(logger, "search-symbols");

        if (holder.LoadFailure is { } failure)
            return SearchSymbolsResult.LoadFailed(failure.Message);
        if (!holder.IsLoaded)
            return SearchSymbolsResult.NotLoaded();

        SymbolFilter? symbolFilter = null;
        if (kind is not null)
        {
            if (!KindMap.TryGetValue(kind, out var filter))
                return SearchSymbolsResult.Error($"Unknown kind '{kind}'. Valid values: class, struct, interface, enum, method, property, field, event.");
            symbolFilter = filter;
        }

        // Request more than maxResults so we can report accurate TotalMatches after post-filtering
        var (results, _) = await query.SearchSymbolsAsync(searchQuery, symbolFilter, maxResults * 10, ct);

        // Post-filter by specific kind (e.g., "class" not just "Type")
        if (kind is not null)
        {
            if (TypeKindMap.TryGetValue(kind, out var typeKind))
            {
                results = results
                    .Where(r => r.Symbol is INamedTypeSymbol nts && nts.TypeKind == typeKind)
                    .ToImmutableList();
            }
            else if (MemberKindMap.TryGetValue(kind, out var memberKind))
            {
                results = results
                    .Where(r => r.Symbol.Kind == memberKind)
                    .ToImmutableList();
            }
        }

        if (results.IsEmpty)
            return SearchSymbolsResult.NoMatches(searchQuery);

        var totalMatches = results.Count;

        var matches = results.Take(maxResults).Select(r =>
        {
            var loc = r.Symbol.Locations.FirstOrDefault();
            var span = loc?.GetLineSpan();
            return new SymbolMatch(
                r.Symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                r.Symbol.ToDisplayString(),
                r.Symbol.Kind.ToString(),
                r.Project.Name,
                span?.Path,
                span is null ? null : span.Value.StartLinePosition.Line + 1);
        }).ToImmutableList();

        return SearchSymbolsResult.Found(searchQuery, matches, totalMatches);
    }
}

public sealed record SearchSymbolsResult(
    string Status, string? Query,
    ImmutableList<SymbolMatch> Matches,
    int TotalMatches,
    string? Message)
{
    public static SearchSymbolsResult Found(string searchQuery, ImmutableList<SymbolMatch> matches, int totalMatches) => new(
        "found", searchQuery, matches, totalMatches, null);
    public static SearchSymbolsResult NoMatches(string searchQuery) => new(
        "no_matches", searchQuery, [], 0, $"No symbols matching '{searchQuery}' found in the workspace");
    public static SearchSymbolsResult NotLoaded() => new(
        "not_loaded", null, [], 0, "Workspace is still loading");
    public static SearchSymbolsResult LoadFailed(string message) => new(
        "load_failed", null, [], 0, message);
    public static SearchSymbolsResult Error(string message) => new(
        "error", null, [], 0, message);
}

public sealed record SymbolMatch(
    string DisplayName, string FullyQualifiedName, string Kind,
    string ProjectName, string? FilePath, int? Line);
