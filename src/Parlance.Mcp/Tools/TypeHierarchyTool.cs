using System.Collections.Immutable;
using System.ComponentModel;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;
using Parlance.CSharp.Workspace;

namespace Parlance.Mcp.Tools;

[McpServerToolType]
public sealed class TypeHierarchyTool
{
    [McpServerTool(Name = "type-hierarchy", ReadOnly = true)]
    [Description("Walk the inheritance tree of a type in both directions. " +
                 "Returns supertypes (base classes, interfaces) and subtypes (classes/structs that inherit or implement). " +
                 "Use maxDepth to control how many levels deep to walk (default 1).")]
    public static async Task<TypeHierarchyToolResult> TypeHierarchy(
        WorkspaceSessionHolder holder, WorkspaceQueryService query,
        [Description("Type name to look up (e.g., 'MyClass' or 'Namespace.MyClass')")]
        string typeName,
        [Description("How many levels deep to walk (default 1)")]
        int maxDepth = 1,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return TypeHierarchyToolResult.Error("typeName is required.");
        if (maxDepth < 1)
            return TypeHierarchyToolResult.Error("maxDepth must be >= 1.");

        if (holder.LoadFailure is { } failure)
            return TypeHierarchyToolResult.LoadFailed(failure.Message);
        if (!holder.IsLoaded)
            return TypeHierarchyToolResult.NotLoaded();

        var symbols = await query.FindSymbolsAsync(typeName, SymbolFilter.Type, ct: ct);
        if (symbols.IsEmpty)
            return TypeHierarchyToolResult.NotFound(typeName);

        if (symbols.Count > 1 && !typeName.Contains('.'))
            return TypeHierarchyToolResult.Ambiguous(typeName, symbols.Select(s => s.ToCandidate()).ToImmutableList());

        if (symbols[0].Symbol is not INamedTypeSymbol namedType)
            return TypeHierarchyToolResult.NotFound(typeName);

        var hierarchy = await query.GetTypeHierarchyAsync(namedType, maxDepth, ct);

        return new TypeHierarchyToolResult(
            Status: "found",
            TypeName: namedType.Name,
            Kind: namedType.TypeKind.ToString(),
            Supertypes: hierarchy.Supertypes,
            Subtypes: hierarchy.Subtypes,
            Truncated: hierarchy.Truncated,
            Candidates: [],
            Message: null);
    }
}

public sealed record TypeHierarchyToolResult(
    string Status, string? TypeName, string? Kind,
    ImmutableList<HierarchyNode> Supertypes,
    ImmutableList<HierarchyNode> Subtypes,
    bool Truncated,
    ImmutableList<SymbolCandidate> Candidates,
    string? Message)
{
    public static TypeHierarchyToolResult NotFound(string typeName) => new(
        "not_found", typeName, null, [], [], false, [],
        $"Type '{typeName}' not found in the workspace");
    public static TypeHierarchyToolResult NotLoaded() => new(
        "not_loaded", null, null, [], [], false, [],
        "Workspace is still loading");
    public static TypeHierarchyToolResult LoadFailed(string message) => new(
        "load_failed", null, null, [], [], false, [], message);
    public static TypeHierarchyToolResult Ambiguous(string typeName, ImmutableList<SymbolCandidate> candidates) => new(
        "ambiguous", typeName, null, [], [], false, candidates,
        $"Multiple types match '{typeName}'. Use a fully qualified name to disambiguate.");
    public static TypeHierarchyToolResult Error(string message) => new(
        "error", null, null, [], [], false, [], message);
}
