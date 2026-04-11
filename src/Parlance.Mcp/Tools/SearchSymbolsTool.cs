using System.Collections.Immutable;
using System.ComponentModel;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;
using Parlance.CSharp.Workspace;

namespace Parlance.Mcp.Tools;

[McpServerToolType]
public sealed class SearchSymbolsTool
{
    [McpServerTool(Name = "search-symbols", ReadOnly = true)]
    [Description("Fuzzy search for symbols by name across the workspace. " +
                 "Returns matching types, methods, properties, and other symbols. " +
                 "Use this to discover symbols when you don't know the exact name.")]
    public static async Task<SearchSymbolsResult> SearchSymbols(
        WorkspaceSessionHolder holder, WorkspaceQueryService query,
        ToolAnalytics analytics,
        [Description("Substring to search for (e.g., 'Handler', 'Parse')")]
        string searchQuery,
        [Description("Filter by symbol kind (single value): class, method, property, interface, enum, struct, field, event")]
        string? kind = null,
        [Description("Maximum number of results to return (default 25)")]
        int maxResults = 25,
        CancellationToken ct = default)
    {
        using var _ = analytics.TimeToolCall("search-symbols", new { searchQuery, kind, maxResults });

        if (holder.LoadFailure is { } failure)
            return SearchSymbolsResult.LoadFailed(failure.Message);
        if (!holder.IsLoaded)
            return SearchSymbolsResult.NotLoaded();

        if (string.IsNullOrWhiteSpace(searchQuery))
            return SearchSymbolsResult.Error("searchQuery must not be blank.");
        if (maxResults < 1)
            return SearchSymbolsResult.Error("maxResults must be >= 1.");
        maxResults = Math.Min(maxResults, 250);

        var parsed = kind is not null ? ParseKind(kind) : null;
        if (kind is not null && parsed is null)
            return SearchSymbolsResult.Error($"Unknown kind '{kind}'. Valid values: class, struct, interface, enum, method, property, field, event.");

        // Request more than maxResults so we can post-filter by specific kind
        var requestLimit = maxResults * 10;
        var (results, totalCount) = await query.SearchSymbolsAsync(searchQuery, parsed?.Filter, requestLimit, ct);

        results = parsed switch
        {
            // Post-filter by specific kind (e.g., "class" not just "Type")
            { TypeKind: { } typeKind } => [.. results.Where(r => r.Symbol is INamedTypeSymbol nts && nts.TypeKind == typeKind)],
            { MemberKind: { } memberKind } => [.. results.Where(r => r.Symbol.Kind == memberKind)],
            _ => results
        };

        if (results.IsEmpty)
            return SearchSymbolsResult.NoMatches(searchQuery);

        var totalMatches = parsed is not null ? results.Count : totalCount;
        var isTruncated = totalMatches > maxResults;

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

        return SearchSymbolsResult.Found(searchQuery, matches, totalMatches, isTruncated);
    }

    private static (SymbolFilter Filter, TypeKind? TypeKind, SymbolKind? MemberKind)? ParseKind(string kind) =>
        kind.ToLowerInvariant() switch
        {
            "class" => (SymbolFilter.Type, TypeKind.Class, null),
            "struct" => (SymbolFilter.Type, TypeKind.Struct, null),
            "interface" => (SymbolFilter.Type, TypeKind.Interface, null),
            "enum" => (SymbolFilter.Type, TypeKind.Enum, null),
            "method" => (SymbolFilter.Member, null, SymbolKind.Method),
            "property" => (SymbolFilter.Member, null, SymbolKind.Property),
            "field" => (SymbolFilter.Member, null, SymbolKind.Field),
            "event" => (SymbolFilter.Member, null, SymbolKind.Event),
            _ => null
        };
}

public sealed record SearchSymbolsResult(
    string Status, string? Query,
    ImmutableList<SymbolMatch> Matches,
    int TotalMatches,
    bool IsTruncated,
    string? Message)
{
    public static SearchSymbolsResult Found(string searchQuery, ImmutableList<SymbolMatch> matches,
        int totalMatches, bool isTruncated) => new(
        "found", searchQuery, matches, totalMatches, isTruncated, null);
    public static SearchSymbolsResult NoMatches(string searchQuery) => new(
        "no_matches", searchQuery, [], 0, false, $"No symbols matching '{searchQuery}' found in the workspace");
    public static SearchSymbolsResult NotLoaded() => new(
        "not_loaded", null, [], 0, false, "Workspace is still loading");
    public static SearchSymbolsResult LoadFailed(string message) => new(
        "load_failed", null, [], 0, false, message);
    public static SearchSymbolsResult Error(string message) => new(
        "error", null, [], 0, false, message);
}

public sealed record SymbolMatch(
    string DisplayName, string FullyQualifiedName, string Kind,
    string ProjectName, string? FilePath, int? Line);
