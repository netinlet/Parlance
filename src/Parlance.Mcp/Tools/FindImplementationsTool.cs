using System.Collections.Immutable;
using System.ComponentModel;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;
using Parlance.Abstractions;
using Parlance.CSharp.Workspace;

namespace Parlance.Mcp.Tools;

[McpServerToolType]
public sealed class FindImplementationsTool
{
    [McpServerTool(Name = "find-implementations", ReadOnly = true)]
    [Description("Find all types that implement or inherit from a given interface or class.")]
    public static Task<FindImplementationsResult> FindImplementations(
        WorkspaceSessionHolder holder, WorkspaceQueryService query,
        string typeName, CancellationToken ct) =>
        holder.State.Match(
            notLoaded: () => Task.FromResult(FindImplementationsResult.NotLoaded()),
            loaded: session => RunAsync(query, session, typeName, ct),
            loadFailed: failure => Task.FromResult(FindImplementationsResult.LoadFailed(failure.Message)),
            disposed: () => Task.FromResult(FindImplementationsResult.NotLoaded()));

    private static async Task<FindImplementationsResult> RunAsync(
        WorkspaceQueryService query, CSharpWorkspaceSession session, string typeName, CancellationToken ct)
    {
        var snapshotVersion = session.SnapshotVersion;

        var symbols = await query.FindSymbolsAsync(typeName, SymbolFilter.Type, ct: ct);
        if (symbols.IsEmpty)
            return FindImplementationsResult.NotFound(typeName, snapshotVersion);

        if (symbols.Count > 1 && !typeName.Contains('.'))
            return FindImplementationsResult.Ambiguous(typeName, symbols.Select(s => s.ToCandidate()).ToImmutableList(), snapshotVersion);

        var targetSymbol = symbols[0].Symbol;
        var implementations = await query.FindImplementationsAsync(targetSymbol, ct);

        var entries = implementations
            .Select(s =>
            {
                return new ImplementationEntry(
                    s.Name,
                    s.ToDisplayString(),
                    s.Kind.ToString(),
                    s.Locations.FirstOrDefault()?.GetLineSpan().ToRepoPath(),
                    s.Locations.FirstOrDefault()?.GetLineSpan().StartLinePosition.Line + 1);
            })
            .ToImmutableList();

        return FindImplementationsResult.Found(targetSymbol.ToDisplayString(), entries, snapshotVersion);
    }
}

public sealed record FindImplementationsResult(
    string Status, string? TargetType, int Count,
    ImmutableList<ImplementationEntry> Implementations,
    ImmutableList<SymbolCandidate> Candidates,
    string? Message)
{
    public long SnapshotVersion { get; init; }

    public static FindImplementationsResult NotFound(string typeName, long snapshotVersion) => new(
        "not_found", typeName, 0, [], [], $"Type '{typeName}' not found in the workspace")
    { SnapshotVersion = snapshotVersion };
    public static FindImplementationsResult NotLoaded() => new(
        "not_loaded", null, 0, [], [], "Workspace is still loading");
    public static FindImplementationsResult LoadFailed(string message) => new(
        "load_failed", null, 0, [], [], message);
    public static FindImplementationsResult Ambiguous(string typeName, ImmutableList<SymbolCandidate> candidates, long snapshotVersion) => new(
        "ambiguous", typeName, 0, [], candidates,
        $"Multiple types match '{typeName}'. Use a fully qualified name to disambiguate.")
    { SnapshotVersion = snapshotVersion };
    public static FindImplementationsResult Found(
        string targetType, ImmutableList<ImplementationEntry> implementations, long snapshotVersion) => new(
        "found", targetType, implementations.Count, implementations, [], null)
        { SnapshotVersion = snapshotVersion };
}

public sealed record ImplementationEntry(
    string Name, string FullyQualifiedName, string Kind, RepoPath? FilePath, int? Line);
