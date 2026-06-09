using System.Collections.Immutable;
using System.ComponentModel;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;
using Parlance.Abstractions;
using Parlance.CSharp.Workspace;

namespace Parlance.Mcp.Tools;

[McpServerToolType]
public sealed class GotoDefinitionTool
{
    [McpServerTool(Name = "goto-definition", ReadOnly = true)]
    [Description("Go to the definition of a symbol. Provide either a symbolName for name-based lookup, " +
                 "or filePath + line + column (1-based) for position-based lookup. " +
                 "If both are provided, position takes precedence.")]
    public static Task<GotoDefinitionResult> GotoDefinition(
        WorkspaceSessionHolder holder, WorkspaceQueryService query,
        [Description("Symbol name to look up (e.g., 'MyClass' or 'Namespace.MyClass')")]
        string? symbolName = null,
        [Description("File path for position-based lookup")]
        string? filePath = null,
        [Description("1-based line number (required with filePath)")]
        int? line = null,
        [Description("1-based column number (required with filePath)")]
        int? column = null,
        CancellationToken ct = default) =>
        holder.State.Match(
            notLoaded: () => Task.FromResult(GotoDefinitionResult.NotLoaded()),
            loaded: session => RunAsync(query, session, symbolName, filePath, line, column, ct),
            loadFailed: failure => Task.FromResult(GotoDefinitionResult.LoadFailed(failure.Message)),
            disposed: () => Task.FromResult(GotoDefinitionResult.NotLoaded()));

    private static async Task<GotoDefinitionResult> RunAsync(
        WorkspaceQueryService query, CSharpWorkspaceSession session, string? symbolName, string? filePath,
        int? line, int? column, CancellationToken ct)
    {
        // Capture the version the operation begins against (see FindReferencesTool for the rationale).
        var snapshotVersion = session.SnapshotVersion;

        var hasPosition = filePath is not null && line is not null && column is not null;
        var hasPartialPosition = filePath is not null && (line is null || column is null);
        var hasName = symbolName is not null;

        if (hasPartialPosition)
            return GotoDefinitionResult.Error("Position-based lookup requires filePath, line, and column.", snapshotVersion);

        if (!hasPosition && !hasName)
            return GotoDefinitionResult.Error("Provide either symbolName or filePath + line + column.", snapshotVersion);

        ISymbol? targetSymbol;

        if (hasPosition)
        {
            if (line!.Value < 1 || column!.Value < 1)
                return GotoDefinitionResult.Error("line and column must be >= 1 (1-based).", snapshotVersion);

            var zeroLine = line.Value - 1;
            var zeroCol = column.Value - 1;
            targetSymbol = await query.GetSymbolAtPositionAsync(filePath!, zeroLine, zeroCol, ct);

            if (targetSymbol is null)
                return GotoDefinitionResult.NotFound(filePath!, snapshotVersion);
        }
        else
        {
            var symbols = await query.FindSymbolsAsync(symbolName!, ct: ct);
            if (symbols.IsEmpty)
                return GotoDefinitionResult.NotFound(symbolName!, snapshotVersion);

            if (symbols.Count > 1 && !symbolName!.Contains('.'))
            {
                return GotoDefinitionResult.Ambiguous(symbolName,
                    [.. symbols.Select(s => s.ToCandidate())], snapshotVersion);
            }

            targetSymbol = symbols[0].Symbol;
        }

        // Navigate to the original/unbound definition (e.g., List<T> instead of List<string>)
        targetSymbol = targetSymbol.OriginalDefinition;

        var sourceLocations = targetSymbol.Locations
            .Where(loc => loc.IsInSource)
            .ToList();

        if (sourceLocations.Count == 0)
        {
            return GotoDefinitionResult.Metadata(
                targetSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                targetSymbol.Kind.ToString(),
                targetSymbol.ContainingAssembly?.Name,
                snapshotVersion);
        }

        var locations = new List<DefinitionLocation>();
        foreach (var loc in sourceLocations)
        {
            var span = loc.GetLineSpan();
            string? snippet = null;
            if (loc.SourceTree is { } tree)
            {
                var text = await tree.GetTextAsync(ct);
                var zeroBasedLine = span.StartLinePosition.Line;
                if (zeroBasedLine >= 0 && zeroBasedLine < text.Lines.Count)
                    snippet = text.Lines[zeroBasedLine].ToString().Trim();
            }

            locations.Add(new DefinitionLocation(
                span.ToRepoPath(),
                span.StartLinePosition.Line + 1,
                span.StartLinePosition.Character + 1,
                snippet));
        }

        return GotoDefinitionResult.Found(
            targetSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            targetSymbol.Kind.ToString(),
            [.. locations],
            snapshotVersion);
    }
}

public sealed record GotoDefinitionResult(
    string Status,
    string? SymbolName,
    string? Kind,
    bool IsMetadata,
    string? AssemblyName,
    ImmutableList<DefinitionLocation> Locations,
    ImmutableList<SymbolCandidate> Candidates,
    string? Message)
{
    public long SnapshotVersion { get; init; }

    public static GotoDefinitionResult NotFound(string identifier, long snapshotVersion) => new(
        "not_found", null, null, false, null, [], [],
        $"Symbol '{identifier}' not found in the workspace")
    { SnapshotVersion = snapshotVersion };

    public static GotoDefinitionResult NotLoaded() => new(
        "not_loaded", null, null, false, null, [], [],
        "Workspace is still loading");

    public static GotoDefinitionResult LoadFailed(string message) => new(
        "load_failed", null, null, false, null, [], [], message);

    public static GotoDefinitionResult Ambiguous(string symbolName, ImmutableList<SymbolCandidate> candidates, long snapshotVersion) => new(
        "ambiguous", symbolName, null, false, null, [], candidates,
        $"Multiple symbols match '{symbolName}'. Use a fully qualified name to disambiguate.")
    { SnapshotVersion = snapshotVersion };

    public static GotoDefinitionResult Error(string message, long snapshotVersion) => new(
        "error", null, null, false, null, [], [], message)
    { SnapshotVersion = snapshotVersion };

    public static GotoDefinitionResult Found(string symbolName, string kind,
        ImmutableList<DefinitionLocation> locations, long snapshotVersion) => new(
        "found", symbolName, kind, false, null, locations, [], null)
        { SnapshotVersion = snapshotVersion };

    public static GotoDefinitionResult Metadata(
        string symbolName, string kind, string? assemblyName, long snapshotVersion) => new(
        "found", symbolName, kind, true, assemblyName, [], [],
        $"Symbol is defined in metadata assembly '{assemblyName}'. Use decompile-type to view source.")
        { SnapshotVersion = snapshotVersion };
}

public sealed record DefinitionLocation(RepoPath? FilePath, int Line, int Column, string? Snippet);
