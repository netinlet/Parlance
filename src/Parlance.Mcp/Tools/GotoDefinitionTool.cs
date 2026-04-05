using System.Collections.Immutable;
using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Parlance.CSharp.Workspace;

namespace Parlance.Mcp.Tools;

[McpServerToolType]
public sealed class GotoDefinitionTool
{
    [McpServerTool(Name = "goto-definition", ReadOnly = true)]
    [Description("Go to the definition of a symbol. Provide either a symbolName for name-based lookup, " +
                 "or filePath + line + column (1-based) for position-based lookup. " +
                 "If both are provided, position takes precedence.")]
    public static async Task<GotoDefinitionResult> GotoDefinition(
        WorkspaceSessionHolder holder, WorkspaceQueryService query,
        ILogger<GotoDefinitionTool> logger,
        [Description("Symbol name to look up (e.g., 'MyClass' or 'Namespace.MyClass')")]
        string? symbolName = null,
        [Description("File path for position-based lookup")]
        string? filePath = null,
        [Description("1-based line number (required with filePath)")]
        int? line = null,
        [Description("1-based column number (required with filePath)")]
        int? column = null,
        CancellationToken ct = default)
    {
        using var _ = ToolDiagnostics.TimeToolCall(logger, "goto-definition");

        if (holder.LoadFailure is { } failure)
            return GotoDefinitionResult.LoadFailed(failure.Message);
        if (!holder.IsLoaded)
            return GotoDefinitionResult.NotLoaded();

        var hasPosition = filePath is not null && line is not null && column is not null;
        var hasPartialPosition = filePath is not null && (line is null || column is null);
        var hasName = symbolName is not null;

        if (hasPartialPosition)
            return GotoDefinitionResult.Error("Position-based lookup requires filePath, line, and column.");

        if (!hasPosition && !hasName)
            return GotoDefinitionResult.Error("Provide either symbolName or filePath + line + column.");

        ISymbol? targetSymbol;

        if (hasPosition)
        {
            if (line!.Value < 1 || column!.Value < 1)
                return GotoDefinitionResult.Error("line and column must be >= 1 (1-based).");

            var zeroLine = line.Value - 1;
            var zeroCol = column.Value - 1;
            targetSymbol = await query.GetSymbolAtPositionAsync(filePath!, zeroLine, zeroCol, ct);

            if (targetSymbol is null)
                return GotoDefinitionResult.NotFound(filePath!);
        }
        else
        {
            var symbols = await query.FindSymbolsAsync(symbolName!, ct: ct);
            if (symbols.IsEmpty)
                return GotoDefinitionResult.NotFound(symbolName!);

            if (symbols.Count > 1 && !symbolName!.Contains('.'))
            {
                return GotoDefinitionResult.Ambiguous(symbolName,
                    [.. symbols.Select(s => s.ToCandidate())]);
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
                targetSymbol.ContainingAssembly?.Name);
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
                span.Path,
                span.StartLinePosition.Line + 1,
                span.StartLinePosition.Character + 1,
                snippet));
        }

        return GotoDefinitionResult.Found(
            targetSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            targetSymbol.Kind.ToString(),
            [.. locations]);
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
    public static GotoDefinitionResult NotFound(string identifier) => new(
        "not_found", null, null, false, null, [], [],
        $"Symbol '{identifier}' not found in the workspace");

    public static GotoDefinitionResult NotLoaded() => new(
        "not_loaded", null, null, false, null, [], [],
        "Workspace is still loading");

    public static GotoDefinitionResult LoadFailed(string message) => new(
        "load_failed", null, null, false, null, [], [], message);

    public static GotoDefinitionResult Ambiguous(string symbolName, ImmutableList<SymbolCandidate> candidates) => new(
        "ambiguous", symbolName, null, false, null, [], candidates,
        $"Multiple symbols match '{symbolName}'. Use a fully qualified name to disambiguate.");

    public static GotoDefinitionResult Error(string message) => new(
        "error", null, null, false, null, [], [], message);

    public static GotoDefinitionResult Found(string symbolName, string kind,
        ImmutableList<DefinitionLocation> locations) => new(
        "found", symbolName, kind, false, null, locations, [], null);

    public static GotoDefinitionResult Metadata(string symbolName, string kind, string? assemblyName) => new(
        "found", symbolName, kind, true, assemblyName, [], [],
        $"Symbol is defined in metadata assembly '{assemblyName}'. Use decompile-type to view source.");
}

public sealed record DefinitionLocation(string FilePath, int Line, int Column, string? Snippet);
