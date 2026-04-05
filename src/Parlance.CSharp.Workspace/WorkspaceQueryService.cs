using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;

namespace Parlance.CSharp.Workspace;

public sealed class WorkspaceQueryService(WorkspaceSessionHolder holder, ILogger<WorkspaceQueryService> logger)
{
    private CSharpWorkspaceSession Session => holder.Session;

    public async Task<ImmutableList<ResolvedSymbol>> FindSymbolsAsync(
        string name, SymbolFilter filter = SymbolFilter.All, bool ignoreCase = false,
        CancellationToken ct = default)
    {
        logger.LogDebug("FindSymbols: {Name}, Filter: {Filter}, IgnoreCase: {IgnoreCase}", name, filter, ignoreCase);

        var solution = Session.CurrentSolution;
        var solutionAssemblyNames = solution.Projects
            .Select(p => p.AssemblyName).ToHashSet();

        // FindDeclarationsAsync matches by simple (unqualified) name only.
        // If the caller supplied a qualified name (e.g. "Parlance.Abstractions.Diagnostic"),
        // search by the last segment and post-filter by display string.
        var isQualified = name.Contains('.');
        var simpleName = isQualified ? name[(name.LastIndexOf('.') + 1)..] : name;

        var results = new List<ResolvedSymbol>();
        foreach (var project in solution.Projects)
        {
            var declarations = await SymbolFinder.FindDeclarationsAsync(project, simpleName, ignoreCase, filter, ct);
            results.AddRange(declarations.Select(s => new ResolvedSymbol(s, project)));
        }

        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return results
            .DistinctBy(r => r.Symbol.ToDisplayString())
            .Where(r => !isQualified || MatchesQualifiedName(r.Symbol, name, comparison))
            .OrderByDescending(r => solutionAssemblyNames.Contains(r.Symbol.ContainingAssembly?.Name ?? "") ? 1 : 0)
            .ToImmutableList();
    }

    // ToDisplayString() includes parameter lists for methods, so "Ns.Type.Method" won't
    // EndsWith-match "Ns.Type.Method(int, string)". Build the parameter-free path as a fallback.
    private static bool MatchesQualifiedName(ISymbol symbol, string qualifiedName, StringComparison comparison)
    {
        if (symbol.ToDisplayString().EndsWith(qualifiedName, comparison))
            return true;

        var parts = new List<string> { symbol.Name };
        var containingType = symbol.ContainingType;
        while (containingType is not null)
        {
            parts.Insert(0, containingType.Name);
            containingType = containingType.ContainingType;
        }
        var ns = symbol.ContainingNamespace;
        if (ns is not null && !ns.IsGlobalNamespace)
            parts.Insert(0, ns.ToDisplayString());
        return string.Join(".", parts).EndsWith(qualifiedName, comparison);
    }

    public async Task<(ImmutableList<ResolvedSymbol> Results, int TotalCount)> SearchSymbolsAsync(
        string query, SymbolFilter? kindFilter = null, int maxResults = 25,
        CancellationToken ct = default)
    {
        logger.LogDebug("SearchSymbols: {Query}, Kind: {Kind}, Max: {Max}", query, kindFilter, maxResults);

        var solution = Session.CurrentSolution;
        var results = new List<ResolvedSymbol>(maxResults);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var totalCount = 0;

        foreach (var project in solution.Projects)
        {
            var declarations = await SymbolFinder.FindDeclarationsAsync(
                project, query, ignoreCase: true, filter: kindFilter ?? SymbolFilter.All, ct);

            foreach (var symbol in declarations)
            {
                if (!symbol.CanBeReferencedByName)
                    continue;
                if (!seen.Add(symbol.ToDisplayString()))
                    continue;

                totalCount++;
                if (results.Count < maxResults)
                    results.Add(new ResolvedSymbol(symbol, project));
            }
        }

        return (results.ToImmutableList(), totalCount);
    }

    public async Task<Compilation?> GetCompilationAsync(string projectName, CancellationToken ct = default)
    {
        var solution = Session.CurrentSolution;
        var project = solution.Projects.FirstOrDefault(p => p.Name == projectName);
        return project is null ? null : await GetCompilationAsync(project, ct);
    }

    public async Task<Compilation> GetCompilationAsync(Project project, CancellationToken ct = default)
    {
        var state = await Session.GetCompilationStateAsync(project, ct);
        return state.Compilation;
    }

    public async IAsyncEnumerable<(Project Project, Compilation Compilation)> GetCompilationsAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var solution = Session.CurrentSolution;
        foreach (var project in solution.Projects)
        {
            Compilation compilation;
            try
            {
                compilation = await GetCompilationAsync(project, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Skipping project {Name}: compilation failed", project.Name);
                continue;
            }

            yield return (project, compilation);
        }
    }

    public async Task<SemanticModel?> GetSemanticModelAsync(string filePath, CancellationToken ct = default)
    {
        var solution = Session.CurrentSolution;
        var docId = solution.GetDocumentIdsWithFilePath(filePath).FirstOrDefault();
        if (docId is null) return null;

        var document = solution.GetDocument(docId);
        if (document is null) return null;

        var compilation = await GetCompilationAsync(document.Project, ct);
        var tree = await document.GetSyntaxTreeAsync(ct);
        return tree is not null ? compilation.GetSemanticModel(tree) : null;
    }

    public async Task<ImmutableList<ReferencedSymbol>> FindReferencesAsync(ISymbol symbol, CancellationToken ct = default)
    {
        logger.LogDebug("FindReferences: {Symbol}", symbol.ToDisplayString());
        var references = await SymbolFinder.FindReferencesAsync(symbol, Session.CurrentSolution, ct);
        return [.. references];
    }

    public async Task<ImmutableList<ISymbol>> FindImplementationsAsync(ISymbol symbol, CancellationToken ct = default)
    {
        logger.LogDebug("FindImplementations: {Symbol}", symbol.ToDisplayString());
        var implementations = await SymbolFinder.FindImplementationsAsync(symbol, Session.CurrentSolution, cancellationToken: ct);
        return [.. implementations];
    }

    private const int MaxSubtypesPerLevel = 50;

    public async Task<TypeHierarchyResult> GetTypeHierarchyAsync(
        INamedTypeSymbol typeSymbol, int maxDepth = 1, CancellationToken ct = default)
    {
        logger.LogDebug("GetTypeHierarchy: {Type}, MaxDepth: {Depth}", typeSymbol.ToDisplayString(), maxDepth);

        var supertypes = GetSupertypes(typeSymbol, maxDepth);
        var subtypes = await GetSubtypesAsync(typeSymbol, maxDepth, 1, ct);

        return new TypeHierarchyResult(supertypes, subtypes.Nodes, subtypes.Truncated);
    }

    private static ImmutableList<HierarchyNode> GetSupertypes(INamedTypeSymbol typeSymbol, int maxDepth, int currentDepth = 1)
    {
        if (currentDepth > maxDepth)
            return [];

        var nodes = new List<HierarchyNode>();

        // Base class
        if (typeSymbol.BaseType is { } baseType && baseType.SpecialType != SpecialType.System_Object)
        {
            var children = currentDepth < maxDepth
                ? GetSupertypes(baseType, maxDepth, currentDepth + 1)
                : [];
            nodes.Add(ToHierarchyNode(baseType, "base_class", children));
        }
        else if (typeSymbol.BaseType is { SpecialType: SpecialType.System_Object } objectType)
        {
            // Include object but don't recurse past it
            nodes.Add(ToHierarchyNode(objectType, "base_class", []));
        }

        // Direct interfaces (not inherited ones)
        foreach (var iface in typeSymbol.Interfaces)
        {
            var children = currentDepth < maxDepth
                ? GetSupertypes(iface, maxDepth, currentDepth + 1)
                : [];
            nodes.Add(ToHierarchyNode(iface, "interface", children));
        }

        return [.. nodes];
    }

    private async Task<(ImmutableList<HierarchyNode> Nodes, bool Truncated)> GetSubtypesAsync(
        ISymbol typeSymbol, int maxDepth, int currentDepth, CancellationToken ct)
    {
        if (currentDepth > maxDepth)
            return ([], false);

        var implementations = await FindImplementationsAsync(typeSymbol, ct);
        var truncated = implementations.Count > MaxSubtypesPerLevel;
        var capped = implementations.Take(MaxSubtypesPerLevel).ToList();

        // Relationship describes how the child relates to the parent:
        // if the parent is an interface, children "implement" it; if a class, children "extend" it
        var relationship = typeSymbol is INamedTypeSymbol { TypeKind: TypeKind.Interface } ? "interface" : "base_class";

        var nodes = new List<HierarchyNode>();
        foreach (var impl in capped)
        {
            var children = ImmutableList<HierarchyNode>.Empty;
            if (currentDepth < maxDepth && impl is INamedTypeSymbol namedImpl)
            {
                var (childNodes, childTruncated) = await GetSubtypesAsync(namedImpl, maxDepth, currentDepth + 1, ct);
                children = childNodes;
                truncated = truncated || childTruncated;
            }

            nodes.Add(ToHierarchyNode(impl, relationship, children));
        }

        return ([.. nodes], truncated);
    }

    private static HierarchyNode ToHierarchyNode(ISymbol symbol, string relationship, ImmutableList<HierarchyNode> children)
    {
        var loc = symbol.Locations.FirstOrDefault();
        var span = loc?.GetLineSpan();
        var kind = symbol is INamedTypeSymbol namedType
            ? namedType.TypeKind.ToString()
            : symbol.Kind.ToString();
        return new HierarchyNode(
            symbol.Name,
            symbol.ToDisplayString(),
            kind,
            relationship,
            span?.Path,
            span is null ? null : span.Value.StartLinePosition.Line + 1,
            children);
    }

    public async Task<ISymbol?> GetSymbolAtPositionAsync(string filePath, int line, int column, CancellationToken ct = default)
    {
        var semanticModel = await GetSemanticModelAsync(filePath, ct);
        if (semanticModel is null) return null;

        var text = await semanticModel.SyntaxTree.GetTextAsync(ct);
        if (line < 0 || line >= text.Lines.Count) return null;
        var lineLength = text.Lines[line].Span.Length;
        if (column < 0 || column > lineLength) return null;
        var position = text.Lines.GetPosition(new LinePosition(line, column));
        var root = await semanticModel.SyntaxTree.GetRootAsync(ct);
        var node = root.FindToken(position).Parent;
        if (node is null) return null;

        var symbolInfo = semanticModel.GetSymbolInfo(node, ct);
        if (symbolInfo.Symbol is not null) return symbolInfo.Symbol;
        if (symbolInfo.CandidateSymbols.Length > 0) return symbolInfo.CandidateSymbols[0];

        // Declarations (class/method/property names) resolve via GetDeclaredSymbol, not GetSymbolInfo
        return semanticModel.GetDeclaredSymbol(node, ct);
    }
}
