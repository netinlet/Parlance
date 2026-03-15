# IDE for AI: Roadmap

## Vision

Parlance is the **IDE for the AI agent**. Developer tooling is a side-effect.

The differentiator is not raw Roslyn (6+ MCP servers already do that). It's **opinionated judgment on top of Roslyn** — curation, scoring, fix safety classification, idiomatic direction. SonarQube is a judge. Raw Roslyn MCP servers are compilers-via-API. Parlance is an opinionated senior developer via MCP.

The primary consumer is the AI agent (Claude). The primary feedback loop is dogfooding: Claude uses Parlance's MCP tools while building Parlance. What works and what's missing drives what gets built next.

## Architectural North Star

**The engine holds the full Roslyn workspace internally from the start; tools are just views into it.**

```
ParlanceWorkspace (MSBuildWorkspace, Compilation, SemanticModel)
    ↑
MCP Server (primary interface, long-running, stdio + HTTP/SSE)
    ├── Semantic Navigation Tools (describe-type, find-implementations, ...)
    ├── Diagnostic Tools (analyze, fix)
    └── Workspace Tools (workspace-status, rules)
    ↑
CLI (thin client, report mode, one-shot)
```

The engine wraps `MSBuildWorkspace` and owns the full `Workspace` → `Solution` → `Project` → `Compilation` → `SemanticModel` graph. Callers never touch Roslyn directly. The internal API is designed for the full vision (semantic queries, navigation, impact analysis) even though the external surface starts small.

## Two Modes

- **Server mode** (Milestones 2-4): Long-running process, hot workspace, MCP interface. Immediate feedback while coding. File watching keeps the workspace current.
- **Report mode** (Milestone 5): One-shot — load, analyze, output, exit. For CI, quality gates, SonarQube-style reports. The CLI `analyze` command becomes this.

Same engine, same analyzers, different lifecycle.

## Dependency Graph

```
Milestone 1 (Workspace Engine)
  └── Milestone 2 (MCP Server)
        ├── Milestone 3 (Semantic Navigation) ← priority: helps build everything after
        └── Milestone 4 (Diagnostics over MCP)
  Milestone 5 (CLI Pivot) ← last, uses same engine
```

Milestones 3 and 4 are parallel branches off the MCP server. Semantic navigation is prioritized because those tools help Claude build the diagnostic layer more effectively. Milestone 5 depends on Milestone 1 (workspace engine) and Milestone 4 (diagnostic model) but is last priority.

## Curation Sets

Curation sets are named, opinionated selections of rules from any source (NetAnalyzers, Roslynator, PARL, future packages) with:

- Which rules are in/out
- Severity overrides
- Rationale and fix classification

**Default**: "What the project already says." Respect `AnalysisLevel`, `.editorconfig`, `NoWarn`. Zero Parlance opinions unless a named set is applied.

**Named sets** layer on top of the project default. Example: `ai-agent` (tuned for validating AI-generated code), `modern-csharp`, `security`, `minimal`.

Curation set definitions are a product design exercise as much as engineering. They evolve continuously through dogfooding and have no "done" state. They are tracked via a pinned tracking issue and `curation` label, not a milestone.

PARL analyzers are trimmed to 1 stub rule as part of Milestone 4 (issue #16). Until then, the existing 8 rules remain but are not a development focus. They are loaded identically to any 3rd party analyzer package — no special code path. Value comes from curating 3rd party analyzers.

## Key Decisions

- **MSBuildWorkspace from day one.** This is a pivot, not incremental. The synthetic compilation approach is replaced.
- **MCP server is the primary interface.** CLI becomes a thin client.
- **Semantic navigation before diagnostics.** Build the tools that make Claude better at coding, then use them to build the diagnostic layer.
- **File watching is core, not optional.** Stale compilations produce wrong diagnostics.
- **Lazy project compilation.** Load the project graph eagerly, compile on demand. Design for large solutions.
- **Pluggable transport.** stdio (Claude Code) + HTTP/SSE (future clients). Day one.
- **Logging day one.** `Microsoft.Extensions.Logging`, structured, throughout.
- **Curation sets are code-defined.** `.editorconfig` is an output, not an input. (Note: earlier design docs used the term "profiles" — "curation sets" supersedes that terminology.)

## Risks and Technical Uncertainty

**MSBuildWorkspace is the highest-risk work in this roadmap.** It is notoriously difficult to get working correctly: MSBuild locator issues, SDK resolution, cross-platform behavior, NuGet restore interactions, and handling partially broken projects. Milestone 1 should be approached expecting friction, not treating it as routine plumbing. Known pitfalls include MSBuild version conflicts, `Microsoft.Build.Locator` needing to run before any MSBuild types are loaded, and workspace diagnostic events that must be monitored for silent failures.

**Existing tests (120+) rely on the current architecture.** The synthetic compilation engine isn't deleted immediately — it's replaced milestone by milestone. Tests migrate as their corresponding engine components are replaced. No big-bang cutover.

## Project Structure

`Parlance.Workspace` is a new project that sits below `Parlance.CSharp` in the dependency graph:

```
Parlance.Abstractions          (interfaces, models — unchanged)
    ↑
Parlance.Workspace             (NEW — MSBuildWorkspace, semantic model, file watching)
    ↑
Parlance.CSharp                (analysis engine — evolves to use Workspace instead of CompilationFactory)
    ↑
Parlance.Analyzers.Upstream    (analyzer loading — evolves to be the only loading path)
    ↑
Parlance.Mcp                   (NEW — MCP server, tools)
Parlance.Cli                   (thin client — refactored in Milestone 5)
```

## Not in Scope (Future Work)

These capabilities from the research document are intentionally deferred. They may become issues later based on dogfooding:

- `type-check` — Verify an expression resolves correctly without a full build
- `check-impact` — "If I change this signature, what breaks?"
- `validate-pattern` — "Is this async/dispose/nullable pattern correct?"
- `refactor-preview` / `refactor-apply` — Preview and apply refactorings with impact assessment
- `generate-boilerplate` — Compiler-correct IDisposable, equality, etc.
- `trace-data-flow` — How does a value flow through the code?
- `find-similar` — Semantic similarity search across the codebase

## GitHub Project Setup

- **One Kanban board** with columns: `Backlog` → `Up Next` → `In Progress` → `Done`
- **Milestones** for major deliverables (each groups related issues)
- **Labels** for area tagging: `engine`, `mcp`, `tools`, `curation`, `cli`
- **Curation sets**: Pinned tracking issue + `curation` label (no milestone — ongoing work). Path to separate board if it outgrows the main one.

---

## Milestone 1: Workspace Loads a Real Project

Foundation. Replaces synthetic `CompilationFactory` with real `MSBuildWorkspace`.

### Issues

#### 1. Create `Parlance.Workspace` project and design API

New project: `Parlance.Workspace` (net10.0). Add `Microsoft.Build.Locator`, `Microsoft.CodeAnalysis.Workspaces.MSBuild`. Design the core `ParlanceWorkspace` type that wraps `MSBuildWorkspace`. Internal API surface designed for the full vision (diagnostics, semantic model, symbols) — external surface starts with loading and health only. This issue is project scaffolding and API design; loading implementation is issue #2.

**Labels:** `engine`

**Acceptance:**
- Project builds with MSBuild dependencies resolved
- Core type compiles with the designed API surface (methods stubbed/throwing)
- API design documented or self-evident from the type signatures

#### 2. Implement solution/project loading

Load `.sln` via `OpenSolutionAsync()`, `.csproj` via `OpenProjectAsync()`. Resolve NuGet references, project references, target frameworks. Error boundaries: one project failing doesn't block others.

**Labels:** `engine`

**Acceptance:**
- Loads `Parlance.sln` successfully
- Reports all projects and their status
- A broken project reference doesn't crash the loader
- Integration test confirms loading

#### 3. Workspace health reporting

Report what loaded, what failed, per-project status, language version, TFM, package references. Degraded-state awareness (partial load is OK, total failure is reported).

**Labels:** `engine`

**Acceptance:**
- Health report for `Parlance.sln` shows all projects, TFMs, language versions
- Failed projects reported with reason, not swallowed

#### 4. File watching and incremental updates

`FileSystemWatcher` on loaded project directories. On file change, update workspace via `WithDocumentText()` — not full reload. Debounce rapid changes. Invalidate cached compilations.

**Labels:** `engine`

**Acceptance:**
- Modify a `.cs` file, workspace reflects the change without full reload
- Rapid saves don't cause thrashing

#### 5. Lazy project compilation

Load project graph structure eagerly (what projects exist, their dependencies). Compile on demand when a query targets a specific project. Cache compilations, invalidate on file change.

**Labels:** `engine`

**Acceptance:**
- Loading a solution does not compile all projects upfront
- First query against a project triggers compilation
- Subsequent queries use cached compilation
- File change invalidates the right cache entries

#### 6. Structured logging for workspace operations

`Microsoft.Extensions.Logging` throughout workspace engine. Log: load start/complete, project load, file change, compilation, errors. Configurable sinks.

**Labels:** `engine`

**Acceptance:**
- Loading a solution produces structured log output
- Log levels are appropriate (info for lifecycle, debug for details, error for failures)

---

## Milestone 2: First MCP Tool Working

MCP server starts, loads workspace, Claude can query it.

### Issues

#### 7. Create `Parlance.Mcp` project

New project with `ModelContextProtocol` SDK. Transport architecture is pluggable from the start. Implement stdio transport first (required for Claude Code). HTTP/SSE transport is deferred to a future issue but the design should not preclude it. Server lifecycle: startup, graceful shutdown, configuration (solution path, log level, transport).

**I/O and transport evaluation:** Before committing to the `ModelContextProtocol` SDK's built-in transport, evaluate the underlying I/O model. The server must perform well cross-platform (Linux/WSL2 + Windows). Considerations:
- What does the MCP SDK use under the hood? Is it `System.IO.Pipelines` or raw streams?
- For stdio: `PipeReader`/`PipeWriter` over `Console.OpenStandardInput/Output` is the performant path
- For HTTP/SSE (future): Kestrel abstracts over epoll (Linux) / IOCP (Windows) / kqueue (macOS) automatically
- Can we swap the I/O layer if the SDK's default isn't sufficient, or do we need to bring our own transport?

Document the evaluation findings before building on top.

**Labels:** `mcp`

**Acceptance:**
- Project builds
- I/O model evaluated and documented (SDK capabilities, cross-platform behavior, performance characteristics)
- Server starts and accepts connections via stdio
- Graceful shutdown on SIGTERM/SIGINT
- Transport abstraction allows adding HTTP/SSE later without restructuring

#### 8. Wire workspace engine into MCP server

MCP server loads workspace on startup (configurable solution path). Holds engine reference for tool handlers. The engine's file watcher keeps the workspace current — the MCP layer queries the engine's current state on each tool call (no caching at the MCP level that could go stale).

**Labels:** `mcp`, `engine`

**Acceptance:**
- Server starts, loads configured solution
- Workspace health is available to tool handlers
- Load failure is reported, not crashed
- File changes are reflected in subsequent tool calls without server restart

#### 9. Implement `workspace-status` tool and end-to-end verification

First MCP tool. Returns: loaded projects, health, TFMs, language versions, project dependency graph. Shaped for LLM consumption — compact, structured, actionable. Includes end-to-end verification: configure as Claude Code MCP server, verify Claude can invoke the tool, document setup.

**Labels:** `mcp`, `tools`

**Acceptance:**
- Claude can call `workspace-status` and get a useful response
- Response fits comfortably in a context window
- Includes project count, health summary, any load failures
- Claude Code MCP server configuration documented in repo
- Claude invokes the tool successfully end-to-end

#### 10. Structured logging for MCP operations

Request/response logging, error logging, performance timing. Same logging infrastructure as the engine.

**Labels:** `mcp`

**Acceptance:**
- Each MCP tool call logged with timing
- Errors logged with context
- Log output doesn't interfere with stdio transport

---

## Milestone 3: Semantic Navigation Usable

Claude can query types, find implementations, trace references. Force multiplier for all subsequent work.

### Issues

#### 11. Implement `describe-type` tool

Given a type name, resolve via workspace semantic model. Return: members, base types, interfaces, attributes, accessibility. Filtered to what matters — not a raw 200-member dump. Handle ambiguity (multiple types with same name across projects).

**Labels:** `tools`

**Acceptance:**
- `describe-type IAnalysisEngine` returns members, implemented by, namespace
- `describe-type Diagnostic` returns record members, base type
- Ambiguous names return candidates with project context

#### 12. Implement `find-implementations` tool

Given interface or abstract type name, find all implementations across the solution. Return: implementing type, project, file location.

**Labels:** `tools`

**Acceptance:**
- `find-implementations IAnalysisEngine` returns `CSharpAnalysisEngine`
- Works for interfaces and abstract classes
- Results include file paths and project names

#### 13. Implement `find-references` tool

Given a symbol name, find all usages across the solution. Grouped by project/file. Handle overloads (let caller specify which overload or return all).

**Labels:** `tools`

**Acceptance:**
- `find-references CompilationFactory.Create` returns all call sites
- Results grouped by file with line numbers
- Handles methods, types, properties

#### 14. Implement `get-type-at` tool

Given a file path and line/column (or expression text), resolve the actual type. The `var` resolver. Also resolves generic type arguments, return types, parameter types.

**Labels:** `tools`

**Acceptance:**
- Points at `var engine = new CSharpAnalysisEngine(...)` → reports `CSharpAnalysisEngine`
- Resolves generic types: `IReadOnlyList<Diagnostic>`
- Reports nullability state

#### 15. LLM output shaping for semantic tools

Ensure all semantic tool outputs are compact, structured, and actionable for an LLM context window. Iterate based on dogfooding. Consistent format across tools.

**Labels:** `tools`

**Acceptance:**
- Response format documented
- No tool response exceeds reasonable context budget for its query
- Format is consistent across all semantic tools

---

## Milestone 4: Diagnostics over MCP

Run analyzers against workspace compilations, return curated diagnostics.

### Issues

#### 16. Unify analyzer loading

All analyzers (PARL stub + NetAnalyzers + Roslynator + future packages) loaded through one path. No special-casing for PARL. Trim PARL rules to 1 stub to keep the project alive.

**Labels:** `engine`

**Acceptance:**
- Single loader loads all analyzer DLLs
- PARL has 1 stub rule
- Loader reports what it found

#### 17. Wire analyzers into workspace compilation

Run loaded analyzers against real workspace compilations via `WithAnalyzers()`. Map Roslyn diagnostics to Parlance output model.

**Labels:** `engine`

**Acceptance:**
- Analyzers run against workspace compilation (not synthetic)
- Diagnostics mapped to Parlance types with metadata
- Analyzer failure doesn't crash the engine

#### 18. Implement `analyze` tool

MCP tool: run diagnostics on a file, project, or solution scope. Returns curated diagnostics with fix classification (auto-fixable / needs-review / info-only).

**Labels:** `mcp`, `tools`

**Acceptance:**
- `analyze` on a file returns diagnostics from workspace compilation
- Each diagnostic includes: rule ID, severity, message, location, fix classification
- Scoping works: file, project, solution

#### 19. Diagnostic output model for LLMs

Design the diagnostic response format: structured, compact, actionable. Rationale, fix suggestions, severity, source rule. This is the shared contract for MCP and future CLI.

**Labels:** `tools`

**Acceptance:**
- Format documented
- Includes enough context to act on (file, location, message, fix suggestion)
- Compact enough for batch results (20+ diagnostics don't blow up context)

---

## Milestone 5: CLI Pivot

CLI becomes a thin client over the workspace engine. Report mode for CI and quality gates.

### Issues

#### 20. Refactor CLI to use workspace engine

Replace synthetic compilation with workspace engine from Milestone 1. `analyze`, `fix`, `rules` commands all use the new engine.

**Labels:** `cli`, `engine`

**Acceptance:**
- `parlance analyze` uses MSBuildWorkspace, not CompilationFactory
- Output is equivalent or better than current
- Existing CLI tests pass (updated as needed)

#### 21. Report mode: one-shot analysis

Load workspace, run analysis, output, exit. Optimized for CI — no file watching, no hot workspace. Fast startup, clean exit with appropriate exit code.

**Labels:** `cli`

**Acceptance:**
- `parlance analyze --report` (or equivalent) runs one-shot
- Exit code reflects pass/fail threshold
- Works in CI without a long-running process

#### 22. Machine-readable output formats

JSON output (already exists, update for new model). Consider SARIF for CI tool integration. Human-readable text output updated.

**Labels:** `cli`

**Acceptance:**
- JSON output matches new diagnostic model
- Text output is clean and useful
- SARIF output parseable by standard CI tools (if implemented)

---

## Curation Sets (ongoing, no milestone)

Tracked via pinned tracking issue + `curation` label on individual issues.

**Tracking issue description:**

> Curation sets are named, opinionated selections of analyzer rules. This is ongoing product design work — no "done" state. Each curation task (add a rule, tune a severity, define a new set) is its own issue labeled `curation`.
>
> **Infrastructure:** Built as a curation issue (see "Curation set infrastructure" below), prerequisite for Milestone 4's `analyze` tool.
> **Default set:** "What the project says" — respect project configuration, zero Parlance opinions.
> **Named sets:** Layer on top of defaults. First set: `ai-agent`, informed by dogfooding.

### Initial curation issues (created as needed):

#### Curation set infrastructure
Define sets in code, apply/layer/override mechanism. A curation set specifies rule selections, severity overrides, and metadata. Sets layer on top of project defaults. **Labels:** `curation`, `engine`. **Acceptance:** A curation set can be defined in code and applied to filter/override analyzer output. Sets compose (project default + named set).

#### Default curation set
"What the project says." Respect `AnalysisLevel`, `.editorconfig`, `NoWarn` from the loaded workspace. Zero Parlance opinions. **Labels:** `curation`. **Acceptance:** With no named set applied, diagnostic output matches what `dotnet build` would produce.

#### `ai-agent` curation set v1
First named set, tuned for validating AI-generated code. Built from dogfooding observations — what does Claude actually get wrong? **Labels:** `curation`. **Acceptance:** Set defined with at least 10 curated rules. Rationale documented for each rule inclusion.

#### Curation set selection via MCP
`analyze` tool accepts optional curation set parameter. `rules` tool shows what's active in the current set. **Labels:** `curation`, `mcp`. **Acceptance:** Claude can pass a curation set name to `analyze` and get filtered results. Default behavior (no set specified) uses project defaults.

---

## What Gets Replaced

The pivot replaces several components of the current architecture:

| Current | Replaced by | When |
|---|---|---|
| `CompilationFactory` (synthetic ref-pack loading) | `ParlanceWorkspace` (MSBuildWorkspace) | Milestone 1 |
| `IAnalysisEngine.AnalyzeSourceAsync(string)` | Workspace-centric API | Milestone 1 |
| `WorkspaceAnalyzer` (multi-file synthetic compilation) | Workspace engine project queries | Milestone 1 |
| `CSharpAnalysisEngine` (file-centric) | Workspace-based analysis | Milestone 4 |
| PARL analyzers (8 rules) | 1 stub rule | Milestone 4 |
| CLI as primary interface | MCP server as primary, CLI as thin client | Milestone 5 |

## What Stays

- Upstream analyzer loader concept (NetAnalyzers, Roslynator) — evolved to be the only loading path
- `DiagnosticEnricher` — evolved for workspace-based diagnostics
- Scoring model — evolved after curation sets exist
- System.CommandLine 2.0.3 CLI structure — refactored, not replaced
- xUnit test infrastructure
- Real-world test repos and `test-repos.sh`
