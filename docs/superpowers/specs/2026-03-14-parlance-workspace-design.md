# Parlance.Workspace — API Design Spec

**Issue:** #33 — Create Parlance.Workspace project and design API
**Milestone:** 1 — Workspace Loads a Real Project
**Date:** 2026-03-14

## Summary

New project `Parlance.Workspace` (net10.0) wrapping `MSBuildWorkspace`. Defines `IWorkspace` interface in Abstractions, `ParlanceWorkspace` implementation with full-vision API stubs, and a Strategy pattern for compilation caching/invalidation. This issue is scaffolding and API design — loading implementation is issue #34.

## Key Decisions

1. **Interface in Abstractions** — `IWorkspace` lives in `Parlance.Abstractions` so a future `TypeScriptWorkspace` (or other language) can implement the same contract. MCP server and CLI code against the interface. Roslyn types stay internal to `Parlance.Workspace`.

2. **Static factory, no DI** — `ParlanceWorkspace.OpenSolutionAsync(path, strategy?, logger?)`. Owns `MSBuildLocator.RegisterDefaults()` call internally (guarded by `IsRegistered`). Callers never touch MSBuild/Roslyn directly.

3. **Compilation Strategy pattern** — `ICompilationStrategy` selected at creation time. Two implementations:
   - `ServerCompilationStrategy` — debounce + generation barrier for long-running MCP server mode
   - `ReportCompilationStrategy` — compile once, no invalidation, for one-shot CLI/CI mode
   - Swappable at creation, not at runtime

4. **No stale reads** — Hard guarantee. A read never returns without first checking if it's current. Generation counter with barrier: file changes increment generation, reads check cached generation vs. current before returning. If stale, recompile before serving.

5. **Full-vision stubs** — Internal API designed for the complete roadmap (navigation, diagnostics). External surface starts with loading and health. Stubbed methods throw `NotImplementedException`, grouped by concern.

6. **Location backward compat** — `FilePath` added as optional last parameter to existing `Location` record. No breaking change to existing call sites.

7. **Flat records** — All new model types are positional records in Abstractions, matching existing style.

## Project Structure

### New project: `src/Parlance.Workspace/Parlance.Workspace.csproj`

- **TFM:** net10.0
- **Dependencies:**
  - `Parlance.Abstractions` (project reference)
  - `Microsoft.Build.Locator`
  - `Microsoft.CodeAnalysis.Workspaces.MSBuild`
  - `Microsoft.CodeAnalysis.CSharp`
  - `Microsoft.Extensions.Logging.Abstractions`

### New test project: `tests/Parlance.Workspace.Tests/Parlance.Workspace.Tests.csproj`

- **TFM:** net10.0
- xUnit, matching existing test projects

### Dependency graph update

```
Parlance.Abstractions          (IWorkspace, models — unchanged TFM)
    ↑
Parlance.Workspace             (NEW — ParlanceWorkspace, strategies)
    ↑
Parlance.CSharp                (evolves later to use Workspace)
    ↑
Parlance.Analyzers.Upstream
    ↑
Parlance.Mcp                   (NEW — future)
Parlance.Cli
```

## Abstractions Layer Changes

### Modified: `Location`

Add optional `FilePath` as last parameter — no breaking change:

```csharp
public sealed record Location(
    int Line,
    int Column,
    int EndLine,
    int EndColumn,
    string? FilePath = null);
```

### New: `ProjectStatus`

```csharp
public enum ProjectStatus { Loading, Loaded, Failed }
```

### New: `ProjectInfo`

```csharp
public sealed record ProjectInfo(
    string Id,
    string Name,
    string FilePath,
    string? TargetFramework,
    string? LangVersion);
```

### New: `ProjectHealth`

```csharp
public sealed record ProjectHealth(
    string ProjectId,
    string ProjectName,
    ProjectStatus Status,
    IReadOnlyList<string> Diagnostics);
```

### New: `WorkspaceHealth`

```csharp
public sealed record WorkspaceHealth(
    IReadOnlyList<ProjectHealth> Projects,
    bool IsFullyLoaded);
```

### New: `IWorkspace`

```csharp
public interface IWorkspace : IAsyncDisposable
{
    // --- Loading (Issue #33/#34) ---
    string SolutionPath { get; }
    WorkspaceHealth Health { get; }

    // --- Project queries ---
    IReadOnlyList<ProjectInfo> Projects { get; }
    ProjectInfo? GetProject(string projectIdOrName);

    // --- Compilation (Issue #37) ---
    Task<object> GetCompilationAsync(string projectId, CancellationToken ct = default);
    Task<object> GetSemanticModelAsync(string documentPath, CancellationToken ct = default);

    // --- Semantic Navigation (Issues #43-#46) ---
    Task<object> DescribeTypeAsync(string typeName, CancellationToken ct = default);
    Task<object> FindImplementationsAsync(string typeName, CancellationToken ct = default);
    Task<object> FindReferencesAsync(string symbolName, CancellationToken ct = default);
    Task<object> GetTypeAtLocationAsync(string filePath, int line, int column, CancellationToken ct = default);

    // --- Diagnostics (Issues #48-#50) ---
    Task<AnalysisResult> AnalyzeProjectAsync(string projectId, AnalysisOptions? options = null, CancellationToken ct = default);
    Task<AnalysisResult> AnalyzeDocumentAsync(string documentPath, AnalysisOptions? options = null, CancellationToken ct = default);

    // --- File Watching (Issue #36) ---
    event Action<string>? FileChanged;
}
```

## Workspace Implementation

### `ParlanceWorkspace`

Sealed class implementing `IWorkspace`.

**Static factory:**

```csharp
public static Task<ParlanceWorkspace> OpenSolutionAsync(
    string solutionPath,
    ICompilationStrategy? strategy = null,
    ILogger<ParlanceWorkspace>? logger = null,
    CancellationToken ct = default);
```

- Calls `MSBuildLocator.RegisterDefaults()` guarded by `MSBuildLocator.IsRegistered`
- Creates `MSBuildWorkspace.Create()`
- Opens the solution via `MSBuildWorkspace.OpenSolutionAsync()`
- Subscribes to `WorkspaceFailed` for diagnostic capture
- Populates `Projects` and `Health` from loaded solution
- Defaults to `ServerCompilationStrategy` if no strategy provided

**Disposal:**

- `IAsyncDisposable` — disposes inner `MSBuildWorkspace`
- Stops file watching if active
- Clears compilation caches via strategy

**Implemented in issue #33:**

- `SolutionPath` — returns path passed to factory
- `Health` — built from solution load results
- `Projects` — populated from solution's project graph
- `GetProject(string)` — lookup by ID or name

**Stubbed (throw `NotImplementedException`):**

- `GetCompilationAsync` / `GetSemanticModelAsync` — issue #37
- `DescribeTypeAsync` / `FindImplementationsAsync` / `FindReferencesAsync` / `GetTypeAtLocationAsync` — Milestone 3
- `AnalyzeProjectAsync` / `AnalyzeDocumentAsync` — Milestone 4
- `FileChanged` event — issue #36

**Error handling:**

- Individual project load failures captured in `ProjectHealth` with `Status = Failed`
- Solution-level failure throws (can't operate without a solution)
- `WorkspaceFailed` events logged and surfaced in health

### `ICompilationStrategy` (internal)

```csharp
internal interface ICompilationStrategy
{
    Task<CSharpCompilation> GetCompilationAsync(Project project, CancellationToken ct = default);
    void Invalidate(string projectId);
    void InvalidateAll();
}
```

Uses Roslyn types — internal to `Parlance.Workspace`, never exposed to callers.

### `ServerCompilationStrategy` (internal, sealed)

- `ConcurrentDictionary<string, CachedCompilation>` cache
- Global generation counter (interlocked increment on `Invalidate`)
- `GetCompilationAsync` checks cached generation vs. current generation
  - Current: return cached
  - Stale: recompile, update cache with current generation, return
- **Hard guarantee:** a read never returns without checking currency
- Thread-safe — concurrent queries to different projects don't block each other

### `ReportCompilationStrategy` (internal, sealed)

- Same dictionary structure
- `Invalidate` / `InvalidateAll` are no-ops
- First `GetCompilationAsync` compiles and caches; subsequent calls return cached
- Designed for one-shot: load, compile what's needed, serve, dispose

## Testing (Issue #33 scope)

- **Model tests** — Record equality, `WorkspaceHealth`/`ProjectHealth` construction
- **Location tests** — Existing call sites work with new optional `FilePath`
- **Strategy tests** — Generation barrier logic (ServerCompilationStrategy), compile-once (ReportCompilationStrategy). May use mock projects or be deferred to issue #34.
- **Build verification** — Project builds, MSBuild dependencies resolved

Integration tests (loading `Parlance.sln`) come in issue #34.

## Acceptance Criteria (from issue #33)

- [ ] Project builds with MSBuild dependencies resolved
- [ ] Core type compiles with the designed API surface (methods stubbed/throwing)
- [ ] API design documented or self-evident from the type signatures

## Roadmap Reference

`docs/plans/2026-03-14-ide-for-ai-roadmap.md` — Milestone 1, Issue #1
