# Milestone 3: Semantic Navigation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the shared query layer (`WorkspaceQueryService`) and 10 semantic navigation MCP tools that let Claude query types, find references, trace implementations, and understand code structure.

**Architecture:** `WorkspaceQueryService` is a singleton in `Parlance.CSharp.Workspace` that wraps Roslyn's `SymbolFinder`, `Compilation`, and `SemanticModel` APIs. MCP tools in `Parlance.Mcp` are thin formatting layers — they call the query service, shape results for LLM consumption, and handle error states. The query service returns raw Roslyn types; the Roslyn-free boundary is at the tool output (serialized records).

**Tech Stack:** Roslyn (`Microsoft.CodeAnalysis`), `SymbolFinder`, `SemanticModel`, MCP SDK (`ModelContextProtocol`), ICSharpCode.Decompiler (for `decompile-type`).

**Spec:** `docs/superpowers/specs/2026-03-23-milestone-3-semantic-navigation-design.md`

---

## File Structure

### New files

| File | Responsibility |
|------|---------------|
| `src/Parlance.CSharp.Workspace/WorkspaceQueryService.cs` | Shared query library — symbol resolution, compilation access, cross-solution queries |
| `src/Parlance.CSharp.Workspace/ResolvedSymbol.cs` | `ISymbol` + `Project` pair returned by `FindSymbolsAsync` |
| `src/Parlance.CSharp.Workspace/SymbolCandidate.cs` | Lightweight record for presenting ambiguous matches, with `From()` factory |
| `src/Parlance.CSharp.Workspace/SymbolExtensions.cs` | `ToCandidate()` extension method on `ResolvedSymbol` |
| `src/Parlance.Mcp/Tools/DescribeTypeTool.cs` | MCP tool + result record |
| `src/Parlance.Mcp/Tools/FindImplementationsTool.cs` | MCP tool + result record |
| `src/Parlance.Mcp/Tools/FindReferencesTool.cs` | MCP tool + result record |
| `src/Parlance.Mcp/Tools/GetTypeAtTool.cs` | MCP tool + result record |
| `src/Parlance.Mcp/Tools/OutlineFileTool.cs` | MCP tool + result record |
| `src/Parlance.Mcp/Tools/GetSymbolDocsTool.cs` | MCP tool + result record |
| `src/Parlance.Mcp/Tools/CallHierarchyTool.cs` | MCP tool + result record |
| `src/Parlance.Mcp/Tools/GetTypeDependenciesTool.cs` | MCP tool + result record |
| `src/Parlance.Mcp/Tools/SafeToDeleteTool.cs` | MCP tool + result record |
| `src/Parlance.Mcp/Tools/DecompileTypeTool.cs` | MCP tool + result record |
| `tests/Parlance.CSharp.Workspace.Tests/WorkspaceQueryServiceTests.cs` | Integration tests for query service against Parlance.sln |
| `tests/Parlance.Mcp.Tests/Tools/DescribeTypeToolTests.cs` | Unit tests for describe-type |
| `tests/Parlance.Mcp.Tests/Tools/FindImplementationsToolTests.cs` | Unit tests |
| `tests/Parlance.Mcp.Tests/Tools/FindReferencesToolTests.cs` | Unit tests |
| `tests/Parlance.Mcp.Tests/Tools/GetTypeAtToolTests.cs` | Unit tests |
| `tests/Parlance.Mcp.Tests/Tools/OutlineFileToolTests.cs` | Unit tests |
| `tests/Parlance.Mcp.Tests/Tools/GetSymbolDocsToolTests.cs` | Unit tests |
| `tests/Parlance.Mcp.Tests/Tools/CallHierarchyToolTests.cs` | Unit tests |
| `tests/Parlance.Mcp.Tests/Tools/GetTypeDependenciesToolTests.cs` | Unit tests |
| `tests/Parlance.Mcp.Tests/Tools/SafeToDeleteToolTests.cs` | Unit tests |
| `tests/Parlance.Mcp.Tests/Tools/DecompileTypeToolTests.cs` | Unit tests |

### Modified files

| File | Change |
|------|--------|
| `src/Parlance.CSharp.Workspace/CSharpWorkspaceSession.cs` | Add `internal GetCompilationStateAsync()` method |
| `src/Parlance.CSharp.Workspace/Parlance.CSharp.Workspace.csproj` | Add `InternalsVisibleTo` for `Parlance.Mcp` and `Parlance.Mcp.Tests` |
| `src/Parlance.Mcp/Program.cs` | Register `WorkspaceQueryService` singleton + all new tools |
| `src/Parlance.Mcp/WorkspaceSessionHolder.cs` | Change namespace to `Parlance.CSharp.Workspace` (move file) |
| `src/Parlance.Mcp/WorkspaceLoadFailure.cs` | Change namespace to `Parlance.CSharp.Workspace` (move file) |
| `src/Parlance.Mcp/WorkspaceSessionLifecycle.cs` | Remove now-redundant `using Parlance.CSharp.Workspace` |
| `src/Parlance.Mcp/Parlance.Mcp.csproj` | Add `ICSharpCode.Decompiler` package ref (Task 10) |

---

## Reference: common patterns

### Three-state check (every tool)

```csharp
if (holder.LoadFailure is { } failure)
    return SomeResult.LoadFailed(failure.Message);
if (!holder.IsLoaded)
    return SomeResult.NotLoaded();
// ... proceed with query
```

### Test helpers

- `TestPaths.FindSolutionPath()` — walks up from `AppContext.BaseDirectory` to find `Parlance.sln`
- `TestPaths.RepoRoot` — parent directory of the solution file
- `NullLogger<T>.Instance` — for unit tests that don't need log output

**Important:** `TestPaths` is `internal` in `Parlance.CSharp.Workspace.Tests`. For `Parlance.Mcp.Tests` to use it: (1) make `TestPaths` `public`, and (2) add a project reference from `Parlance.Mcp.Tests` to `Parlance.CSharp.Workspace.Tests`. Do this in Task 1 alongside the other project reference changes.

### Build & test commands

```bash
dotnet build src/Parlance.Mcp/Parlance.Mcp.csproj
dotnet test tests/Parlance.CSharp.Workspace.Tests/Parlance.CSharp.Workspace.Tests.csproj --filter "ClassName"
dotnet test tests/Parlance.Mcp.Tests/Parlance.Mcp.Tests.csproj --filter "ClassName"
dotnet format Parlance.sln --verify-no-changes
```

---

## Task 1: Move `WorkspaceSessionHolder` to the Workspace project

`WorkspaceQueryService` (in `Parlance.CSharp.Workspace`) takes `WorkspaceSessionHolder` via DI. Currently the holder lives in `Parlance.Mcp` — that would create a circular dependency. The holder has no MCP dependencies; it's a session lifecycle type that belongs with the session.

**Files:**
- Move: `src/Parlance.Mcp/WorkspaceSessionHolder.cs` → `src/Parlance.CSharp.Workspace/WorkspaceSessionHolder.cs`
- Move: `src/Parlance.Mcp/WorkspaceLoadFailure.cs` → `src/Parlance.CSharp.Workspace/WorkspaceLoadFailure.cs`
- Modify: `src/Parlance.CSharp.Workspace/Parlance.CSharp.Workspace.csproj`
- Modify: `src/Parlance.Mcp/WorkspaceSessionLifecycle.cs`
- Modify: `src/Parlance.Mcp/Tools/WorkspaceStatusTool.cs`
- Modify: `src/Parlance.Mcp/Program.cs`
- Modify: `tests/Parlance.Mcp.Tests/WorkspaceStatusToolTests.cs`

- [ ] **Step 1: Move the files and change namespaces**

Move `WorkspaceSessionHolder.cs` and `WorkspaceLoadFailure.cs` from `src/Parlance.Mcp/` to `src/Parlance.CSharp.Workspace/`. Change both namespaces from `Parlance.Mcp` to `Parlance.CSharp.Workspace`.

`WorkspaceLoadFailure.cs`:
```csharp
namespace Parlance.CSharp.Workspace;

public sealed record WorkspaceLoadFailure(string Message, string SolutionPath);
```

`WorkspaceSessionHolder.cs`:
```csharp
namespace Parlance.CSharp.Workspace;

public sealed class WorkspaceSessionHolder : IAsyncDisposable
{
    private volatile CSharpWorkspaceSession? _session;
    private volatile WorkspaceLoadFailure? _loadFailure;

    public CSharpWorkspaceSession Session =>
        _session ?? throw new InvalidOperationException("Workspace session is not yet loaded");

    public bool IsLoaded => _session is not null;
    public WorkspaceLoadFailure? LoadFailure => _loadFailure;

    internal void SetSession(CSharpWorkspaceSession session) => _session = session;
    internal void SetLoadFailure(WorkspaceLoadFailure failure) => _loadFailure = failure;

    public async ValueTask DisposeAsync()
    {
        if (_session is not null)
            await _session.DisposeAsync();
    }
}
```

- [ ] **Step 2: Add `InternalsVisibleTo` for `Parlance.Mcp`**

In `src/Parlance.CSharp.Workspace/Parlance.CSharp.Workspace.csproj`, add to the existing `InternalsVisibleTo` ItemGroup:

```xml
<InternalsVisibleTo Include="Parlance.Mcp" />
<InternalsVisibleTo Include="Parlance.Mcp.Tests" />
```

This lets `WorkspaceSessionLifecycle` (in Mcp) call `SetSession()` and `SetLoadFailure()` which are `internal`. The test project also needs access since tool unit tests call `SetSession`/`SetLoadFailure` to set up test state.

- [ ] **Step 3: Update Mcp imports**

In `src/Parlance.Mcp/WorkspaceSessionLifecycle.cs`: no changes needed — it already has `using Parlance.CSharp.Workspace;` for `CSharpWorkspaceSession`, which now also covers the moved holder types.

In `src/Parlance.Mcp/Tools/WorkspaceStatusTool.cs`: add `using Parlance.CSharp.Workspace;` — it's needed for `WorkspaceSessionHolder` which moved to that namespace.

In `src/Parlance.Mcp/Program.cs`: add `using Parlance.CSharp.Workspace;` — needed for `WorkspaceSessionHolder` and `WorkspaceQueryService`.

In `tests/Parlance.Mcp.Tests/WorkspaceStatusToolTests.cs`: add `using Parlance.CSharp.Workspace;` for `WorkspaceSessionHolder` and `WorkspaceLoadFailure`.

- [ ] **Step 4: Make `TestPaths` public and add project reference**

In `tests/Parlance.CSharp.Workspace.Tests/Integration/TestPaths.cs`: change `internal static class TestPaths` to `public static class TestPaths`.

In `tests/Parlance.Mcp.Tests/Parlance.Mcp.Tests.csproj`: add a project reference so tool tests can use `TestPaths`:

```xml
<ProjectReference Include="..\..\tests\Parlance.CSharp.Workspace.Tests\Parlance.CSharp.Workspace.Tests.csproj" />
```

- [ ] **Step 5: Build and run all tests**

```bash
dotnet build src/Parlance.Mcp/Parlance.Mcp.csproj
dotnet test tests/Parlance.Mcp.Tests/Parlance.Mcp.Tests.csproj
dotnet test tests/Parlance.CSharp.Workspace.Tests/Parlance.CSharp.Workspace.Tests.csproj
```

All existing tests must pass. This is a pure refactoring — no behavior changes.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "refactor: move WorkspaceSessionHolder to Parlance.CSharp.Workspace"
```

---

## Task 2: Foundation types and session internal method

**Files:**
- Create: `src/Parlance.CSharp.Workspace/ResolvedSymbol.cs`
- Create: `src/Parlance.CSharp.Workspace/SymbolCandidate.cs`
- Create: `src/Parlance.CSharp.Workspace/SymbolExtensions.cs`
- Modify: `src/Parlance.CSharp.Workspace/CSharpWorkspaceSession.cs`

- [ ] **Step 1: Create `ResolvedSymbol.cs`**

```csharp
using Microsoft.CodeAnalysis;

namespace Parlance.CSharp.Workspace;

/// <summary>A symbol paired with the project it was resolved from.</summary>
public sealed record ResolvedSymbol(ISymbol Symbol, Project Project);
```

- [ ] **Step 2: Create `SymbolCandidate.cs`**

```csharp
using Microsoft.CodeAnalysis;

namespace Parlance.CSharp.Workspace;

/// <summary>Lightweight view for presenting ambiguous matches to callers.</summary>
public sealed record SymbolCandidate(
    string DisplayName, string FullyQualifiedName, string Kind,
    string ProjectName, string? FilePath, int? Line)
{
    public static SymbolCandidate From(ResolvedSymbol resolved) => new(
        resolved.Symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
        resolved.Symbol.ToDisplayString(), resolved.Symbol.Kind.ToString(), resolved.Project.Name,
        resolved.Symbol.Locations.FirstOrDefault()?.GetLineSpan().Path,
        resolved.Symbol.Locations.FirstOrDefault()?.GetLineSpan().StartLinePosition.Line);
}
```

- [ ] **Step 3: Create `SymbolExtensions.cs`**

```csharp
namespace Parlance.CSharp.Workspace;

public static class SymbolExtensions
{
    public static SymbolCandidate ToCandidate(this ResolvedSymbol resolved) =>
        SymbolCandidate.From(resolved);
}
```

- [ ] **Step 4: Add `GetCompilationStateAsync` to `CSharpWorkspaceSession`**

In `src/Parlance.CSharp.Workspace/CSharpWorkspaceSession.cs`, add this method (anywhere in the class body, near the other internal members):

```csharp
internal Task<ProjectCompilationState> GetCompilationStateAsync(Project project, CancellationToken ct = default) =>
    _cache.GetAsync(project, ct);
```

- [ ] **Step 5: Build**

```bash
dotnet build src/Parlance.CSharp.Workspace/Parlance.CSharp.Workspace.csproj
```

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: add ResolvedSymbol, SymbolCandidate, and GetCompilationStateAsync"
```

---

## Task 3: `WorkspaceQueryService` implementation and tests

**Files:**
- Create: `src/Parlance.CSharp.Workspace/WorkspaceQueryService.cs`
- Create: `tests/Parlance.CSharp.Workspace.Tests/WorkspaceQueryServiceTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/Parlance.CSharp.Workspace.Tests/WorkspaceQueryServiceTests.cs`:

```csharp
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using Parlance.CSharp.Workspace.Tests.Integration;

namespace Parlance.CSharp.Workspace.Tests;

public sealed class WorkspaceQueryServiceTests : IAsyncLifetime
{
    private CSharpWorkspaceSession _session = null!;
    private WorkspaceQueryService _query = null!;

    public async Task InitializeAsync()
    {
        var solutionPath = TestPaths.FindSolutionPath();
        _session = await CSharpWorkspaceSession.OpenSolutionAsync(solutionPath);
        var holder = new WorkspaceSessionHolder();
        holder.SetSession(_session);
        _query = new WorkspaceQueryService(holder, NullLogger<WorkspaceQueryService>.Instance);
    }

    public async Task DisposeAsync() => await _session.DisposeAsync();

    [Fact]
    public async Task FindSymbolsAsync_FindsTypeByName()
    {
        var results = await _query.FindSymbolsAsync("CSharpWorkspaceSession", SymbolFilter.Type);
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.Symbol.Name == "CSharpWorkspaceSession");
    }

    [Fact]
    public async Task FindSymbolsAsync_SolutionTypesRankFirst()
    {
        // "Diagnostic" exists in both Parlance.Abstractions and Microsoft.CodeAnalysis
        var results = await _query.FindSymbolsAsync("Diagnostic", SymbolFilter.Type);
        Assert.NotEmpty(results);
        var first = results[0];
        Assert.Contains("Parlance", first.Symbol.ContainingNamespace.ToDisplayString());
    }

    [Fact]
    public async Task FindSymbolsAsync_ReturnsProjectContext()
    {
        var results = await _query.FindSymbolsAsync("CSharpWorkspaceSession", SymbolFilter.Type);
        var match = results.First(r => r.Symbol.Name == "CSharpWorkspaceSession");
        Assert.Equal("Parlance.CSharp.Workspace", match.Project.Name);
    }

    [Fact]
    public async Task FindSymbolsAsync_EmptyForNonexistentType()
    {
        var results = await _query.FindSymbolsAsync("ThisTypeDoesNotExist12345", SymbolFilter.Type);
        Assert.Empty(results);
    }

    [Fact]
    public async Task GetCompilationAsync_ByName_ReturnsCompilation()
    {
        var compilation = await _query.GetCompilationAsync("Parlance.CSharp.Workspace");
        Assert.NotNull(compilation);
    }

    [Fact]
    public async Task GetCompilationAsync_ByName_ReturnsNullForUnknown()
    {
        var compilation = await _query.GetCompilationAsync("NonExistentProject");
        Assert.Null(compilation);
    }

    [Fact]
    public async Task GetCompilationsAsync_YieldsAllProjects()
    {
        var count = 0;
        await foreach (var (project, compilation) in _query.GetCompilationsAsync())
        {
            Assert.NotNull(compilation);
            count++;
        }
        Assert.True(count > 0);
    }

    [Fact]
    public async Task GetSemanticModelAsync_ReturnsModelForKnownFile()
    {
        var sessionFile = Path.Combine(TestPaths.RepoRoot,
            "src", "Parlance.CSharp.Workspace", "CSharpWorkspaceSession.cs");
        var model = await _query.GetSemanticModelAsync(sessionFile);
        Assert.NotNull(model);
    }

    [Fact]
    public async Task GetSemanticModelAsync_ReturnsNullForUnknownFile()
    {
        var model = await _query.GetSemanticModelAsync("/nonexistent/file.cs");
        Assert.Null(model);
    }

    [Fact]
    public async Task FindImplementationsAsync_FindsConcreteTypes()
    {
        var symbols = await _query.FindSymbolsAsync("IAnalysisEngine", SymbolFilter.Type);
        Assert.NotEmpty(symbols);
        var iface = symbols[0].Symbol;

        var implementations = await _query.FindImplementationsAsync(iface);
        Assert.NotEmpty(implementations);
    }

    [Fact]
    public async Task FindReferencesAsync_FindsUsages()
    {
        var symbols = await _query.FindSymbolsAsync("CSharpWorkspaceSession", SymbolFilter.Type);
        Assert.NotEmpty(symbols);

        var references = await _query.FindReferencesAsync(symbols[0].Symbol);
        Assert.NotEmpty(references);
    }

    [Fact]
    public async Task GetSymbolAtPositionAsync_ResolvesType()
    {
        // Line 15, column 20 in CSharpWorkspaceSession.cs is inside the class declaration
        var sessionFile = Path.Combine(TestPaths.RepoRoot,
            "src", "Parlance.CSharp.Workspace", "CSharpWorkspaceSession.cs");
        // Find the class declaration line dynamically
        var lines = await File.ReadAllLinesAsync(sessionFile);
        var classLine = Array.FindIndex(lines, l => l.Contains("class CSharpWorkspaceSession"));
        Assert.True(classLine >= 0, "Could not find class declaration");

        var classCol = lines[classLine].IndexOf("CSharpWorkspaceSession", StringComparison.Ordinal);
        var symbol = await _query.GetSymbolAtPositionAsync(sessionFile, classLine, classCol);
        Assert.NotNull(symbol);
        Assert.Equal("CSharpWorkspaceSession", symbol.Name);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/Parlance.CSharp.Workspace.Tests/Parlance.CSharp.Workspace.Tests.csproj --filter "WorkspaceQueryServiceTests"
```

Expected: compilation error — `WorkspaceQueryService` does not exist yet.

- [ ] **Step 3: Implement `WorkspaceQueryService`**

Create `src/Parlance.CSharp.Workspace/WorkspaceQueryService.cs`:

```csharp
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

        var solutionAssemblyNames = Session.CurrentSolution.Projects
            .Select(p => p.AssemblyName).ToHashSet();

        var results = new List<ResolvedSymbol>();
        await foreach (var (project, _) in GetCompilationsAsync(ct))
        {
            var declarations = await SymbolFinder.FindDeclarationsAsync(project, name, ignoreCase, filter, ct);
            results.AddRange(declarations.Select(s => new ResolvedSymbol(s, project)));
        }

        return results
            .DistinctBy(r => r.Symbol.ToDisplayString())
            .OrderByDescending(r => solutionAssemblyNames.Contains(r.Symbol.ContainingAssembly?.Name ?? "") ? 1 : 0)
            .ToImmutableList();
    }

    public async Task<Compilation?> GetCompilationAsync(string projectName, CancellationToken ct = default)
    {
        var project = Session.CurrentSolution.Projects.FirstOrDefault(p => p.Name == projectName);
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
        foreach (var project in Session.CurrentSolution.Projects)
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
        var docId = Session.CurrentSolution.GetDocumentIdsWithFilePath(filePath).FirstOrDefault();
        if (docId is null) return null;

        var document = Session.CurrentSolution.GetDocument(docId);
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

    public async Task<ISymbol?> GetSymbolAtPositionAsync(string filePath, int line, int column, CancellationToken ct = default)
    {
        var semanticModel = await GetSemanticModelAsync(filePath, ct);
        if (semanticModel is null) return null;

        var text = await semanticModel.SyntaxTree.GetTextAsync(ct);
        var position = text.Lines.GetPosition(new LinePosition(line, column));
        var root = await semanticModel.SyntaxTree.GetRootAsync(ct);
        var node = root.FindToken(position).Parent;
        if (node is null) return null;

        var symbolInfo = semanticModel.GetSymbolInfo(node, ct);
        return symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test tests/Parlance.CSharp.Workspace.Tests/Parlance.CSharp.Workspace.Tests.csproj --filter "WorkspaceQueryServiceTests"
```

Expected: all pass. Some tests may need adjustment based on exact Roslyn behavior — fix iteratively.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: add WorkspaceQueryService with symbol resolution and semantic queries"
```

---

## Task 4: DI wiring and `describe-type` tool

First MCP tool. Validates the full pattern end-to-end: DI → query service → formatted result.

**Files:**
- Modify: `src/Parlance.Mcp/Program.cs`
- Create: `src/Parlance.Mcp/Tools/DescribeTypeTool.cs`
- Create: `tests/Parlance.Mcp.Tests/Tools/DescribeTypeToolTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/Parlance.Mcp.Tests/Tools/DescribeTypeToolTests.cs`:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Parlance.CSharp.Workspace;
using Parlance.CSharp.Workspace.Tests.Integration;
using Parlance.Mcp.Tools;

namespace Parlance.Mcp.Tests.Tools;

public sealed class DescribeTypeToolTests : IAsyncLifetime
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
    public async Task DescribeType_FindsKnownType()
    {
        var result = await DescribeTypeTool.DescribeType(
            _holder, _query, NullLogger<DescribeTypeTool>.Instance,
            "CSharpWorkspaceSession", CancellationToken.None);

        Assert.Equal("found", result.Status);
        Assert.Equal("CSharpWorkspaceSession", result.Name);
        Assert.NotEmpty(result.Members);
    }

    [Fact]
    public async Task DescribeType_NotFound_ReturnsNotFound()
    {
        var result = await DescribeTypeTool.DescribeType(
            _holder, _query, NullLogger<DescribeTypeTool>.Instance,
            "ThisTypeDoesNotExist", CancellationToken.None);

        Assert.Equal("not_found", result.Status);
    }

    [Fact]
    public void DescribeType_NotLoaded_ReturnsNotLoaded()
    {
        var holder = new WorkspaceSessionHolder();
        var query = new WorkspaceQueryService(holder, NullLogger<WorkspaceQueryService>.Instance);

        var result = DescribeTypeTool.DescribeType(
            holder, query, NullLogger<DescribeTypeTool>.Instance,
            "Anything", CancellationToken.None).Result;

        Assert.Equal("not_loaded", result.Status);
    }

    [Fact]
    public void DescribeType_LoadFailed_ReturnsFailure()
    {
        var holder = new WorkspaceSessionHolder();
        holder.SetLoadFailure(new WorkspaceLoadFailure("boom", "/path.sln"));
        var query = new WorkspaceQueryService(holder, NullLogger<WorkspaceQueryService>.Instance);

        var result = DescribeTypeTool.DescribeType(
            holder, query, NullLogger<DescribeTypeTool>.Instance,
            "Anything", CancellationToken.None).Result;

        Assert.Equal("load_failed", result.Status);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test tests/Parlance.Mcp.Tests/Parlance.Mcp.Tests.csproj --filter "DescribeTypeToolTests"
```

Expected: compilation error — `DescribeTypeTool` does not exist.

- [ ] **Step 3: Implement `DescribeTypeTool`**

Create `src/Parlance.Mcp/Tools/DescribeTypeTool.cs`:

```csharp
using System.Collections.Immutable;
using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Parlance.CSharp.Workspace;

namespace Parlance.Mcp.Tools;

[McpServerToolType]
public sealed class DescribeTypeTool
{
    [McpServerTool(Name = "describe-type", ReadOnly = true)]
    [Description("Resolve a type by name and return its members, base types, interfaces, and accessibility. " +
                 "Use a fully qualified name to disambiguate (e.g., 'Parlance.Abstractions.Diagnostic').")]
    public static async Task<DescribeTypeResult> DescribeType(
        WorkspaceSessionHolder holder, WorkspaceQueryService query,
        ILogger<DescribeTypeTool> logger, string typeName, CancellationToken ct)
    {
        using var _ = ToolDiagnostics.TimeToolCall(logger, "describe-type");

        if (holder.LoadFailure is { } failure)
            return DescribeTypeResult.LoadFailed(failure.Message);
        if (!holder.IsLoaded)
            return DescribeTypeResult.NotLoaded();

        var symbols = await query.FindSymbolsAsync(typeName, SymbolFilter.Type, ct: ct);

        if (symbols.IsEmpty)
            return DescribeTypeResult.NotFound(typeName);

        if (symbols.Count > 1 && !typeName.Contains('.'))
            return DescribeTypeResult.Ambiguous(typeName, symbols.Select(s => s.ToCandidate()).ToImmutableList());

        var resolved = symbols[0];
        if (resolved.Symbol is not INamedTypeSymbol type)
            return DescribeTypeResult.NotFound(typeName);

        var members = type.GetMembers()
            .Where(m => m.DeclaredAccessibility is Accessibility.Public or Accessibility.Protected or Accessibility.Internal)
            .Where(m => m is not IMethodSymbol ms || ms.MethodKind is MethodKind.Ordinary or MethodKind.Constructor)
            .Select(m => new MemberEntry(
                m.Name, m.Kind.ToString(), m.DeclaredAccessibility.ToString(),
                m.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                m.IsStatic))
            .ToImmutableList();

        var baseTypes = new List<string>();
        var current = type.BaseType;
        while (current is not null && current.SpecialType is not SpecialType.System_Object)
        {
            baseTypes.Add(current.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
            current = current.BaseType;
        }

        var interfaces = type.AllInterfaces
            .Select(i => i.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat))
            .ToImmutableList();

        return new DescribeTypeResult(
            Status: "found",
            Name: type.Name,
            FullyQualifiedName: type.ToDisplayString(),
            Kind: type.TypeKind.ToString(),
            Accessibility: type.DeclaredAccessibility.ToString(),
            IsSealed: type.IsSealed,
            IsAbstract: type.IsAbstract,
            IsStatic: type.IsStatic,
            ProjectName: resolved.Project.Name,
            FilePath: type.Locations.FirstOrDefault()?.GetLineSpan().Path,
            Line: type.Locations.FirstOrDefault()?.GetLineSpan().StartLinePosition.Line,
            BaseTypes: [.. baseTypes],
            Interfaces: interfaces,
            Members: members,
            Candidates: [],
            Message: null);
    }
}

public sealed record DescribeTypeResult(
    string Status, string? Name, string? FullyQualifiedName, string? Kind,
    string? Accessibility, bool IsSealed, bool IsAbstract, bool IsStatic,
    string? ProjectName, string? FilePath, int? Line,
    ImmutableList<string> BaseTypes, ImmutableList<string> Interfaces,
    ImmutableList<MemberEntry> Members, ImmutableList<SymbolCandidate> Candidates,
    string? Message)
{
    public static DescribeTypeResult NotFound(string typeName) => new(
        "not_found", typeName, null, null, null, false, false, false,
        null, null, null, [], [], [], [], $"Type '{typeName}' not found in the workspace");

    public static DescribeTypeResult NotLoaded() => new(
        "not_loaded", null, null, null, null, false, false, false,
        null, null, null, [], [], [], [], "Workspace is still loading");

    public static DescribeTypeResult LoadFailed(string message) => new(
        "load_failed", null, null, null, null, false, false, false,
        null, null, null, [], [], [], [], message);

    public static DescribeTypeResult Ambiguous(string typeName, ImmutableList<SymbolCandidate> candidates) => new(
        "ambiguous", typeName, null, null, null, false, false, false,
        null, null, null, [], [], [], candidates,
        $"Multiple types match '{typeName}'. Use a fully qualified name to disambiguate.");
}

public sealed record MemberEntry(
    string Name, string Kind, string Accessibility, string Signature, bool IsStatic);
```

- [ ] **Step 4: Register in `Program.cs`**

In `src/Parlance.Mcp/Program.cs`, add DI registration and tool:

```csharp
// After: builder.Services.AddHostedService<WorkspaceSessionLifecycle>();
builder.Services.AddSingleton<WorkspaceQueryService>();

// Update the MCP chain:
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<WorkspaceStatusTool>()
    .WithTools<DescribeTypeTool>();
```

Add the required usings at the top of `Program.cs`:
```csharp
using Parlance.CSharp.Workspace;
```

- [ ] **Step 5: Run tests**

```bash
dotnet test tests/Parlance.Mcp.Tests/Parlance.Mcp.Tests.csproj --filter "DescribeTypeToolTests"
```

Expected: all pass.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: add describe-type MCP tool with WorkspaceQueryService DI wiring"
```

---

## Task 5: `find-implementations` and `find-references` tools

Both use `FindSymbolsAsync` → cross-solution query. Same structure as `describe-type`.

**Files:**
- Create: `src/Parlance.Mcp/Tools/FindImplementationsTool.cs`
- Create: `src/Parlance.Mcp/Tools/FindReferencesTool.cs`
- Create: `tests/Parlance.Mcp.Tests/Tools/FindImplementationsToolTests.cs`
- Create: `tests/Parlance.Mcp.Tests/Tools/FindReferencesToolTests.cs`
- Modify: `src/Parlance.Mcp/Program.cs` (register tools)

- [ ] **Step 1: Write failing tests for both tools**

`FindImplementationsToolTests.cs` — test that `find-implementations IAnalysisEngine` returns implementing types with project context and file paths. Test not-found, not-loaded, load-failed states.

`FindReferencesToolTests.cs` — test that `find-references CSharpWorkspaceSession` returns reference locations grouped by file. Test not-found, not-loaded, load-failed states.

- [ ] **Step 2: Implement `FindImplementationsTool`**

The tool calls `query.FindSymbolsAsync(typeName, SymbolFilter.Type)`, then `query.FindImplementationsAsync(symbol)`. Result record includes: status, target type name, list of implementing types with project/file/line.

- [ ] **Step 3: Implement `FindReferencesTool`**

The tool calls `query.FindSymbolsAsync(symbolName)`, then `query.FindReferencesAsync(symbol)`. Result record includes: status, symbol name, reference count, references grouped by file (file path, line numbers, context snippets).

For context snippets: read the source line from the `SyntaxTree` at each reference location — `tree.GetText().Lines[lineSpan.StartLinePosition.Line].ToString().Trim()`.

- [ ] **Step 4: Register both tools in `Program.cs`**

```csharp
.WithTools<FindImplementationsTool>()
.WithTools<FindReferencesTool>()
```

- [ ] **Step 5: Run tests, verify pass**

```bash
dotnet test tests/Parlance.Mcp.Tests/Parlance.Mcp.Tests.csproj --filter "FindImplementationsToolTests|FindReferencesToolTests"
```

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: add find-implementations and find-references MCP tools"
```

---

## Task 6: `get-type-at` tool

Position-based query — the `var` resolver.

**Files:**
- Create: `src/Parlance.Mcp/Tools/GetTypeAtTool.cs`
- Create: `tests/Parlance.Mcp.Tests/Tools/GetTypeAtToolTests.cs`
- Modify: `src/Parlance.Mcp/Program.cs`

- [ ] **Step 1: Write failing test**

Test with a known file + line/column. The tool accepts 1-based line/column (editor convention) and converts to 0-based before calling `query.GetSymbolAtPositionAsync`. Test: pointing at a `var` declaration resolves the actual type. Test: pointing at a type name resolves it. Test: unknown file returns not-found.

- [ ] **Step 2: Implement `GetTypeAtTool`**

Parameters: `filePath` (string), `line` (int, 1-based), `column` (int, 1-based). Converts to 0-based before calling query service. Result includes: resolved type name, fully qualified name, kind, whether it was inferred (`var`), the original source text at that position.

For `var` detection: check if the syntax node at the position is a `VariableDeclarationSyntax` with `var` as the type — if so, flag `isInferred: true` and return the inferred type from the semantic model via `GetTypeInfo()`.

- [ ] **Step 3: Register tool, run tests, commit**

```bash
git add -A
git commit -m "feat: add get-type-at MCP tool"
```

---

## Task 7: `outline-file` tool

Returns the type/member skeleton of a file — no method bodies.

**Files:**
- Create: `src/Parlance.Mcp/Tools/OutlineFileTool.cs`
- Create: `tests/Parlance.Mcp.Tests/Tools/OutlineFileToolTests.cs`
- Modify: `src/Parlance.Mcp/Program.cs`

- [ ] **Step 1: Write failing test**

Test with `CSharpWorkspaceSession.cs` — verify it returns type names, method signatures, property signatures, no method bodies. Verify it handles files with multiple types.

- [ ] **Step 2: Implement `OutlineFileTool`**

Get the semantic model via `query.GetSemanticModelAsync(filePath)`. Walk the syntax tree using `SyntaxWalker` or LINQ over `DescendantNodes()`:
- Find all `TypeDeclarationSyntax` (class, struct, record, interface, enum)
- For each type, find all `MemberDeclarationSyntax` children
- Extract: name, kind, accessibility, signature (using semantic model's `GetDeclaredSymbol()`)
- Skip method bodies — only report signatures

Result structure: list of `OutlineType` records, each containing a list of `OutlineMember` records.

- [ ] **Step 3: Register tool, run tests, commit**

```bash
git add -A
git commit -m "feat: add outline-file MCP tool"
```

---

## Task 8: `get-symbol-docs` tool

Returns structured XML documentation for a symbol.

**Files:**
- Create: `src/Parlance.Mcp/Tools/GetSymbolDocsTool.cs`
- Create: `tests/Parlance.Mcp.Tests/Tools/GetSymbolDocsToolTests.cs`
- Modify: `src/Parlance.Mcp/Program.cs`

- [ ] **Step 1: Write failing test**

Test with a type that has XML docs (e.g., `IAnalysisEngine` or any type with `///` comments). Verify structured output: summary, params, returns, remarks. Test graceful fallback when no docs exist.

- [ ] **Step 2: Implement `GetSymbolDocsTool`**

Resolve symbol via `query.FindSymbolsAsync`. Call `symbol.GetDocumentationCommentXml()`. Parse the XML into structured fields: `summary`, `params` (list of name/description), `returns`, `remarks`, `example`. Strip XML tags, return plain text with structure.

Handle `<inheritdoc/>` by walking to the base symbol and getting its docs. Handle `<see cref="..."/>` by resolving the reference name.

- [ ] **Step 3: Register tool, run tests, commit**

```bash
git add -A
git commit -m "feat: add get-symbol-docs MCP tool"
```

---

## Task 9: `call-hierarchy` tool

Returns callers (incoming) and callees (outgoing) one level deep.

**Files:**
- Create: `src/Parlance.Mcp/Tools/CallHierarchyTool.cs`
- Create: `tests/Parlance.Mcp.Tests/Tools/CallHierarchyToolTests.cs`
- Modify: `src/Parlance.Mcp/Program.cs`

- [ ] **Step 1: Write failing test**

Test with a method that's called from multiple places. Verify callers list with file/line. Verify callees list (what this method calls).

- [ ] **Step 2: Implement `CallHierarchyTool`**

**Callers (incoming):** Use `query.FindReferencesAsync(methodSymbol)`, then filter to call sites by checking if the reference's parent syntax node is an `InvocationExpressionSyntax`. Extract the containing method for each caller.

**Callees (outgoing):** Get the method's syntax body, walk all `InvocationExpressionSyntax` nodes, resolve each via the semantic model to get the called method symbol.

Result: two lists — `callers` and `callees`, each with method name, containing type, file, line.

- [ ] **Step 3: Register tool, run tests, commit**

```bash
git add -A
git commit -m "feat: add call-hierarchy MCP tool"
```

---

## Task 10: `get-type-dependencies` and `safe-to-delete` tools

**Files:**
- Create: `src/Parlance.Mcp/Tools/GetTypeDependenciesTool.cs`
- Create: `src/Parlance.Mcp/Tools/SafeToDeleteTool.cs`
- Create: `tests/Parlance.Mcp.Tests/Tools/GetTypeDependenciesToolTests.cs`
- Create: `tests/Parlance.Mcp.Tests/Tools/SafeToDeleteToolTests.cs`
- Modify: `src/Parlance.Mcp/Program.cs`

- [ ] **Step 1: Write failing tests**

`GetTypeDependenciesToolTests` — test with `CSharpWorkspaceSession`: verify it returns dependencies (base types, field types, parameter types) and dependents (types that reference it). Verify scoped to solution types.

`SafeToDeleteToolTests` — test with a type that IS referenced (should return `safe: false` with reference count). Test with a private method/type that isn't referenced (should return `safe: true`). Note: finding a truly unreferenced symbol in Parlance.sln may require creating a test fixture or checking carefully.

- [ ] **Step 2: Implement `GetTypeDependenciesTool`**

Resolve the type. For **dependencies**: walk the type's members and extract referenced types from base types, interfaces, field types, property types, method parameter/return types. For **dependents**: use `query.FindReferencesAsync` to find types that reference this one, then extract the containing type for each reference. Group by relationship kind (inherits, implements, field, parameter, etc.).

Filter to solution-defined types only — don't enumerate framework types.

- [ ] **Step 3: Implement `SafeToDeleteTool`**

Resolve the symbol. Call `query.FindReferencesAsync`. Count total references. Return: `safe` (bool), `referenceCount` (int), `sampleLocations` (first 5 references with file/line). Works for types, methods, properties, fields.

- [ ] **Step 4: Register both tools, run tests, commit**

```bash
git add -A
git commit -m "feat: add get-type-dependencies and safe-to-delete MCP tools"
```

---

## Task 11: `decompile-type` tool

External type decompilation via ICSharpCode.Decompiler.

**Files:**
- Modify: `src/Parlance.Mcp/Parlance.Mcp.csproj` (add NuGet package)
- Create: `src/Parlance.Mcp/Tools/DecompileTypeTool.cs`
- Create: `tests/Parlance.Mcp.Tests/Tools/DecompileTypeToolTests.cs`
- Modify: `src/Parlance.Mcp/Program.cs`

- [ ] **Step 1: Add ICSharpCode.Decompiler package**

```bash
dotnet add src/Parlance.Mcp/Parlance.Mcp.csproj package ICSharpCode.Decompiler
```

- [ ] **Step 2: Write failing test**

Test decompiling `Microsoft.CodeAnalysis.Project` (an external Roslyn type). Verify the result is valid C# source text. Test with a nonexistent type — should return not-found gracefully.

- [ ] **Step 3: Implement `DecompileTypeTool`**

Strategy: iterate compilations via `query.GetCompilationsAsync`. For each compilation, check `MetadataReferences` for the assembly containing the target type. Once found, get the assembly file path from the `PortableExecutableReference`. Use `ICSharpCode.Decompiler.CSharpDecompiler` to decompile the type from that assembly.

```csharp
var decompiler = new CSharpDecompiler(assemblyPath, new DecompilerSettings());
var fullTypeName = new FullTypeName(targetTypeFullName);
var decompiledCode = decompiler.DecompileTypeAsString(fullTypeName);
```

Result: status, type name, decompiled C# source, assembly name, assembly path.

- [ ] **Step 4: Register tool, run tests, commit**

```bash
git add -A
git commit -m "feat: add decompile-type MCP tool"
```

---

## Task 12: E2E integration tests and final wiring

Verify all tools work end-to-end through the MCP server.

**Files:**
- Modify: `tests/Parlance.Mcp.Tests/Integration/McpServerIntegrationTests.cs`

- [ ] **Step 1: Add integration tests for key tools**

Add tests to `McpServerIntegrationTests` following the existing pattern (`CreateClientAsync` → `CallToolAsync`):

```csharp
[Fact]
public async Task DescribeType_ReturnsTypeInfo()
{
    await using var client = await CreateClientAsync(SolutionPath);
    var result = await client.CallToolAsync("describe-type",
        new Dictionary<string, object?> { ["typeName"] = "CSharpWorkspaceSession" });

    Assert.True(result.IsError is not true);
    var textBlock = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
    using var doc = JsonDocument.Parse(textBlock.Text!);
    Assert.Equal("found", doc.RootElement.GetProperty("status").GetString());
}

[Fact]
public async Task FindImplementations_ReturnsResults()
{
    await using var client = await CreateClientAsync(SolutionPath);
    var result = await client.CallToolAsync("find-implementations",
        new Dictionary<string, object?> { ["typeName"] = "IAnalysisEngine" });

    Assert.True(result.IsError is not true);
    var textBlock = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
    using var doc = JsonDocument.Parse(textBlock.Text!);
    Assert.Equal("found", doc.RootElement.GetProperty("status").GetString());
}
```

Add similar tests for `find-references`, `outline-file`, `safe-to-delete` — at minimum one happy-path test per tool.

- [ ] **Step 2: Verify all tools appear in tool listing**

```csharp
[Fact]
public async Task ListTools_ReturnsAllSemanticTools()
{
    await using var client = await CreateClientAsync(SolutionPath);
    var tools = await client.ListToolsAsync();
    var toolNames = tools.Select(t => t.Name).ToHashSet();

    Assert.Contains("workspace-status", toolNames);
    Assert.Contains("describe-type", toolNames);
    Assert.Contains("find-implementations", toolNames);
    Assert.Contains("find-references", toolNames);
    Assert.Contains("get-type-at", toolNames);
    Assert.Contains("outline-file", toolNames);
    Assert.Contains("get-symbol-docs", toolNames);
    Assert.Contains("call-hierarchy", toolNames);
    Assert.Contains("get-type-dependencies", toolNames);
    Assert.Contains("safe-to-delete", toolNames);
    Assert.Contains("decompile-type", toolNames);
}
```

- [ ] **Step 3: Run full test suite**

```bash
dotnet test Parlance.sln
dotnet format Parlance.sln --verify-no-changes
```

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat: add E2E integration tests for all semantic navigation tools"
```

---

## Notes for implementers

- **Code style:** Modern C#, file-scoped namespaces, `var` everywhere, seal by default, primary constructors, `ImmutableList<T>`, collection expressions `[..]`. Multiple parameters on the same line — don't one-per-line everything.
- **Formatting:** Run `dotnet format Parlance.sln --verify-no-changes` before committing. CI enforces this.
- **Test approach:** Unit tests call static tool methods directly (no MCP framework). Integration tests spawn the server as a subprocess via `StdioClientTransport`. Use `TestPaths.FindSolutionPath()` to locate `Parlance.sln`.
- **Error states:** Every tool must check `holder.LoadFailure`, then `!holder.IsLoaded`, before calling the query service. See the three-state check pattern in the reference section above.
- **LLM output shaping (issue 15):** Not a separate tool. After all tools are implemented, dogfood them and iterate on output formats. Keep results compact — no tool response should overwhelm a context window.
- **Build note:** `dotnet build Parlance.sln` fails because of the pack-only `Parlance.CSharp.Package` project. Build individual projects: `dotnet build src/Parlance.Mcp/Parlance.Mcp.csproj`.
