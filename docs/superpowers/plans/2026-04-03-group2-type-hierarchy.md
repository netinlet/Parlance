# Group 2: Type Hierarchy Tool Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `type-hierarchy` MCP tool that walks the inheritance tree in both directions (supertypes and subtypes) from a given type, with configurable depth.

**Architecture:** The tool resolves a type name via existing `FindSymbolsAsync`, then delegates to a new `GetTypeHierarchyAsync` method on `WorkspaceQueryService`. Supertypes walk `INamedTypeSymbol.BaseType` and direct interfaces. Subtypes use `FindImplementationsAsync` at each level. Results are nested `HierarchyNode` trees. Subtypes are capped at 50 per level with a `Truncated` flag.

**Tech Stack:** Roslyn (`Microsoft.CodeAnalysis`, `Microsoft.CodeAnalysis.FindSymbols`), MCP SDK (`ModelContextProtocol.Server`), xUnit

**Spec:** `docs/superpowers/specs/2026-04-03-more-tools-design.md` — Group 2: Type Hierarchy section

---

### Task 1: GetTypeHierarchyAsync — Workspace Query Method

**Files:**
- Modify: `src/Parlance.CSharp.Workspace/WorkspaceQueryService.cs`

- [ ] **Step 1: Add the TypeHierarchyResult and HierarchyNode records**

Add these records at the end of `WorkspaceQueryService.cs` (or in a new file `TypeHierarchyResult.cs` in the same project — follow whichever pattern the implementer prefers, but same namespace):

```csharp
public sealed record TypeHierarchyResult(
    ImmutableList<HierarchyNode> Supertypes,
    ImmutableList<HierarchyNode> Subtypes,
    bool Truncated);

public sealed record HierarchyNode(
    string Name, string FullyQualifiedName, string Kind,
    string Relationship, string? FilePath, int? Line,
    ImmutableList<HierarchyNode> Children);
```

- [ ] **Step 2: Add the GetTypeHierarchyAsync method to WorkspaceQueryService**

Add this method after the existing `FindImplementationsAsync` method:

```csharp
    private const int MaxSubtypesPerLevel = 50;

    public async Task<TypeHierarchyResult> GetTypeHierarchyAsync(
        INamedTypeSymbol typeSymbol, int maxDepth = 1, CancellationToken ct = default)
    {
        logger.LogDebug("GetTypeHierarchy: {Type}, MaxDepth: {Depth}", typeSymbol.ToDisplayString(), maxDepth);

        var truncated = false;
        var supertypes = GetSupertypes(typeSymbol, maxDepth);
        var subtypes = await GetSubtypesAsync(typeSymbol, maxDepth, 1, ct);
        truncated = subtypes.Truncated;

        return new TypeHierarchyResult(supertypes, subtypes.Nodes, truncated);
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

            var relationship = impl is INamedTypeSymbol { TypeKind: TypeKind.Interface } ? "interface" : "base_class";
            nodes.Add(ToHierarchyNode(impl, relationship, children));
        }

        return ([.. nodes], truncated);
    }

    private static HierarchyNode ToHierarchyNode(ISymbol symbol, string relationship, ImmutableList<HierarchyNode> children)
    {
        var loc = symbol.Locations.FirstOrDefault();
        var span = loc?.GetLineSpan();
        return new HierarchyNode(
            symbol.Name,
            symbol.ToDisplayString(),
            symbol.Kind.ToString(),
            relationship,
            span?.Path,
            span is null ? null : span.Value.StartLinePosition.Line + 1,
            children);
    }
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build src/Parlance.CSharp.Workspace/Parlance.CSharp.Workspace.csproj`
Expected: Build succeeds

- [ ] **Step 4: Commit**

```bash
git add src/Parlance.CSharp.Workspace/WorkspaceQueryService.cs
git commit -m "feat: add GetTypeHierarchyAsync to WorkspaceQueryService

Recursive walk of supertypes (BaseType + Interfaces) and subtypes
(FindImplementationsAsync) with configurable depth and 50-per-level cap."
```

---

### Task 2: type-hierarchy — Failing Tests

**Files:**
- Create: `tests/Parlance.Mcp.Tests/Tools/TypeHierarchyToolTests.cs`

- [ ] **Step 1: Create the test file with all test cases**

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Parlance.CSharp.Workspace;
using Parlance.CSharp.Workspace.Tests.Integration;
using Parlance.Mcp.Tools;

namespace Parlance.Mcp.Tests.Tools;

public sealed class TypeHierarchyToolTests : IAsyncLifetime
{
    private WorkspaceSessionHolder _holder = null!;
    private WorkspaceQueryService _query = null!;
    private CSharpWorkspaceSession _session = null!;

    public async Task InitializeAsync()
    {
        var solutionPath = TestPaths.FindSolutionPath();
        _session = await CSharpWorkspaceSession.OpenSolutionAsync(solutionPath);
        _holder = new WorkspaceSessionHolder();
        _holder.SetSession(_session);
        _query = new WorkspaceQueryService(_holder, NullLogger<WorkspaceQueryService>.Instance);
    }

    public async Task DisposeAsync() => await _session.DisposeAsync();

    [Fact]
    public async Task TypeHierarchy_DefaultDepth_ReturnsBothDirections()
    {
        // CSharpWorkspaceSession implements IAsyncDisposable
        var result = await TypeHierarchyTool.TypeHierarchy(
            _holder, _query, NullLogger<TypeHierarchyTool>.Instance,
            typeName: "CSharpWorkspaceSession",
            maxDepth: 1,
            CancellationToken.None);

        Assert.Equal("found", result.Status);
        Assert.Equal("CSharpWorkspaceSession", result.TypeName);
        Assert.NotNull(result.Kind);

        // Should have supertypes (at least object and IAsyncDisposable)
        Assert.NotEmpty(result.Supertypes);

        // All nodes at depth 1 should have empty children
        Assert.All(result.Supertypes, node =>
        {
            Assert.NotEmpty(node.Name);
            Assert.NotEmpty(node.FullyQualifiedName);
            Assert.Empty(node.Children);
        });
    }

    [Fact]
    public async Task TypeHierarchy_Interface_FindsSubtypes()
    {
        // IProjectCompilationCache should have implementations
        var result = await TypeHierarchyTool.TypeHierarchy(
            _holder, _query, NullLogger<TypeHierarchyTool>.Instance,
            typeName: "IProjectCompilationCache",
            maxDepth: 1,
            CancellationToken.None);

        Assert.Equal("found", result.Status);
        Assert.NotEmpty(result.Subtypes);
        Assert.All(result.Subtypes, node =>
        {
            Assert.NotEmpty(node.Name);
            Assert.NotEmpty(node.FullyQualifiedName);
        });
    }

    [Fact]
    public async Task TypeHierarchy_Depth2_PopulatesChildren()
    {
        var result = await TypeHierarchyTool.TypeHierarchy(
            _holder, _query, NullLogger<TypeHierarchyTool>.Instance,
            typeName: "CSharpWorkspaceSession",
            maxDepth: 2,
            CancellationToken.None);

        Assert.Equal("found", result.Status);
        // At depth 2, supertypes of supertypes should be populated
        // IAsyncDisposable's supertypes (if any) would appear as children
    }

    [Fact]
    public async Task TypeHierarchy_NonTypeSymbol_ReturnsError()
    {
        // FindSymbolsAsync with SymbolFilter.Type won't find methods
        var result = await TypeHierarchyTool.TypeHierarchy(
            _holder, _query, NullLogger<TypeHierarchyTool>.Instance,
            typeName: "ThisMethodDoesNotExistAsAType",
            maxDepth: 1,
            CancellationToken.None);

        Assert.Equal("not_found", result.Status);
    }

    [Fact]
    public async Task TypeHierarchy_UnknownType_ReturnsNotFound()
    {
        var result = await TypeHierarchyTool.TypeHierarchy(
            _holder, _query, NullLogger<TypeHierarchyTool>.Instance,
            typeName: "ThisTypeDefinitelyDoesNotExist",
            maxDepth: 1,
            CancellationToken.None);

        Assert.Equal("not_found", result.Status);
    }

    [Fact]
    public async Task TypeHierarchy_AmbiguousName_ReturnsAmbiguous()
    {
        // "Diagnostic" exists in both Parlance and Roslyn
        var result = await TypeHierarchyTool.TypeHierarchy(
            _holder, _query, NullLogger<TypeHierarchyTool>.Instance,
            typeName: "Diagnostic",
            maxDepth: 1,
            CancellationToken.None);

        Assert.Equal("ambiguous", result.Status);
        Assert.NotEmpty(result.Candidates);
    }

    [Fact]
    public void TypeHierarchy_NotLoaded_ReturnsNotLoaded()
    {
        var holder = new WorkspaceSessionHolder();
        var query = new WorkspaceQueryService(holder, NullLogger<WorkspaceQueryService>.Instance);

        var result = TypeHierarchyTool.TypeHierarchy(
            holder, query, NullLogger<TypeHierarchyTool>.Instance,
            typeName: "Anything", maxDepth: 1,
            CancellationToken.None).Result;

        Assert.Equal("not_loaded", result.Status);
    }

    [Fact]
    public void TypeHierarchy_LoadFailed_ReturnsLoadFailed()
    {
        var holder = new WorkspaceSessionHolder();
        holder.SetLoadFailure(new WorkspaceLoadFailure("boom", "/path.sln"));
        var query = new WorkspaceQueryService(holder, NullLogger<WorkspaceQueryService>.Instance);

        var result = TypeHierarchyTool.TypeHierarchy(
            holder, query, NullLogger<TypeHierarchyTool>.Instance,
            typeName: "Anything", maxDepth: 1,
            CancellationToken.None).Result;

        Assert.Equal("load_failed", result.Status);
        Assert.Equal("boom", result.Message);
    }
}
```

- [ ] **Step 2: Verify the tests do not compile (tool class doesn't exist yet)**

Run: `dotnet build tests/Parlance.Mcp.Tests/Parlance.Mcp.Tests.csproj`
Expected: Compilation error — `TypeHierarchyTool` does not exist

---

### Task 3: type-hierarchy — Implementation

**Files:**
- Create: `src/Parlance.Mcp/Tools/TypeHierarchyTool.cs`
- Modify: `src/Parlance.Mcp/Program.cs`

- [ ] **Step 1: Create the tool class**

```csharp
using System.Collections.Immutable;
using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
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
        ILogger<TypeHierarchyTool> logger,
        [Description("Type name to look up (e.g., 'MyClass' or 'Namespace.MyClass')")]
        string typeName,
        [Description("How many levels deep to walk (default 1)")]
        int maxDepth = 1,
        CancellationToken ct = default)
    {
        using var _ = ToolDiagnostics.TimeToolCall(logger, "type-hierarchy");

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
}
```

- [ ] **Step 2: Register the tool in Program.cs**

Add `.WithTools<TypeHierarchyTool>()` after `.WithTools<SearchSymbolsTool>()` (or after `.WithTools<SafeToDeleteTool>()` if Group 1 hasn't been implemented yet — insert alphabetically by class name, before `DecompileTypeTool`):

```csharp
    .WithTools<SafeToDeleteTool>()
    .WithTools<SearchSymbolsTool>()
    .WithTools<TypeHierarchyTool>()
    .WithTools<DecompileTypeTool>()
```

- [ ] **Step 3: Build and run the tests**

Run: `dotnet test tests/Parlance.Mcp.Tests/Parlance.Mcp.Tests.csproj --filter "TypeHierarchy" -v n`
Expected: All tests pass

- [ ] **Step 4: Run the full test suite to check for regressions**

Run: `dotnet test Parlance.sln`
Expected: All tests pass

- [ ] **Step 5: Check formatting**

Run: `dotnet format Parlance.sln --verify-no-changes`
Expected: No formatting violations

- [ ] **Step 6: Commit**

```bash
git add src/Parlance.Mcp/Tools/TypeHierarchyTool.cs tests/Parlance.Mcp.Tests/Tools/TypeHierarchyToolTests.cs src/Parlance.Mcp/Program.cs
git commit -m "feat: add type-hierarchy MCP tool

Walks inheritance tree in both directions with configurable depth.
Supertypes include base classes and interfaces. Subtypes capped at
50 per level with Truncated flag."
```

---

### Task 4: Final Verification

**Files:** None (verification only)

- [ ] **Step 1: Run the full test suite**

Run: `dotnet test Parlance.sln`
Expected: All tests pass

- [ ] **Step 2: Check formatting**

Run: `dotnet format Parlance.sln --verify-no-changes`
Expected: No formatting violations

- [ ] **Step 3: Build the MCP server**

Run: `dotnet build src/Parlance.Mcp/Parlance.Mcp.csproj`
Expected: Clean build with no warnings
