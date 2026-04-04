# More Tools: Navigation, Type Hierarchy, and Code Actions — Design Spec

## Context

Parlance ships 12 MCP tools powered by a live Roslyn workspace. A [comparison against LSP 3.17](../../research/2026-04-03-parlance-vs-lsp-comparison.md) identified gaps where new tools would enrich the agent experience. This spec covers 6 new tools across 3 groups, bringing the total to 18.

The workspace engine (`CSharpWorkspaceSession`, `WorkspaceQueryService`) and tool pattern (`[McpServerToolType]`, DI injection, structured result records, ambiguity handling) are well-established from Milestones 1–4. All 6 tools follow existing conventions — no new abstractions or base classes.

## Design Decisions

Resolved during brainstorming:

1. **Approach: group by shared infrastructure** — 3 groups (navigation, type hierarchy, code actions) matching how the Roslyn APIs cluster. Each group extends the workspace/service layer with what it needs, then builds the tools. Groups are independently reviewable.

2. **goto-definition supports dual input** — either `symbolName` (name-based, consistent with Parlance) or `filePath`+`line`+`column` (position-based, natural for "I'm looking at this call site"). Both paths already exist in `WorkspaceQueryService`. Position takes precedence if both provided.

3. **search-symbols uses query + kind filter** — fuzzy/substring matching with optional symbol kind filter and `maxResults` cap. Distinct from `FindSymbolsAsync` which does exact/qualified matching.

4. **type-hierarchy has configurable depth** — `maxDepth` parameter defaults to 1 (callable without specifying it). Returns both directions (supertypes and subtypes). Subtypes capped at 50 per level to handle widely-implemented interfaces.

5. **Code actions split into 3 tools** — `get-code-fixes` (diagnostic-driven), `get-refactorings` (structural suggestions), `preview-code-action` (diff before applying). Each tool does one thing. Splitting avoids a monolithic tool and lets the agent pick what it needs.

6. **Session-scoped action identifiers** — code actions are cached in a `ConcurrentDictionary` keyed by generated IDs, valid for the current workspace snapshot. `preview-code-action` references these IDs. IDs expire when files change.

## Tool Inventory

### Group 1: Navigation

#### goto-definition

Jumps to where a symbol is defined. Handles partial classes (multiple locations) and metadata symbols.

**Input:**
- `symbolName` (string, optional) — name-based lookup with standard ambiguity handling
- `filePath` (string, optional) — position-based lookup
- `line` (int, optional) — required with `filePath`
- `column` (int, optional) — required with `filePath`

At least one of `symbolName` or `filePath`+`line`+`column` must be provided. If both, position takes precedence.

**Resolution:**
- Position path: `WorkspaceQueryService.GetSymbolAtPositionAsync` → resolved symbol
- Name path: `WorkspaceQueryService.FindSymbolsAsync` → standard ambiguity handling
- Extract locations from `ISymbol.Locations`, filtering to source locations (`Location.IsInSource`)
- For metadata symbols (no source location): return empty `Locations` list with `IsMetadata: true` and `AssemblyName` populated, so the agent knows it's external and can use `decompile-type` if needed

**Result:**
```
Status: "found" | "not_found" | "ambiguous" | "not_loaded" | "load_failed"
SymbolName: string
Kind: string
IsMetadata: bool
AssemblyName: string?
Locations: [{ FilePath, Line, Column, Snippet }]
Candidates: [SymbolCandidate]
```

**Snippet:** The source line at the definition location, trimmed. Gives the agent immediate context without a separate file read.

#### search-symbols

Fuzzy search for symbols by name across the workspace. Lets the agent discover symbols it doesn't know the exact name of.

**Input:**
- `query` (string, required) — substring to search for
- `kind` (string, optional) — filter by symbol kind: "class", "method", "property", "interface", "enum", "struct", "field", "event"
- `maxResults` (int, optional, default 25) — cap on returned results

**Resolution:**
- New method `WorkspaceQueryService.SearchSymbolsAsync(query, kindFilter, maxResults)`
- Uses `SymbolFinder.FindDeclarationsAsync` per project with substring matching enabled
- Filters by `SymbolKind` if `kind` is provided
- Deduplicates by fully qualified name across projects
- Returns up to `maxResults` results

**Result:**
```
Status: "found" | "no_matches" | "not_loaded" | "load_failed"
Query: string
Matches: [{ DisplayName, FullyQualifiedName, Kind, ProjectName, FilePath, Line }]
TotalMatches: int
```

`TotalMatches` is the count before the `maxResults` cap, so the agent knows whether to refine its query.

### Group 2: Type Hierarchy

#### type-hierarchy

Walks the inheritance tree in both directions from a given type. Primary new value over `describe-type` is the downward direction (subtypes) and configurable recursive depth.

**Input:**
- `typeName` (string, required) — must resolve to an `INamedTypeSymbol`
- `maxDepth` (int, optional, default 1) — how many levels deep to walk

**Resolution:**
- Standard name-based lookup via `FindSymbolsAsync`, must resolve to a type (not method, property, etc.)
- New method `WorkspaceQueryService.GetTypeHierarchyAsync(symbol, maxDepth)`

**Supertypes (up):**
- Walk `INamedTypeSymbol.BaseType` chain and direct interfaces at each level
- Recursive up to `maxDepth` levels
- Stop at `object` — include it but don't recurse past

**Subtypes (down):**
- Use `FindImplementationsAsync` at each level to find direct inheritors/implementors
- Recursive up to `maxDepth` levels
- Cap at 50 results per level; set `Truncated: true` if capped

**Result:**
```
Status: "found" | "not_found" | "ambiguous" | "not_loaded" | "load_failed"
TypeName: string
Kind: string (class, interface, struct, enum)
Supertypes: [HierarchyNode]
Subtypes: [HierarchyNode]
Truncated: bool
Candidates: [SymbolCandidate]
```

**HierarchyNode:**
```
Name: string
FullyQualifiedName: string
Kind: string
Relationship: string ("base_class" | "interface")
FilePath: string?
Line: int?
Children: [HierarchyNode]
```

Depth 1: nodes have empty `Children`. Depth 2+: `Children` populated recursively. Tree structure nests naturally.

### Group 3: Code Actions

These three tools share infrastructure via a new `CodeActionService`.

#### CodeActionService

A new singleton service registered alongside `AnalysisService`. Responsibilities:

**Analyzer and provider loading:**
- Code fix providers ship in the same assemblies as analyzers. Extend the existing `AnalyzerLoader` (or create a parallel `CodeFixLoader`) to extract `CodeFixProvider` and `CodeRefactoringProvider` types via reflection.
- Also load built-in Roslyn providers from `Microsoft.CodeAnalysis.CSharp.Features` — these supply standard refactorings (extract method, introduce variable, etc.).

**Code fix retrieval:**
- Given a document + line (+ optional diagnostic ID filter), get diagnostics at that location via `Compilation.WithAnalyzers`
- Run matching `CodeFixProvider.RegisterCodeFixesAsync` to collect available fixes
- Return structured list with generated action IDs

**Refactoring retrieval:**
- Given a document + text span, run `CodeRefactoringProvider.ComputeRefactoringsAsync`
- Return structured list with generated action IDs

**Action cache:**
- `ConcurrentDictionary<string, CachedCodeAction>` where `CachedCodeAction` holds the `CodeAction` and the workspace snapshot version it was generated against
- IDs are simple incrementing strings: `"fix-1"`, `"refactor-3"`
- Lookup checks snapshot version — if the workspace has changed, the action is expired

**Preview generation:**
- Given an action ID, look up the cached `CodeAction`
- Call `CodeAction.GetChangedSolutionAsync()` to get the modified solution
- Diff each changed document against the current solution to produce text edits

#### get-code-fixes

Returns available automatic fixes for diagnostics at a location. Natural next step after `analyze`.

**Input:**
- `filePath` (string, required)
- `line` (int, required)
- `diagnosticId` (string, optional) — filter to a specific diagnostic (e.g., "CS8600", "PARL0004")

**Resolution:**
- Find document by file path via `WorkspaceQueryService.GetSemanticModelAsync` (to validate the file exists), then get the `Document` from the solution
- Get diagnostics at/spanning the specified line
- If `diagnosticId` provided, filter to only that diagnostic
- Ask `CodeActionService` for fixes

**Result:**
```
Status: "found" | "no_fixes" | "not_found" | "not_loaded" | "load_failed"
FilePath: string
Line: int
Fixes: [{
    Id: string
    Title: string
    DiagnosticId: string
    DiagnosticMessage: string
    Scope: string ("document" | "project" | "solution")
}]
```

`Scope` indicates whether the fix supports `FixAllProvider` at document, project, or solution scope.

#### get-refactorings

Returns available refactoring actions at a location or range. Used when the agent is doing structural work.

**Input:**
- `filePath` (string, required)
- `line` (int, required)
- `column` (int, required)
- `endLine` (int, optional) — for range selection
- `endColumn` (int, optional) — for range selection

If `endLine`/`endColumn` are provided, the span covers that range. Otherwise the span is the token at the position.

**Resolution:**
- Find document by file path
- Convert line/column to text span
- Ask `CodeActionService` for refactorings in that span

**Result:**
```
Status: "found" | "no_refactorings" | "not_found" | "not_loaded" | "load_failed"
FilePath: string
Refactorings: [{
    Id: string
    Title: string
    Category: string?
}]
```

#### preview-code-action

Shows the exact changes a code fix or refactoring would make. Works with action IDs from either `get-code-fixes` or `get-refactorings`.

**Input:**
- `actionId` (string, required) — from a previous `get-code-fixes` or `get-refactorings` call

**Resolution:**
- Look up `CodeAction` from `CodeActionService` cache
- Check snapshot version — if expired, return `"expired"` status
- Call `CodeAction.GetChangedSolutionAsync()`
- Diff changed documents against current solution

**Result:**
```
Status: "found" | "expired" | "not_found" | "not_loaded" | "load_failed"
ActionId: string
Title: string
Changes: [{
    FilePath: string
    Edits: [{ StartLine, EndLine, OriginalText, NewText }]
}]
```

`"expired"` tells the agent to re-query fixes/refactorings — the workspace changed since the action was generated.

## Project Structure After This Work

```
Parlance.Abstractions              (diagnostic models — unchanged)
    ↑
Parlance.CSharp.Workspace          (compilations, session, file watching, query service)
Parlance.CSharp                    (DiagnosticEnricher, IdiomaticScoreCalculator)
Parlance.Analyzers.Upstream        (analyzer + code fix/refactoring provider loading)
    ↑
Parlance.Analysis                  (analysis orchestration, curation sets)
Parlance.CodeActions               (NEW — CodeActionService, action cache, preview)
    ↑
Parlance.Mcp                      (18 tools — thin handlers)
Parlance.Cli                      (commands — unchanged)
```

`Parlance.CodeActions` is a new project because:
- It has distinct dependencies (`Microsoft.CodeAnalysis.CSharp.Features` for built-in refactorings)
- Its lifecycle concern (action caching tied to snapshot versions) is separate from analysis orchestration
- It keeps `Parlance.Analysis` focused on diagnostics and curation

If the dependency on `Microsoft.CodeAnalysis.CSharp.Features` turns out to be unnecessary (all providers come from analyzer assemblies already loaded), `CodeActionService` can live in `Parlance.Analysis` instead and this project isn't needed. Decide during implementation.

## Workspace Layer Changes

### WorkspaceQueryService — New Methods

1. **`SearchSymbolsAsync(string query, SymbolKindFilter? kind, int maxResults)`** — fuzzy/substring symbol search across all projects. Distinct from `FindSymbolsAsync` (exact/qualified match).

2. **`GetTypeHierarchyAsync(INamedTypeSymbol symbol, int maxDepth)`** — recursive walk of supertypes and subtypes. Returns `TypeHierarchyResult` with nested `HierarchyNode` trees.

3. **`GetDocumentAsync(string filePath)`** — returns the `Document` for a file path. Currently tools go through `GetSemanticModelAsync` and don't have direct document access. The code action tools need the `Document` to pass to Roslyn's code fix/refactoring APIs.

### Existing Methods — No Changes

- `FindSymbolsAsync` — used by goto-definition (name path)
- `GetSymbolAtPositionAsync` — used by goto-definition (position path)
- `GetCompilationsAsync` — used by search-symbols (iterate all projects)

## Testing Strategy

Each group gets its own test class following the existing pattern in `tests/Parlance.Mcp.Tests/`.

**Group 1 tests:**
- goto-definition: resolve by name, resolve by position, partial class (multiple locations), metadata symbol (no source), ambiguous name, both inputs (position wins)
- search-symbols: substring match, kind filter, maxResults cap, no matches, TotalMatches exceeds cap

**Group 2 tests:**
- type-hierarchy: depth 1 (default), depth 2+, interface with multiple implementors, class inheritance chain, truncation at 50, non-type symbol error

**Group 3 tests:**
- get-code-fixes: fix available, no fixes, diagnostic ID filter, file not found
- get-refactorings: refactoring available at position, range span, no refactorings
- preview-code-action: valid action preview, expired action, invalid ID
- Cross-tool flow: analyze → get-code-fixes → preview-code-action

Test projects should include intentional code patterns that trigger known diagnostics and refactoring opportunities.

## Registration

All 6 tools registered in `Program.cs` via `.WithTools<T>()`:

```csharp
.WithTools<GotoDefinitionTool>()
.WithTools<SearchSymbolsTool>()
.WithTools<TypeHierarchyTool>()
.WithTools<GetCodeFixesTool>()
.WithTools<GetRefactoringsTool>()
.WithTools<PreviewCodeActionTool>()
```

`CodeActionService` registered as singleton in DI:
```csharp
builder.Services.AddSingleton<CodeActionService>();
```
