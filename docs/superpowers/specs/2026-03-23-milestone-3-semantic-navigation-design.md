# Milestone 3: Semantic Navigation — Design Spec

## Context

Milestone 2 delivered a working MCP server with `workspace-status`. The `CSharpWorkspaceSession` holds a live Roslyn `Solution` with per-project compilation caching, file watching, and snapshot versioning. Milestone 3 builds semantic navigation tools on top of this infrastructure.

The roadmap defines 11 tools (issues 11-21). This spec covers the **shared query layer** that all tools consume, plus the tool integration pattern. Individual tool output formatting is per-tool — not spec'd here.

## Design Decisions

These were resolved during brainstorming:

1. **Shared query library in `Parlance.CSharp.Workspace`** — not per-tool Roslyn plumbing, not MCP-side helpers. A library both MCP tools and CLI (Milestone 5) consume.

2. **Returns Roslyn types** (`ISymbol`, `INamedTypeSymbol`, `Compilation`, `SemanticModel`). The Roslyn-free boundary is at the MCP tool output layer, not the library API. Tools get full flexibility to extract whatever they need.

3. **Ambiguity handling: solution-first ranking with optional qualification.** `FindSymbolsAsync("Diagnostic")` returns solution-defined types before external/framework types. Fully qualifying (`Parlance.Abstractions.Diagnostic`) resolves exactly. Tools present candidates when ambiguous.

4. **Standalone type, not just extension methods.** `WorkspaceQueryService` holds `WorkspaceSessionHolder` + `ILogger`. Logging is a day-one requirement and doesn't fit the extension method pattern. Extension methods are used for ergonomic conversions (e.g., `ISymbol.ToCandidate()`).

5. **DI-registered singleton.** Takes `WorkspaceSessionHolder` (not the session directly) because the session loads asynchronously. Tools check `holder.IsLoaded` before calling query methods — same pattern as `WorkspaceStatusTool`.

## `WorkspaceQueryService`

Lives in `Parlance.CSharp.Workspace`. Primary constructor with holder + logger.

### Public API

```csharp
namespace Parlance.CSharp.Workspace;

public sealed class WorkspaceQueryService(WorkspaceSessionHolder holder, ILogger<WorkspaceQueryService> logger)
{
    // ── Symbol resolution ──────────────────────────────────────

    /// Resolve a name to matching symbols across all projects.
    /// Solution-defined types rank before external/framework types.
    /// Accepts partial names ("Diagnostic") or fully qualified ("Parlance.Abstractions.Diagnostic").
    /// Returns each symbol paired with the project it was found in.
    public Task<ImmutableList<ResolvedSymbol>> FindSymbolsAsync(
        string name, SymbolFilter filter = SymbolFilter.All, bool ignoreCase = false,
        CancellationToken ct = default);

    // ── Compilation access ─────────────────────────────────────

    /// Get the compilation for a named project. Returns null if not found.
    public Task<Compilation?> GetCompilationAsync(string projectName, CancellationToken ct = default);

    /// Get the compilation for a Roslyn Project.
    public Task<Compilation> GetCompilationAsync(Project project, CancellationToken ct = default);

    /// Iterate all project compilations lazily. Compiles on demand, skips failures.
    public IAsyncEnumerable<(Project Project, Compilation Compilation)> GetCompilationsAsync(CancellationToken ct = default);

    // ── Semantic model ─────────────────────────────────────────

    /// Get the semantic model for a file path. Returns null if the file isn't in the workspace.
    public Task<SemanticModel?> GetSemanticModelAsync(string filePath, CancellationToken ct = default);

    // ── Cross-solution queries ─────────────────────────────────

    /// Find all references to a symbol across the solution.
    public Task<ImmutableList<ReferencedSymbol>> FindReferencesAsync(ISymbol symbol, CancellationToken ct = default);

    /// Find all implementations of an interface or abstract type.
    public Task<ImmutableList<ISymbol>> FindImplementationsAsync(ISymbol symbol, CancellationToken ct = default);

    /// Get the symbol at a specific file position (the `var` resolver).
    /// Line and column are 0-based (matching Roslyn's LinePosition).
    /// MCP tools convert from 1-based editor coordinates before calling.
    public Task<ISymbol?> GetSymbolAtPositionAsync(string filePath, int line, int column, CancellationToken ct = default);
}
```

### Internal dependency

One new internal method on `CSharpWorkspaceSession`:

```csharp
internal Task<ProjectCompilationState> GetCompilationStateAsync(Project project, CancellationToken ct = default) =>
    _cache.GetAsync(project, ct);
```

`CurrentSolution` remains `internal`. Same assembly — `WorkspaceQueryService` accesses it directly.

### Thread safety

`WorkspaceQueryService` is a DI singleton called concurrently by MCP tool handlers. It is implicitly thread-safe because it composes thread-safe building blocks: `WorkspaceSessionHolder.Session` is a volatile field, `Solution` is immutable, `ServerCompilationCache` uses `ConcurrentDictionary`, and `SymbolFinder` static methods are thread-safe. No explicit synchronization needed in the service itself.

### Error handling in `GetCompilationsAsync`

`GetCompilationsAsync` catches and logs per-project compilation failures rather than propagating them. The underlying `ServerCompilationCache.GetAsync` throws `InvalidOperationException` if compilation returns null — the async enumerable catches this, logs a warning, and yields the next project.

### Symbol resolution details

`FindSymbolsAsync` uses `SymbolFinder.FindDeclarationsAsync` per project, then:

1. Deduplicates by `ToDisplayString()`
2. Ranks solution-defined symbols first (assembly name matches a solution project)
3. External/framework symbols ranked second
4. Returns all matches — tools decide whether to auto-pick or present candidates

When `name` contains `.`, it's treated as a qualified name. When it doesn't, it matches against unqualified type/member names.

## Result types and extension methods

`FindSymbolsAsync` returns `ResolvedSymbol` — a pair of `ISymbol` and the `Project` it was found in. This avoids tools re-deriving project context from the symbol's assembly.

```csharp
namespace Parlance.CSharp.Workspace;

/// A symbol paired with the project it was resolved from.
public sealed record ResolvedSymbol(ISymbol Symbol, Project Project);

/// Lightweight view for presenting ambiguous candidates to callers.
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

public static class SymbolExtensions
{
    public static SymbolCandidate ToCandidate(this ResolvedSymbol resolved) =>
        SymbolCandidate.From(resolved);
}
```

## MCP tool integration pattern

### DI wiring (Program.cs)

```csharp
builder.Services.AddSingleton<WorkspaceQueryService>();
```

`WorkspaceQueryService` takes `WorkspaceSessionHolder` via DI — same singleton the tools already use.

### Tool pattern

Every semantic tool follows this shape:

```csharp
[McpServerToolType]
public sealed class DescribeTypeTool
{
    [McpServerTool(Name = "describe-type", ReadOnly = true)]
    [Description("Resolve a type by name. Returns members, base types, interfaces, accessibility.")]
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
            return DescribeTypeResult.Ambiguous(typeName,
                symbols.Select(s => s.ToCandidate()).ToImmutableList());

        var type = (INamedTypeSymbol)symbols[0].Symbol;
        // ... extract members, bases, interfaces, format into DescribeTypeResult
    }
}
```

Key points:
- Tools get both `holder` (for load-state checks) and `query` (for semantic operations) via DI
- `ToolDiagnostics.TimeToolCall` for timing — already exists
- Each tool defines its own result record shaped for LLM consumption
- The query service does the Roslyn work; the tool does the formatting

### Error states

Every tool handles these consistently (three-state check matching `WorkspaceStatusTool`):
- **Load failed** — `holder.LoadFailure` is set. Return failure message with diagnostic context.
- **Not yet loaded** — `!holder.IsLoaded`. Return loading status.
- **Not found** — symbol/file doesn't exist. Return clear message with what was searched.
- **Ambiguous** — multiple matches. Return candidates with project/namespace context.
- **Compilation failed** — project won't compile. Logged by query service, tool gets null/empty.

## Tool inventory

All 11 tools from the roadmap (issues 11-21) and what they use from `WorkspaceQueryService`:

| Tool | Query methods used |
|------|-------------------|
| `describe-type` | `FindSymbolsAsync` |
| `find-implementations` | `FindSymbolsAsync` → `FindImplementationsAsync` |
| `find-references` | `FindSymbolsAsync` → `FindReferencesAsync` |
| `get-type-at` | `GetSymbolAtPositionAsync` |
| `outline-file` | `GetSemanticModelAsync` (syntax tree walking, semantic model for type info) |
| `get-symbol-docs` | `FindSymbolsAsync` (XML docs from `ISymbol.GetDocumentationCommentXml()`) |
| `call-hierarchy` | `FindSymbolsAsync` → `FindReferencesAsync` + semantic model for callees |
| `get-type-dependencies` | `FindSymbolsAsync` + `GetCompilationsAsync` for dependents |
| `safe-to-delete` | `FindSymbolsAsync` → `FindReferencesAsync` (count + sample) |
| `decompile-type` | `GetCompilationAsync` (raw compilation → ICSharpCode.Decompiler) |
| LLM output shaping | Not a tool — cross-cutting formatting concern across all tools |

Every tool uses `FindSymbolsAsync` or `GetCompilationAsync` as its entry point. The query service covers the common ground; tools handle their specific Roslyn API calls (e.g., `decompile-type` passes the compilation to ICSharpCode.Decompiler directly).

## What this does NOT cover

- **Individual tool output formats** — each tool designs its own result records for LLM context. Issue 15 (LLM output shaping) addresses consistency across tools.
- **`decompile-type` dependency** — ICSharpCode.Decompiler is a new NuGet dependency. Added when that tool is implemented.
- **Pagination/filtering** — deferred until dogfooding reveals whether tools return too much data. Start simple, add if needed.
- **Caching resolved symbols** — the compilation cache handles compilation freshness. Symbol resolution is stateless per call. If profiling shows repeated resolution is expensive, add caching then.

## Implementation order

1. **`WorkspaceQueryService`** — the shared library, with tests
2. **`describe-type`** — first tool, validates the pattern end-to-end
3. **`find-implementations`** + **`find-references`** — exercise cross-solution queries
4. **`get-type-at`** + **`outline-file`** — exercise position-based and file-based queries
5. **`get-symbol-docs`** + **`call-hierarchy`** + **`get-type-dependencies`** + **`safe-to-delete`** — specialized tools building on the same primitives
6. **`decompile-type`** — external dependency, standalone
7. **LLM output shaping** — iterate on all tool outputs based on dogfooding
