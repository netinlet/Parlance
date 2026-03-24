using System.Collections.Immutable;
using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Parlance.CSharp.Workspace;

namespace Parlance.Mcp.Tools;

[McpServerToolType]
public sealed class FindImplementationsTool
{
    [McpServerTool(Name = "find-implementations", ReadOnly = true)]
    [Description("Find all types that implement or inherit from a given interface or class.")]
    public static async Task<FindImplementationsResult> FindImplementations(
        WorkspaceSessionHolder holder, WorkspaceQueryService query,
        ILogger<FindImplementationsTool> logger, string typeName, CancellationToken ct)
    {
        using var _ = ToolDiagnostics.TimeToolCall(logger, "find-implementations");

        if (holder.LoadFailure is { } failure)
            return FindImplementationsResult.LoadFailed(failure.Message);
        if (!holder.IsLoaded)
            return FindImplementationsResult.NotLoaded();

        var symbols = await query.FindSymbolsAsync(typeName, SymbolFilter.Type, ct: ct);
        if (symbols.IsEmpty)
            return FindImplementationsResult.NotFound(typeName);

        var targetSymbol = symbols[0].Symbol;
        var implementations = await query.FindImplementationsAsync(targetSymbol, ct);

        var entries = implementations
            .Select(s => new ImplementationEntry(
                s.Name,
                s.ToDisplayString(),
                s.Kind.ToString(),
                s.Locations.FirstOrDefault()?.GetLineSpan().Path,
                s.Locations.FirstOrDefault()?.GetLineSpan().StartLinePosition.Line))
            .ToImmutableList();

        return new FindImplementationsResult(
            Status: "found",
            TargetType: targetSymbol.ToDisplayString(),
            Count: entries.Count,
            Implementations: entries,
            Message: null);
    }
}

public sealed record FindImplementationsResult(
    string Status, string? TargetType, int Count,
    ImmutableList<ImplementationEntry> Implementations, string? Message)
{
    public static FindImplementationsResult NotFound(string typeName) => new(
        "not_found", typeName, 0, [], $"Type '{typeName}' not found in the workspace");
    public static FindImplementationsResult NotLoaded() => new(
        "not_loaded", null, 0, [], "Workspace is still loading");
    public static FindImplementationsResult LoadFailed(string message) => new(
        "load_failed", null, 0, [], message);
}

public sealed record ImplementationEntry(
    string Name, string FullyQualifiedName, string Kind, string? FilePath, int? Line);
