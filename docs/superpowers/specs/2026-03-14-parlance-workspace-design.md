# Parlance.Workspace — API Design Spec

**Issue:** #33 — Create Parlance.Workspace project and design API
**Milestone:** 1 — Workspace Loads a Real Project
**Date:** 2026-03-15 (revised)

## Summary

New project `Parlance.Workspace` (net10.0) wrapping `MSBuildWorkspace`. Explicitly C#-first — no cross-language interface in Abstractions yet. The host/engine abstraction boundary will emerge naturally in Milestone 2 when the MCP server (the host) is built. For now, `ParlanceWorkspace` is a concrete C# workspace engine with clean internal boundaries so that shared abstractions can be extracted later from real code, not speculation.

## Design Philosophy

**Normalize outputs, not internals.** Mature multi-language analysis systems (SonarQube, CodeQL, Semgrep) unify the host, workflow, and result model. They keep compilation, caching, and program semantics inside each language engine. Parlance follows this principle:

- **Abstractions** owns the normalized result model (`Diagnostic`, `Location`, `AnalysisResult`) — these are already language-neutral and stay unchanged.
- **Parlance.Workspace** owns everything C#/Roslyn-specific: MSBuild loading, compilation, semantic model, caching strategy, project identity.
- **The host/engine boundary** (an `IWorkspaceEngine` or similar interface) will be designed in Milestone 2 when we build the MCP server — the actual host. Designing the plugin interface before building the host or the plugin leads to speculative abstractions that leak implementation concepts.

This means a future TypeScript workspace would be a separate project (e.g. `Parlance.TypeScript`) implementing whatever shared interface emerges from Milestone 2, with its own `ts.Program` internals, its own caching model, its own invalidation semantics.

## Key Decisions

1. **No cross-language interface yet** — `ParlanceWorkspace` is a concrete sealed class in `Parlance.Workspace`. No `IWorkspace` in Abstractions. The MCP server (Milestone 2) will reveal the right abstraction boundary between host and engine. Designing it now would produce a C# interface pretending to be language-neutral.

2. **Static factory, no DI** — `ParlanceWorkspace.OpenSolutionAsync(path, mode, logger?)`. Owns `MSBuildLocator.RegisterDefaults()` call internally (guarded by `IsRegistered`). Callers never touch MSBuild/Roslyn directly.

3. **Public compilation Strategy pattern** — `ICompilationStrategy` is public in `Parlance.Workspace` (not Abstractions — it uses Roslyn types, and the workspace is explicitly C#-specific). Factory takes `ICompilationStrategy?`, defaulting to `ReportCompilationStrategy`. Well-known strategies available via `CompilationStrategies.Report` and `CompilationStrategies.Server`. Custom strategies just implement the interface — swappable for eager recompilation, memory-constrained, profiling, etc.

4. **No stale reads** — Hard guarantee. A read never returns without first checking if it's current. The `ServerCompilationStrategy` uses a generation counter with barrier: file changes increment generation, reads check cached generation vs. current before returning. Synchronization details (concurrent reader coalescing, cascading invalidation across project dependencies) will be designed in issue #37 when real compilation is implemented. The guarantee is a design constraint; the implementation is deferred.

5. **Location backward compat** — `FilePath` added as optional last parameter to existing `Location` record in Abstractions. No compile-time breaking change to existing call sites. Record equality semantics change subtly (a `Location` with `FilePath = null` and one with `FilePath = "foo.cs"` are not equal), but this is acceptable since no existing code constructs `Location` with a `FilePath`.

6. **Workspace types are workspace types** — `ProjectId`, `ProjectInfo`, `ProjectHealth`, `WorkspaceHealth` live in `Parlance.Workspace`, not Abstractions. They use C#-specific concepts (TargetFramework, LangVersion) honestly. When the host/engine boundary is designed in Milestone 2, the truly shared subset will be extracted into Abstractions — informed by what the MCP server actually needs.

7. **No public stubs** — Methods are added to `ParlanceWorkspace` when they are implemented, not before. No `NotImplementedException` traps. The public API surface reflects actual capability.

## Project Structure

### New project: `src/Parlance.Workspace/Parlance.Workspace.csproj`

- **TFM:** net10.0
- **Dependencies:**
  - `Parlance.Abstractions` (project reference — for `Diagnostic`, `Location`, `AnalysisResult`)
  - `Microsoft.Build.Locator`
  - `Microsoft.CodeAnalysis.Workspaces.MSBuild`
  - `Microsoft.CodeAnalysis.CSharp.Workspaces`
  - `Microsoft.Extensions.Logging.Abstractions`

### New test project: `tests/Parlance.Workspace.Tests/Parlance.Workspace.Tests.csproj`

- **TFM:** net10.0
- xUnit, matching existing test projects

### Dependency graph update

```
Parlance.Abstractions          (Diagnostic, Location, AnalysisResult — unchanged)
    ↑
Parlance.Workspace             (NEW — ParlanceWorkspace, C#/Roslyn engine)
    ↑
Parlance.CSharp                (evolves later to use Workspace)
    ↑
Parlance.Analyzers.Upstream
    ↑
Parlance.Mcp                   (NEW — Milestone 2, host/engine boundary designed here)
Parlance.Cli
```

## Abstractions Layer Changes

Only one change to Abstractions — adding `FilePath` to `Location`:

```csharp
public sealed record Location(
    int Line,
    int Column,
    int EndLine,
    int EndColumn,
    string? FilePath = null);
```

Everything else in this spec lives in `Parlance.Workspace`.

## Workspace Types (in Parlance.Workspace)

### `WorkspaceProjectId`

```csharp
public readonly record struct WorkspaceProjectId
{
    public Guid Value { get; }

    private WorkspaceProjectId(Guid value) => Value = value;

    public static WorkspaceProjectId Create(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("Project ID cannot be empty", nameof(value))
            : new(value);

    public override string ToString() => Value.ToString();
}
```

Strongly typed wrapper around Roslyn's `ProjectId.Id` GUID. Factory-only construction with validation — `default(WorkspaceProjectId)` produces `Guid.Empty` (unavoidable with value types), but `Create()` rejects it. API boundaries should validate. Named `WorkspaceProjectId` to avoid collision with Roslyn's `ProjectId`. Future entity IDs (e.g. `WorkspaceDocumentId`) follow the same one-off pattern.

### `ProjectStatus`

```csharp
public enum ProjectStatus { Loaded, Failed }
```

No `Loading` state — the factory returns after loading completes. Callers only observe post-load state.

### `ProjectInfo`

```csharp
public sealed record ProjectInfo(
    WorkspaceProjectId Id,
    string Name,
    string FilePath,
    ProjectStatus Status,
    string? TargetFramework,
    string? LangVersion);
```

C#-specific fields (`TargetFramework`, `LangVersion`) live here honestly. When the host/engine boundary is designed in Milestone 2, the MCP server will determine which fields it needs in a shared model vs. which are language-specific detail.

### `ProjectHealth`

```csharp
public sealed record ProjectHealth(
    WorkspaceProjectId ProjectId,
    string ProjectName,
    ProjectStatus Status,
    IReadOnlyList<WorkspaceDiagnostic> Diagnostics);
```

### `WorkspaceDiagnostic`

```csharp
public sealed record WorkspaceDiagnostic(
    string Code,
    string Message,
    WorkspaceDiagnosticSeverity Severity);

public enum WorkspaceDiagnosticSeverity { Error, Warning, Info }
```

Structured diagnostic for workspace-level issues (MSBuild load failures, missing references, etc.). Distinct from the analysis `Diagnostic` in Abstractions — these are about the workspace itself, not code quality findings.

### `WorkspaceHealth`

```csharp
public sealed record WorkspaceHealth(
    IReadOnlyList<ProjectHealth> Projects);
```

No `IsLoadComplete` — the factory returns after loading, so health always represents the post-load state. Check individual `ProjectHealth.Status` for per-project success/failure.

## ParlanceWorkspace

Sealed class. The public API for issue #33 is loading and health only.

### Static factories

```csharp
public static Task<ParlanceWorkspace> OpenSolutionAsync(
    string solutionPath,
    ICompilationStrategy? strategy = null,
    ILogger<ParlanceWorkspace>? logger = null,
    CancellationToken ct = default);

public static Task<ParlanceWorkspace> OpenProjectAsync(
    string projectPath,
    ICompilationStrategy? strategy = null,
    ILogger<ParlanceWorkspace>? logger = null,
    CancellationToken ct = default);
```

- `strategy` defaults to `CompilationStrategies.Report` when `null`
- Calls `MSBuildLocator.RegisterDefaults()` guarded by `MSBuildLocator.IsRegistered`
- Creates `MSBuildWorkspace.Create()`
- Opens via `MSBuildWorkspace.OpenSolutionAsync()` or `OpenProjectAsync()`
- Subscribes to `WorkspaceFailed` for diagnostic capture
- Populates `Projects` and `Health` from loaded solution/project

### Public API (issue #33 scope)

```csharp
public sealed class ParlanceWorkspace : IAsyncDisposable
{
    // What was loaded
    public string WorkspacePath { get; }

    // Post-load state
    public WorkspaceHealth Health { get; }
    public IReadOnlyList<ProjectInfo> Projects { get; }

    // Lookups
    public ProjectInfo? GetProject(WorkspaceProjectId id);
    public ProjectInfo? GetProjectByName(string name);

    // Lifecycle
    public ValueTask DisposeAsync();
}
```

`WorkspacePath` — not `SolutionPath`. Returns the path that was passed to whichever factory was called (`.sln` or `.csproj`).

No stubs, no `NotImplementedException` methods. Future capabilities (compilation, semantic navigation, diagnostics, file watching) are added as public methods in their respective issues (#37, #43-#46, #50, #36).

### Disposal

- Disposes inner `MSBuildWorkspace`
- Stops file watching if active (issue #36)
- Clears compilation caches via strategy

### Error handling

- Individual project load failures captured in `ProjectHealth` with `Status = Failed` and structured `WorkspaceDiagnostic` messages
- Solution-level failure (file not found, MSBuild locator failure, etc.) throws. Consider a `WorkspaceLoadException` wrapping the underlying cause so callers can distinguish workspace failures from other exceptions.
- `WorkspaceFailed` events from MSBuildWorkspace logged and surfaced in health

## Compilation Strategy

### `ICompilationStrategy`

```csharp
public interface ICompilationStrategy
{
    Task<Compilation> GetCompilationAsync(Project project, CancellationToken ct = default);
    void Invalidate(WorkspaceProjectId projectId);
    void InvalidateAll();
}
```

Public in `Parlance.Workspace`. Uses Roslyn's base `Compilation` type (not `CSharpCompilation`) — costs nothing and doesn't artificially exclude VB.NET. Custom strategies implement this interface for alternative caching/invalidation behavior.

### `CompilationStrategies` (static accessor)

```csharp
public static class CompilationStrategies
{
    public static ICompilationStrategy Report { get; } = new ReportCompilationStrategy();
    public static ICompilationStrategy Server { get; } = new ServerCompilationStrategy();
}
```

Well-known strategies. Discoverable entry point for callers.

### `ServerCompilationStrategy` (sealed)

- `ConcurrentDictionary<WorkspaceProjectId, CachedCompilation>` cache (keyed by typed ID)
- Global generation counter (interlocked increment on `Invalidate`)
- `GetCompilationAsync` checks cached generation vs. current generation
  - Current: return cached
  - Stale: recompile, update cache with current generation, return
- **Hard guarantee:** a read never returns without checking currency
- Synchronization details (concurrent reader coalescing, cascading invalidation across project dependencies, debounce window behavior) will be designed in issue #37

### `ReportCompilationStrategy` (sealed)

- Same dictionary structure
- `Invalidate` / `InvalidateAll` are no-ops
- First `GetCompilationAsync` compiles and caches; subsequent calls return cached
- One-shot: load, compile what's needed, serve, dispose

## Testing (Issue #33 scope)

- **Build verification** — Project builds, MSBuild dependencies resolved
- **Model tests** — Record equality, `WorkspaceHealth`/`ProjectHealth`/`WorkspaceDiagnostic` construction, `WorkspaceProjectId.Create()` validation (rejects `Guid.Empty`)
- **Location tests** — Existing call sites work with new optional `FilePath`; verify existing 120+ tests still pass

Strategy tests (generation barrier, compile-once) belong in issue #37. Integration tests (loading `Parlance.sln`) come in issue #34.

## Acceptance Criteria (from issue #33)

- [ ] Project builds with MSBuild dependencies resolved
- [ ] Core type compiles with the designed API surface
- [ ] API design self-evident from the type signatures
- [ ] Existing tests continue to pass (Location change verified)

## Future: Host/Engine Boundary (Milestone 2)

When the MCP server is built, it will need to hold a workspace reference and dispatch tool calls. That's when the shared interface emerges — designed from what the host actually needs, not from what the engine happens to expose. The interface will likely cover:

- Workspace identity and health (normalized)
- Capability queries (what can this engine do?)
- Normalized analysis results (already in Abstractions)

Language-specific concerns (compilation, semantic model, caching, MSBuild) stay inside the engine. This follows the pattern of SonarQube (host/plugin), CodeQL (per-language extractors), and Semgrep (common UX, per-language backends).

## Roadmap Reference

`docs/plans/2026-03-14-ide-for-ai-roadmap.md` — Milestone 1, GitHub issue #33
