using System.Collections.Immutable;
using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Parlance.CSharp.Workspace;

namespace Parlance.Mcp.Tools;

[McpServerToolType]
public sealed class GetTypeDependenciesTool
{
    [McpServerTool(Name = "get-type-dependencies", ReadOnly = true)]
    [Description("Returns what a type depends on (dependencies) and what depends on it (dependents), " +
                 "scoped to solution-defined types only.")]
    public static async Task<GetTypeDependenciesResult> GetTypeDependencies(
        WorkspaceSessionHolder holder, WorkspaceQueryService query,
        ILogger<GetTypeDependenciesTool> logger, string typeName, CancellationToken ct)
    {
        using var _ = ToolDiagnostics.TimeToolCall(logger, "get-type-dependencies");

        if (holder.LoadFailure is { } failure)
            return GetTypeDependenciesResult.LoadFailed(failure.Message);
        if (!holder.IsLoaded)
            return GetTypeDependenciesResult.NotLoaded();

        var symbols = await query.FindSymbolsAsync(typeName, SymbolFilter.Type, ct: ct);
        if (symbols.IsEmpty)
            return GetTypeDependenciesResult.NotFound(typeName);

        if (symbols.Count > 1 && !typeName.Contains('.'))
            return GetTypeDependenciesResult.Ambiguous(typeName, symbols.Select(s => s.ToCandidate()).ToImmutableList());

        var resolved = symbols[0];
        if (resolved.Symbol is not INamedTypeSymbol typeSymbol)
            return GetTypeDependenciesResult.NotFound(typeName);

        // Get all solution assembly names to filter out framework types
        var solutionAssemblies = holder.Session.CurrentSolution.Projects
            .Select(p => p.AssemblyName)
            .ToHashSet();

        bool IsInSolution(ITypeSymbol? t) =>
            t is not null && solutionAssemblies.Contains(t.ContainingAssembly?.Name ?? "");

        // === DEPENDENCIES ===
        var depsBuilder = ImmutableList.CreateBuilder<TypeDependencyEntry>();
        var seenDeps = new HashSet<string>();

        // Base type
        if (typeSymbol.BaseType is { } baseType && IsInSolution(baseType) &&
            baseType.SpecialType is not SpecialType.System_Object)
        {
            seenDeps.Add($"{baseType.ToDisplayString()}:inherits");
            depsBuilder.Add(new TypeDependencyEntry(
                baseType.Name, baseType.ToDisplayString(), "inherits"));
        }

        // Interfaces
        foreach (var iface in typeSymbol.Interfaces.Where(IsInSolution))
        {
            seenDeps.Add($"{iface.ToDisplayString()}:implements");
            depsBuilder.Add(new TypeDependencyEntry(
                iface.Name, iface.ToDisplayString(), "implements"));
        }

        // Member types (fields, properties, method params/returns)
        foreach (var member in typeSymbol.GetMembers())
        {
            ITypeSymbol? memberType = member switch
            {
                IFieldSymbol f => f.Type,
                IPropertySymbol p => p.Type,
                IMethodSymbol m when m.MethodKind is MethodKind.Ordinary => m.ReturnType,
                _ => null
            };

            if (memberType is not null && IsInSolution(memberType) &&
                !SymbolEqualityComparer.Default.Equals(memberType, typeSymbol))
            {
                var kind = member switch
                {
                    IFieldSymbol => "field",
                    IPropertySymbol => "property",
                    _ => "return-type"
                };
                var key = $"{memberType.ToDisplayString()}:{kind}";
                if (seenDeps.Add(key))
                    depsBuilder.Add(new TypeDependencyEntry(
                        memberType.Name, memberType.ToDisplayString(), kind));
            }

            // Method parameters
            if (member is IMethodSymbol method && method.MethodKind is MethodKind.Ordinary)
            {
                foreach (var param in method.Parameters)
                {
                    if (IsInSolution(param.Type) &&
                        !SymbolEqualityComparer.Default.Equals(param.Type, typeSymbol))
                    {
                        var key = $"{param.Type.ToDisplayString()}:parameter";
                        if (seenDeps.Add(key))
                            depsBuilder.Add(new TypeDependencyEntry(
                                param.Type.Name, param.Type.ToDisplayString(), "parameter"));
                    }
                }
            }
        }

        // === DEPENDENTS ===
        var dependentsBuilder = ImmutableList.CreateBuilder<TypeDependencyEntry>();
        var references = await query.FindReferencesAsync(typeSymbol, ct);
        var seenDependents = new HashSet<string>();

        // Group by tree so each file's root and semantic model are fetched once.
        var locationsByTree = references
            .SelectMany(r => r.Locations)
            .Where(l => l.Location.IsInSource && l.Location.SourceTree is not null)
            .GroupBy(l => l.Location.SourceTree!);

        foreach (var treeGroup in locationsByTree)
        {
            var tree = treeGroup.Key;
            var root = await tree.GetRootAsync(ct);
            var semanticModel = await query.GetSemanticModelAsync(tree.FilePath, ct);
            if (semanticModel is null) continue;

            foreach (var location in treeGroup)
            {
                var node = root.FindNode(location.Location.SourceSpan);
                var containingTypeDecl = node.Ancestors()
                    .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax>()
                    .FirstOrDefault();
                if (containingTypeDecl is null) continue;

                var containingTypeSymbol = semanticModel.GetDeclaredSymbol(containingTypeDecl, ct) as INamedTypeSymbol;
                if (containingTypeSymbol is null) continue;
                if (!IsInSolution(containingTypeSymbol)) continue;
                if (SymbolEqualityComparer.Default.Equals(containingTypeSymbol, typeSymbol)) continue;

                var key = containingTypeSymbol.ToDisplayString();
                if (!seenDependents.Add(key)) continue;

                dependentsBuilder.Add(new TypeDependencyEntry(
                    containingTypeSymbol.Name,
                    containingTypeSymbol.ToDisplayString(),
                    "references"));
            }
        }

        return GetTypeDependenciesResult.Found(
            typeSymbol.ToDisplayString(), depsBuilder.ToImmutable(), dependentsBuilder.ToImmutable());
    }
}

public sealed record GetTypeDependenciesResult(
    string Status, string? TypeName,
    ImmutableList<TypeDependencyEntry> Dependencies,
    ImmutableList<TypeDependencyEntry> Dependents,
    ImmutableList<SymbolCandidate> Candidates,
    string? Message)
{
    public static GetTypeDependenciesResult NotFound(string typeName) => new(
        "not_found", typeName, [], [], [], $"Type '{typeName}' not found");
    public static GetTypeDependenciesResult NotLoaded() => new(
        "not_loaded", null, [], [], [], "Workspace is still loading");
    public static GetTypeDependenciesResult LoadFailed(string message) => new(
        "load_failed", null, [], [], [], message);
    public static GetTypeDependenciesResult Ambiguous(string typeName, ImmutableList<SymbolCandidate> candidates) => new(
        "ambiguous", typeName, [], [], candidates,
        $"Multiple types match '{typeName}'. Use a fully qualified name to disambiguate.");
    public static GetTypeDependenciesResult Found(
        string typeName, ImmutableList<TypeDependencyEntry> dependencies,
        ImmutableList<TypeDependencyEntry> dependents) => new(
        "found", typeName, dependencies, dependents, [], null);
}

public sealed record TypeDependencyEntry(string Name, string FullyQualifiedName, string Relationship);
