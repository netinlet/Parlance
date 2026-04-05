# Group 1: Navigation Tools Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `goto-definition` and `search-symbols` MCP tools to Parlance, enabling agents to jump to symbol definitions and fuzzy-search for symbols across the workspace.

**Architecture:** Both tools follow the existing MCP tool pattern (sealed class, static async method, structured result record). `goto-definition` combines existing `FindSymbolsAsync` (name path) and `GetSymbolAtPositionAsync` (position path) to locate definitions. `search-symbols` adds a new `SearchSymbolsAsync` method to `WorkspaceQueryService` that uses `SymbolFinder.FindDeclarationsAsync` with substring matching. No new projects — just new files in `Parlance.Mcp/Tools/` and a new method in `WorkspaceQueryService`.

**Tech Stack:** Roslyn (`Microsoft.CodeAnalysis`, `Microsoft.CodeAnalysis.FindSymbols`), MCP SDK (`ModelContextProtocol.Server`), xUnit

**Spec:** `docs/superpowers/specs/2026-04-03-more-tools-design.md` — Group 1: Navigation section

---

### Task 1: goto-definition — Failing Tests

**Files:**
- Create: `tests/Parlance.Mcp.Tests/Tools/GotoDefinitionToolTests.cs`

- [ ] **Step 1: Create the test file with all test cases**

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Parlance.CSharp.Workspace;
using Parlance.CSharp.Workspace.Tests.Integration;
using Parlance.Mcp.Tools;

namespace Parlance.Mcp.Tests.Tools;

public sealed class GotoDefinitionToolTests : IAsyncLifetime
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
    public async Task GotoDefinition_ByName_FindsSourceDefinition()
    {
        var result = await GotoDefinitionTool.GotoDefinition(
            _holder, _query, NullLogger<GotoDefinitionTool>.Instance,
            symbolName: "CSharpWorkspaceSession",
            filePath: null, line: null, column: null,
            CancellationToken.None);

        Assert.Equal("found", result.Status);
        Assert.Equal("CSharpWorkspaceSession", result.SymbolName);
        Assert.False(result.IsMetadata);
        Assert.Null(result.AssemblyName);
        Assert.NotEmpty(result.Locations);
        Assert.All(result.Locations, loc =>
        {
            Assert.NotEmpty(loc.FilePath);
            Assert.True(loc.Line > 0);
            Assert.NotNull(loc.Snippet);
        });
    }

    [Fact]
    public async Task GotoDefinition_ByPosition_FindsDefinition()
    {
        var filePath = Path.Combine(TestPaths.RepoRoot,
            "src", "Parlance.CSharp.Workspace", "WorkspaceQueryService.cs");
        var lines = await File.ReadAllLinesAsync(filePath);

        // Find a reference to CSharpWorkspaceSession (the Session property)
        var refLine = Array.FindIndex(lines, l => l.Contains("holder.Session"));
        Assert.True(refLine >= 0, "Could not find 'holder.Session' reference");

        var refCol = lines[refLine].IndexOf("Session", StringComparison.Ordinal);

        var result = await GotoDefinitionTool.GotoDefinition(
            _holder, _query, NullLogger<GotoDefinitionTool>.Instance,
            symbolName: null,
            filePath: filePath, line: refLine + 1, column: refCol + 1,
            CancellationToken.None);

        Assert.Equal("found", result.Status);
        Assert.NotNull(result.SymbolName);
        Assert.NotEmpty(result.Locations);
    }

    [Fact]
    public async Task GotoDefinition_BothInputs_PositionTakesPrecedence()
    {
        var filePath = Path.Combine(TestPaths.RepoRoot,
            "src", "Parlance.CSharp.Workspace", "WorkspaceQueryService.cs");
        var lines = await File.ReadAllLinesAsync(filePath);

        var refLine = Array.FindIndex(lines, l => l.Contains("holder.Session"));
        Assert.True(refLine >= 0);
        var refCol = lines[refLine].IndexOf("Session", StringComparison.Ordinal);

        // Provide both a name (different symbol) and a position — position should win
        var result = await GotoDefinitionTool.GotoDefinition(
            _holder, _query, NullLogger<GotoDefinitionTool>.Instance,
            symbolName: "SymbolCandidate",
            filePath: filePath, line: refLine + 1, column: refCol + 1,
            CancellationToken.None);

        Assert.Equal("found", result.Status);
        // Result should be for the position symbol, not "SymbolCandidate"
        Assert.DoesNotContain("SymbolCandidate", result.SymbolName);
    }

    [Fact]
    public async Task GotoDefinition_MetadataSymbol_ReturnsIsMetadata()
    {
        // ISymbol is defined in Microsoft.CodeAnalysis, not in our source
        var result = await GotoDefinitionTool.GotoDefinition(
            _holder, _query, NullLogger<GotoDefinitionTool>.Instance,
            symbolName: "ISymbol",
            filePath: null, line: null, column: null,
            CancellationToken.None);

        Assert.Equal("found", result.Status);
        Assert.True(result.IsMetadata);
        Assert.NotNull(result.AssemblyName);
        Assert.Empty(result.Locations);
    }

    [Fact]
    public async Task GotoDefinition_UnknownSymbol_ReturnsNotFound()
    {
        var result = await GotoDefinitionTool.GotoDefinition(
            _holder, _query, NullLogger<GotoDefinitionTool>.Instance,
            symbolName: "ThisSymbolDefinitelyDoesNotExist",
            filePath: null, line: null, column: null,
            CancellationToken.None);

        Assert.Equal("not_found", result.Status);
        Assert.Empty(result.Locations);
    }

    [Fact]
    public async Task GotoDefinition_AmbiguousName_ReturnsAmbiguous()
    {
        // "Diagnostic" exists in both Parlance and Roslyn
        var result = await GotoDefinitionTool.GotoDefinition(
            _holder, _query, NullLogger<GotoDefinitionTool>.Instance,
            symbolName: "Diagnostic",
            filePath: null, line: null, column: null,
            CancellationToken.None);

        Assert.Equal("ambiguous", result.Status);
        Assert.NotEmpty(result.Candidates);
    }

    [Fact]
    public async Task GotoDefinition_NoInputs_ReturnsError()
    {
        var result = await GotoDefinitionTool.GotoDefinition(
            _holder, _query, NullLogger<GotoDefinitionTool>.Instance,
            symbolName: null,
            filePath: null, line: null, column: null,
            CancellationToken.None);

        Assert.Equal("error", result.Status);
        Assert.NotNull(result.Message);
    }

    [Fact]
    public void GotoDefinition_NotLoaded_ReturnsNotLoaded()
    {
        var holder = new WorkspaceSessionHolder();
        var query = new WorkspaceQueryService(holder, NullLogger<WorkspaceQueryService>.Instance);

        var result = GotoDefinitionTool.GotoDefinition(
            holder, query, NullLogger<GotoDefinitionTool>.Instance,
            symbolName: "Anything",
            filePath: null, line: null, column: null,
            CancellationToken.None).Result;

        Assert.Equal("not_loaded", result.Status);
    }

    [Fact]
    public void GotoDefinition_LoadFailed_ReturnsLoadFailed()
    {
        var holder = new WorkspaceSessionHolder();
        holder.SetLoadFailure(new WorkspaceLoadFailure("boom", "/path.sln"));
        var query = new WorkspaceQueryService(holder, NullLogger<WorkspaceQueryService>.Instance);

        var result = GotoDefinitionTool.GotoDefinition(
            holder, query, NullLogger<GotoDefinitionTool>.Instance,
            symbolName: "Anything",
            filePath: null, line: null, column: null,
            CancellationToken.None).Result;

        Assert.Equal("load_failed", result.Status);
        Assert.Equal("boom", result.Message);
    }
}
```

- [ ] **Step 2: Verify the tests do not compile (tool class doesn't exist yet)**

Run: `dotnet build tests/Parlance.Mcp.Tests/Parlance.Mcp.Tests.csproj`
Expected: Compilation error — `GotoDefinitionTool` does not exist

---

### Task 2: goto-definition — Implementation

**Files:**
- Create: `src/Parlance.Mcp/Tools/GotoDefinitionTool.cs`
- Modify: `src/Parlance.Mcp/Program.cs:36-47`

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
        var hasName = symbolName is not null;

        if (!hasPosition && !hasName)
            return GotoDefinitionResult.Error("Provide either symbolName or filePath + line + column.");

        ISymbol? targetSymbol = null;

        // Position path takes precedence
        if (hasPosition)
        {
            var zeroLine = line!.Value - 1;
            var zeroCol = column!.Value - 1;
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
                return GotoDefinitionResult.Ambiguous(symbolName!, symbols.Select(s => s.ToCandidate()).ToImmutableList());

            targetSymbol = symbols[0].Symbol;
        }

        // For methods, navigate to the original definition (not overrides)
        targetSymbol = targetSymbol.OriginalDefinition;

        var sourceLocations = targetSymbol.Locations
            .Where(loc => loc.IsInSource)
            .ToList();

        if (sourceLocations.Count == 0)
        {
            // Metadata symbol — no source available
            var assemblyName = targetSymbol.ContainingAssembly?.Name;
            return new GotoDefinitionResult(
                Status: "found",
                SymbolName: targetSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                Kind: targetSymbol.Kind.ToString(),
                IsMetadata: true,
                AssemblyName: assemblyName,
                Locations: [],
                Candidates: [],
                Message: $"Symbol is defined in metadata assembly '{assemblyName}'. Use decompile-type to view source.");
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

        return new GotoDefinitionResult(
            Status: "found",
            SymbolName: targetSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            Kind: targetSymbol.Kind.ToString(),
            IsMetadata: false,
            AssemblyName: null,
            Locations: [.. locations],
            Candidates: [],
            Message: null);
    }
}

public sealed record GotoDefinitionResult(
    string Status, string? SymbolName, string? Kind,
    bool IsMetadata, string? AssemblyName,
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
}

public sealed record DefinitionLocation(string FilePath, int Line, int Column, string? Snippet);
```

- [ ] **Step 2: Register the tool in Program.cs**

Add `.WithTools<GotoDefinitionTool>()` after the existing tool registrations. Insert alphabetically — after `.WithTools<FindReferencesTool>()` and before `.WithTools<GetTypeAtTool>()`:

```csharp
    .WithTools<FindReferencesTool>()
    .WithTools<GotoDefinitionTool>()
    .WithTools<GetTypeAtTool>()
```

- [ ] **Step 3: Build and run the tests**

Run: `dotnet test tests/Parlance.Mcp.Tests/Parlance.Mcp.Tests.csproj --filter "GotoDefinition" -v n`
Expected: All tests pass

- [ ] **Step 4: Run the full test suite to check for regressions**

Run: `dotnet test Parlance.sln`
Expected: All tests pass

- [ ] **Step 5: Check formatting**

Run: `dotnet format Parlance.sln --verify-no-changes`
Expected: No formatting violations

- [ ] **Step 6: Commit**

```bash
git add src/Parlance.Mcp/Tools/GotoDefinitionTool.cs tests/Parlance.Mcp.Tests/Tools/GotoDefinitionToolTests.cs src/Parlance.Mcp/Program.cs
git commit -m "feat: add goto-definition MCP tool

Supports name-based and position-based lookup. Returns source locations
with snippets, or metadata flag for external symbols."
```

---

### Task 3: search-symbols — WorkspaceQueryService.SearchSymbolsAsync

**Files:**
- Modify: `src/Parlance.CSharp.Workspace/WorkspaceQueryService.cs`

- [ ] **Step 1: Add the SearchSymbolsAsync method**

Add this method to `WorkspaceQueryService`, after the existing `FindSymbolsAsync` method (after line 42):

```csharp
    public async Task<(ImmutableList<ResolvedSymbol> Results, int TotalCount)> SearchSymbolsAsync(
        string query, SymbolFilter? kindFilter = null, int maxResults = 25,
        CancellationToken ct = default)
    {
        logger.LogDebug("SearchSymbols: {Query}, Kind: {Kind}, Max: {Max}", query, kindFilter, maxResults);

        var results = new List<ResolvedSymbol>();
        await foreach (var (project, _) in GetCompilationsAsync(ct))
        {
            var declarations = await SymbolFinder.FindDeclarationsAsync(
                project, query, ignoreCase: true, filter: kindFilter ?? SymbolFilter.All, ct);
            results.AddRange(declarations.Select(s => new ResolvedSymbol(s, project)));
        }

        var deduplicated = results
            .DistinctBy(r => r.Symbol.ToDisplayString())
            .ToList();

        var totalCount = deduplicated.Count;
        var capped = deduplicated.Take(maxResults).ToImmutableList();
        return (capped, totalCount);
    }
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build src/Parlance.CSharp.Workspace/Parlance.CSharp.Workspace.csproj`
Expected: Build succeeds

- [ ] **Step 3: Commit**

```bash
git add src/Parlance.CSharp.Workspace/WorkspaceQueryService.cs
git commit -m "feat: add SearchSymbolsAsync to WorkspaceQueryService

Fuzzy/substring symbol search across all projects with optional kind
filter and result cap. Distinct from FindSymbolsAsync which does exact
name matching."
```

---

### Task 4: search-symbols — Failing Tests

**Files:**
- Create: `tests/Parlance.Mcp.Tests/Tools/SearchSymbolsToolTests.cs`

- [ ] **Step 1: Create the test file with all test cases**

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Parlance.CSharp.Workspace;
using Parlance.CSharp.Workspace.Tests.Integration;
using Parlance.Mcp.Tools;

namespace Parlance.Mcp.Tests.Tools;

public sealed class SearchSymbolsToolTests : IAsyncLifetime
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
    public async Task SearchSymbols_SubstringMatch_FindsResults()
    {
        var result = await SearchSymbolsTool.SearchSymbols(
            _holder, _query, NullLogger<SearchSymbolsTool>.Instance,
            searchQuery: "Workspace", kind: null, maxResults: 25,
            CancellationToken.None);

        Assert.Equal("found", result.Status);
        Assert.NotEmpty(result.Matches);
        Assert.True(result.TotalMatches > 0);
        Assert.All(result.Matches, m =>
        {
            Assert.NotEmpty(m.DisplayName);
            Assert.NotEmpty(m.FullyQualifiedName);
            Assert.NotEmpty(m.Kind);
            Assert.NotEmpty(m.ProjectName);
        });
    }

    [Fact]
    public async Task SearchSymbols_KindFilter_ReturnsOnlyMatchingKind()
    {
        var result = await SearchSymbolsTool.SearchSymbols(
            _holder, _query, NullLogger<SearchSymbolsTool>.Instance,
            searchQuery: "Workspace", kind: "class", maxResults: 25,
            CancellationToken.None);

        Assert.Equal("found", result.Status);
        Assert.NotEmpty(result.Matches);
        Assert.All(result.Matches, m => Assert.Contains("Type", m.Kind));
    }

    [Fact]
    public async Task SearchSymbols_MaxResults_CapsOutput()
    {
        var result = await SearchSymbolsTool.SearchSymbols(
            _holder, _query, NullLogger<SearchSymbolsTool>.Instance,
            searchQuery: "Get", kind: null, maxResults: 3,
            CancellationToken.None);

        Assert.Equal("found", result.Status);
        Assert.True(result.Matches.Count <= 3);
        Assert.True(result.TotalMatches >= result.Matches.Count);
    }

    [Fact]
    public async Task SearchSymbols_NoMatches_ReturnsNoMatches()
    {
        var result = await SearchSymbolsTool.SearchSymbols(
            _holder, _query, NullLogger<SearchSymbolsTool>.Instance,
            searchQuery: "XyzzyNonexistentSymbolName", kind: null, maxResults: 25,
            CancellationToken.None);

        Assert.Equal("no_matches", result.Status);
        Assert.Empty(result.Matches);
        Assert.Equal(0, result.TotalMatches);
    }

    [Fact]
    public void SearchSymbols_NotLoaded_ReturnsNotLoaded()
    {
        var holder = new WorkspaceSessionHolder();
        var query = new WorkspaceQueryService(holder, NullLogger<WorkspaceQueryService>.Instance);

        var result = SearchSymbolsTool.SearchSymbols(
            holder, query, NullLogger<SearchSymbolsTool>.Instance,
            searchQuery: "Anything", kind: null, maxResults: 25,
            CancellationToken.None).Result;

        Assert.Equal("not_loaded", result.Status);
    }

    [Fact]
    public void SearchSymbols_LoadFailed_ReturnsLoadFailed()
    {
        var holder = new WorkspaceSessionHolder();
        holder.SetLoadFailure(new WorkspaceLoadFailure("boom", "/path.sln"));
        var query = new WorkspaceQueryService(holder, NullLogger<WorkspaceQueryService>.Instance);

        var result = SearchSymbolsTool.SearchSymbols(
            holder, query, NullLogger<SearchSymbolsTool>.Instance,
            searchQuery: "Anything", kind: null, maxResults: 25,
            CancellationToken.None).Result;

        Assert.Equal("load_failed", result.Status);
        Assert.Equal("boom", result.Message);
    }
}
```

- [ ] **Step 2: Verify the tests do not compile (tool class doesn't exist yet)**

Run: `dotnet build tests/Parlance.Mcp.Tests/Parlance.Mcp.Tests.csproj`
Expected: Compilation error — `SearchSymbolsTool` does not exist

---

### Task 5: search-symbols — Implementation

**Files:**
- Create: `src/Parlance.Mcp/Tools/SearchSymbolsTool.cs`
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
public sealed class SearchSymbolsTool
{
    private static readonly Dictionary<string, SymbolFilter> KindMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["class"] = SymbolFilter.Type,
        ["struct"] = SymbolFilter.Type,
        ["interface"] = SymbolFilter.Type,
        ["enum"] = SymbolFilter.Type,
        ["method"] = SymbolFilter.Member,
        ["property"] = SymbolFilter.Member,
        ["field"] = SymbolFilter.Member,
        ["event"] = SymbolFilter.Member,
    };

    private static readonly Dictionary<string, TypeKind> TypeKindMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["class"] = TypeKind.Class,
        ["struct"] = TypeKind.Struct,
        ["interface"] = TypeKind.Interface,
        ["enum"] = TypeKind.Enum,
    };

    private static readonly Dictionary<string, SymbolKind> MemberKindMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["method"] = SymbolKind.Method,
        ["property"] = SymbolKind.Property,
        ["field"] = SymbolKind.Field,
        ["event"] = SymbolKind.Event,
    };

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

        // Resolve kind to SymbolFilter for the Roslyn API
        SymbolFilter? symbolFilter = null;
        if (kind is not null && KindMap.TryGetValue(kind, out var filter))
            symbolFilter = filter;

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

        return new SearchSymbolsResult(
            Status: "found",
            Query: searchQuery,
            Matches: matches,
            TotalMatches: totalMatches,
            Message: null);
    }
}

public sealed record SearchSymbolsResult(
    string Status, string? Query,
    ImmutableList<SymbolMatch> Matches,
    int TotalMatches,
    string? Message)
{
    public static SearchSymbolsResult NoMatches(string searchQuery) => new(
        "no_matches", searchQuery, [], 0, $"No symbols matching '{searchQuery}' found in the workspace");
    public static SearchSymbolsResult NotLoaded() => new(
        "not_loaded", null, [], 0, "Workspace is still loading");
    public static SearchSymbolsResult LoadFailed(string message) => new(
        "load_failed", null, [], 0, message);
}

public sealed record SymbolMatch(
    string DisplayName, string FullyQualifiedName, string Kind,
    string ProjectName, string? FilePath, int? Line);
```

- [ ] **Step 2: Register the tool in Program.cs**

Add `.WithTools<SearchSymbolsTool>()` after `.WithTools<SafeToDeleteTool>()` and before `.WithTools<DecompileTypeTool>()`:

```csharp
    .WithTools<SafeToDeleteTool>()
    .WithTools<SearchSymbolsTool>()
    .WithTools<DecompileTypeTool>()
```

- [ ] **Step 3: Build and run the tests**

Run: `dotnet test tests/Parlance.Mcp.Tests/Parlance.Mcp.Tests.csproj --filter "SearchSymbols" -v n`
Expected: All tests pass

- [ ] **Step 4: Run the full test suite to check for regressions**

Run: `dotnet test Parlance.sln`
Expected: All tests pass

- [ ] **Step 5: Check formatting**

Run: `dotnet format Parlance.sln --verify-no-changes`
Expected: No formatting violations

- [ ] **Step 6: Commit**

```bash
git add src/Parlance.Mcp/Tools/SearchSymbolsTool.cs tests/Parlance.Mcp.Tests/Tools/SearchSymbolsToolTests.cs src/Parlance.Mcp/Program.cs
git commit -m "feat: add search-symbols MCP tool

Fuzzy symbol search across the workspace with optional kind filter
and maxResults cap. Returns display name, qualified name, kind,
project, and source location for each match."
```

---

### Task 6: Final Verification

**Files:** None (verification only)

- [ ] **Step 1: Run the full test suite**

Run: `dotnet test Parlance.sln`
Expected: All tests pass, including all existing tests and the new GotoDefinition and SearchSymbols tests

- [ ] **Step 2: Check formatting**

Run: `dotnet format Parlance.sln --verify-no-changes`
Expected: No formatting violations

- [ ] **Step 3: Build the MCP server**

Run: `dotnet build src/Parlance.Mcp/Parlance.Mcp.csproj`
Expected: Clean build with no warnings
