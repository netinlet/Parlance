# Parlance.CSharp.Workspace — API Design Spec

**Milestone:** 1 — Workspace Loads a Real Project
**Date:** 2026-03-15 (revised)

## Summary

New project `Parlance.CSharp.Workspace` (net10.0) — an explicitly C#/Roslyn workspace engine wrapping `MSBuildWorkspace`. No cross-language interface in Abstractions. The host/engine boundary will emerge in Milestone 2 when the MCP server is built. The engine is honest about being C#-specific, with clean internal boundaries so shared abstractions can be extracted later from real code.

## Design Philosophy

**Normalize outputs, not internals.** Mature multi-language analysis systems (SonarQube, CodeQL, Semgrep) unify the host, workflow, and result model. They keep compilation, caching, and program semantics inside each language engine.

- **Abstractions** owns the normalized result model (`Diagnostic`, `Location`, `AnalysisResult`) — already language-neutral.
- **Parlance.CSharp.Workspace** owns everything C#/Roslyn-specific: MSBuild loading, compilation, semantic model, caching, project identity.
- **The host/engine boundary** will be designed in Milestone 2 when we build the MCP server — the actual host. Designing the plugin interface before building the host or the plugin leads to speculative abstractions that leak implementation concepts.

The honest split:
- **Shared (future):** session lifecycle, snapshot identity, health, normalized diagnostics, capability signaling
- **Language-specific (now):** workspace loading, build/program creation, semantic model access, invalidation, caching, fix application mechanics

## Key Decisions

1. **No cross-language interface yet** — `CSharpWorkspaceSession` is a concrete sealed class in `Parlance.CSharp.Workspace`. No `IWorkspace` in Abstractions. The MCP server (Milestone 2) will reveal the right abstraction boundary.

2. **Static factory with options record** — `CSharpWorkspaceSession.OpenSolutionAsync(path, options?)`. Bundles lifecycle configuration into `WorkspaceOpenOptions`. Owns `MSBuildLocator.RegisterDefaults()` internally. Callers never touch MSBuild/Roslyn directly.

3. **Internal compilation cache, swappable** — Callers express lifecycle intent via `WorkspaceMode` in options (`Report` or `Server`). File watching defaults by mode (`true` for Server, `false` for Report) but can be overridden. The engine maps mode to an internal `IProjectCompilationCache` strategy. The cache is swappable internally for performance tuning (eager recompilation, memory-constrained, etc.) without changing the public API. Roslyn concerns stay inside the engine.

4. **No stale reads relative to known state** — The compilation cache guarantees that a read never returns data older than what the engine knows about. With file watching enabled, the engine learns about external changes automatically. Without file watching, the engine only knows about changes reported via `RefreshAsync()`. The guarantee is: reads are always consistent with the engine's current snapshot — not that the engine is omniscient. `SnapshotVersion` on the session lets callers detect staleness. Per-project dirty tracking with dependency-aware cascade — changing Project B only invalidates Project A if A depends on B. Synchronization details designed in the compilation cache issue.

5. **Location backward compat** — `FilePath` added as optional last parameter to existing `Location` record in Abstractions. No compile-time breaking change. Record equality semantics change subtly but acceptably (no existing code uses `FilePath`).

6. **Workspace types are C#-specific** — All workspace model types live in `Parlance.CSharp.Workspace` with C#-honest names and fields. When the host/engine boundary is designed in Milestone 2, the truly shared subset will be extracted into Abstractions.

7. **No public stubs** — Methods are added when implemented. No `NotImplementedException` traps.

8. **Names that don't collide with Roslyn** — `WorkspaceProjectKey` (not `ProjectId`), `CSharpProjectInfo` (not `ProjectInfo`), `CSharpWorkspaceSession` (not `Workspace`).

## Project Structure

### New project: `src/Parlance.CSharp.Workspace/Parlance.CSharp.Workspace.csproj`

- **TFM:** net10.0
- **Dependencies:**
  - `Parlance.Abstractions` (project reference — for `Diagnostic`, `Location`, `AnalysisResult`)
  - `Microsoft.Build.Locator`
  - `Microsoft.CodeAnalysis.Workspaces.MSBuild`
  - `Microsoft.CodeAnalysis.CSharp.Workspaces`
  - `Microsoft.Extensions.Logging.Abstractions`

### New test project: `tests/Parlance.CSharp.Workspace.Tests/Parlance.CSharp.Workspace.Tests.csproj`

- **TFM:** net10.0
- xUnit, matching existing test projects

### Dependency graph update

```
Parlance.Abstractions              (Diagnostic, Location, AnalysisResult — unchanged)
    ↑
Parlance.CSharp.Workspace          (NEW — CSharpWorkspaceSession, C#/Roslyn engine)
    ↑
Parlance.CSharp                    (evolves later to use workspace engine)
    ↑
Parlance.Analyzers.Upstream
    ↑
Parlance.Mcp                      (NEW — Milestone 2, host/engine boundary designed here)
Parlance.Cli
```

## Abstractions Layer Changes

Only one change — adding `FilePath` to `Location`:

```csharp
public sealed record Location(
    int Line,
    int Column,
    int EndLine,
    int EndColumn,
    string? FilePath = null);
```

Everything else in this spec lives in `Parlance.CSharp.Workspace`.

## Workspace Types (in Parlance.CSharp.Workspace)

### `WorkspaceMode`

```csharp
public enum WorkspaceMode
{
    Report,  // Compile once, no invalidation. One-shot CLI/CI.
    Server   // File watching, per-project invalidation. Long-running MCP server.
}
```

### `WorkspaceOpenOptions`

```csharp
public sealed record WorkspaceOpenOptions(
    WorkspaceMode Mode = WorkspaceMode.Report,
    bool? EnableFileWatching = null,
    ILoggerFactory? LoggerFactory = null)
{
    public bool FileWatchingEnabled => Mode switch
    {
        WorkspaceMode.Report => EnableFileWatching == true
            ? throw new ArgumentException(
                "File watching is not supported in Report mode")
            : false,
        WorkspaceMode.Server => EnableFileWatching ?? true,
        _ => false
    };
}
```

Bundles lifecycle configuration. `EnableFileWatching` defaults by mode when `null`: `true` for `Server`, `false` for `Report`. Report mode with `EnableFileWatching = true` throws — file watching is nonsensical in a one-shot session. Server mode with `EnableFileWatching = false` is valid for testing/debugging, but callers must use `RefreshAsync()` to update the session manually.

### `WorkspaceProjectKey`

```csharp
public readonly record struct WorkspaceProjectKey(Guid Value);
```

Strongly typed wrapper around Roslyn's `ProjectId.Id` GUID. Named to avoid collision with Roslyn's `ProjectId`. Simple — no factory, no validation. `default(WorkspaceProjectKey)` produces `Guid.Empty`, which is harmless (no project will match it). Validation belongs at API boundaries, not in the identity type.

### `WorkspaceLoadStatus`

```csharp
public enum WorkspaceLoadStatus
{
    Loaded,    // All projects loaded successfully
    Degraded,  // Some projects loaded, some failed — usable but incomplete
    Failed     // No projects loaded
}
```

Session-level status only. `Degraded` is the honest middle ground — a solution where 5 of 6 projects loaded successfully. Callers can still work with the loaded projects. Individual projects use `ProjectLoadStatus` (binary `Loaded`/`Failed`).

### `ProjectLoadStatus`

```csharp
public enum ProjectLoadStatus { Loaded, Failed }
```

Binary at the project level — a single project either loaded or it didn't. `Degraded` only applies at the session level (some projects loaded, some failed).

### `CSharpProjectInfo`

```csharp
public sealed record CSharpProjectInfo(
    WorkspaceProjectKey Key,
    string Name,
    string ProjectPath,
    IReadOnlyList<string> TargetFrameworks,
    string? ActiveTargetFramework,
    string? LangVersion,
    ProjectLoadStatus Status,
    IReadOnlyList<WorkspaceDiagnostic> Diagnostics);
```

Project metadata and health in one type — no separate `ProjectHealth` to correlate by ID. C#-specific fields live here honestly. `Diagnostics` captures MSBuild load warnings/errors for this project.

`TargetFrameworks` lists all TFMs defined by the project (e.g. `["net8.0", "net10.0"]`). Empty for projects that failed before MSBuild could evaluate them. `ActiveTargetFramework` is the one MSBuild evaluated — MSBuildWorkspace loads one TFM per project evaluation. `null` for failed projects. For single-targeted projects that loaded successfully, `TargetFrameworks` contains one entry matching `ActiveTargetFramework`. `LangVersion` corresponds to the active TFM; `null` for failed projects.

### `WorkspaceDiagnostic`

```csharp
public sealed record WorkspaceDiagnostic(
    string Code,
    string Message,
    WorkspaceDiagnosticSeverity Severity);

public enum WorkspaceDiagnosticSeverity { Error, Warning, Info }
```

Structured diagnostic for workspace-level issues (MSBuild load failures, missing references, etc.). Distinct from the analysis `Diagnostic` in Abstractions — these are about the workspace itself, not code quality findings.

### `CSharpWorkspaceHealth`

```csharp
public sealed record CSharpWorkspaceHealth(
    WorkspaceLoadStatus Status,
    IReadOnlyList<CSharpProjectInfo> Projects);
```

Top-level `Status` is derived from project statuses: `Loaded` if all projects have `ProjectLoadStatus.Loaded`, `Degraded` if some loaded and some failed, `Failed` if none loaded.

## CSharpWorkspaceSession

Sealed class. The public API for this issue is loading, health, and snapshot identity only.

### Static factories

```csharp
public static Task<CSharpWorkspaceSession> OpenSolutionAsync(
    string solutionPath,
    WorkspaceOpenOptions? options = null,
    CancellationToken ct = default);

public static Task<CSharpWorkspaceSession> OpenProjectAsync(
    string projectPath,
    WorkspaceOpenOptions? options = null,
    CancellationToken ct = default);
```

- Options default to `new WorkspaceOpenOptions()` (report mode, no file watching, no logging) when `null`
- Calls `MSBuildLocator.RegisterDefaults()` guarded by `MSBuildLocator.IsRegistered`
- Creates `MSBuildWorkspace.Create()`
- Opens via `MSBuildWorkspace.OpenSolutionAsync()` or `OpenProjectAsync()`
- Subscribes to `WorkspaceFailed` for diagnostic capture
- Populates `Projects` and `Health` from loaded solution/project
- Maps `WorkspaceMode` to internal `IProjectCompilationCache` implementation

### Public API (this issue scope)

```csharp
public sealed class CSharpWorkspaceSession : IAsyncDisposable
{
    // Identity
    public string WorkspacePath { get; }

    // Freshness
    public long SnapshotVersion { get; }

    // Post-load state
    public CSharpWorkspaceHealth Health { get; }
    public IReadOnlyList<CSharpProjectInfo> Projects { get; }

    // Lookups
    public CSharpProjectInfo? GetProject(WorkspaceProjectKey key);
    public CSharpProjectInfo? GetProjectByPath(string projectPath);

    // Freshness — manual refresh for sessions without file watching
    public Task RefreshAsync(CancellationToken ct = default);

    // Lifecycle
    public ValueTask DisposeAsync();
}
```

`WorkspacePath` — returns the path passed to whichever factory was called (`.sln` or `.csproj`).

`SnapshotVersion` — monotonically increasing counter. Increments on any workspace change (file edit, project reload). MCP host can track this to detect staleness and reject stale actions. Starts at 1 after initial load.

`GetProjectByPath` instead of `GetProjectByName` — paths are unambiguous, names can collide in large solutions.

`RefreshAsync` — Server mode only (throws `InvalidOperationException` in Report mode). Scans loaded documents for on-disk changes, reads updated file contents, applies new `SourceText` to the Roslyn workspace via `Solution.WithDocumentText()`, then marks affected projects dirty in the compilation cache. Both the workspace's source and the cache's compiled artifacts are updated — marking caches dirty alone is not enough, because recompilation against stale `SourceText` produces stale results. Primary use case: server mode with file watching disabled (testing, debugging). With file watching enabled, this is typically unnecessary but safe to call. Increments `SnapshotVersion` if changes are detected.

No stubs, no `NotImplementedException` methods. Future capabilities (compilation, semantic navigation, diagnostics) are added as public methods in their respective issues.

### Disposal

- Disposes inner `MSBuildWorkspace`
- Stops file watching if active
- Clears compilation caches

### Error handling

- Individual project load failures captured in `CSharpProjectInfo` with `Status = ProjectLoadStatus.Failed` and structured `WorkspaceDiagnostic` messages
- Solution-level failure (file not found, MSBuild locator failure) throws `WorkspaceLoadException` wrapping the underlying cause
- `WorkspaceFailed` events from MSBuildWorkspace logged and surfaced in project diagnostics

### `WorkspaceLoadException`

```csharp
public sealed class WorkspaceLoadException(
    string message,
    string workspacePath,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string WorkspacePath { get; } = workspacePath;
}
```

Distinguishes workspace load failures from other exceptions. Wraps the underlying MSBuild/IO/locator exception.

## Internal Compilation Cache

### `IProjectCompilationCache`

```csharp
internal interface IProjectCompilationCache
{
    Task<ProjectCompilationState> GetAsync(Project project, CancellationToken ct = default);
    void MarkDirty(Microsoft.CodeAnalysis.ProjectId projectId);
    void MarkAllDirty();
}
```

Internal to `Parlance.CSharp.Workspace`. Uses Roslyn types freely. Public callers choose behavior through `WorkspaceOpenOptions` and `WorkspaceMode`, not through cache injection.

`ProjectCompilationState` is an internal type holding the `Compilation`, its snapshot version, and dirty state.

### Server cache (internal, sealed)

- `ConcurrentDictionary<Microsoft.CodeAnalysis.ProjectId, ProjectCompilationState>` — keyed by Roslyn's own `ProjectId` internally
- Per-project dirty tracking with dependency-aware cascade via Roslyn's `ProjectDependencyGraph`
- Reads validate per-project freshness before serving
- Increments `SnapshotVersion` on the session when any project is marked dirty
- **Hard guarantee:** a read never returns without checking currency
- Thread-safe — concurrent queries to different projects don't block each other
- Synchronization details (concurrent reader coalescing, debounce window) designed when this cache is implemented

### Report cache (internal, sealed)

- Same interface, simpler behavior
- `MarkDirty` / `MarkAllDirty` are no-ops
- First `GetAsync` compiles and caches; subsequent calls return cached
- One-shot: load, compile what's needed, serve, dispose
- `RefreshAsync` is not supported in Report mode — the session throws `InvalidOperationException`. Report mode is immutable by contract.

## Testing (this issue scope)

- **Build verification** — Project builds, MSBuild dependencies resolved; `dotnet test Parlance.sln` continues to pass
- **Model tests** — Record equality, `CSharpWorkspaceHealth`/`CSharpProjectInfo`/`WorkspaceDiagnostic` construction
- **Location tests** — Existing call sites work with new optional `FilePath`; verify existing 120+ tests still pass

Cache tests (per-project dirtiness, dependency cascade) belong in the compilation cache issue. Integration tests (loading `Parlance.sln`) come in the loading issue.

## Acceptance Criteria

- [ ] Project builds with MSBuild dependencies resolved
- [ ] Core type compiles with the designed API surface
- [ ] API design self-evident from the type signatures
- [ ] Existing tests continue to pass (Location change verified)

## Future: Host/Engine Boundary (Milestone 2)

When the MCP server is built, it will need to hold a workspace session and dispatch tool calls. That's when the shared interface emerges. The migration path:

1. Keep the C# engine intact
2. Introduce a host-level `IWorkspaceSession` in Abstractions
3. Wrap `CSharpWorkspaceSession` in a C# adapter
4. Future languages implement their own adapters
5. Move only truly shared concepts (session lifecycle, snapshot identity, capabilities, normalized diagnostics) into the shared layer

The shared interface will likely cover:
- Session identity, snapshot version, health (normalized)
- Capability queries (`SupportsSemanticNavigation`, `SupportsIncrementalRefresh`, `SupportsFixes`, `RequiresBuildIntegration`)
- Normalized analysis results (already in Abstractions)

Language-specific concerns (compilation, semantic model, caching, MSBuild) stay inside the engine.

## Guardrails for Later

To keep the path to multi-language open:
- Do not add `IWorkspace` to Abstractions yet
- Use names that don't collide with Roslyn core types
- Keep roadmap members out of the public API until implemented
- Keep `Location.FilePath` in shared abstractions — it's useful across languages

## Roadmap Reference

`docs/plans/2026-03-14-ide-for-ai-roadmap.md` — Milestone 1
`docs/plans/2026-03-15-parlance-workspace-reframe-design.md` — design framing document
