# Milestone 4: Diagnostics over MCP — Design Spec

## Context

Milestones 1–3 delivered a working MCP server with 11 semantic navigation tools powered by a live Roslyn workspace. `CSharpWorkspaceSession` holds compilations with per-project caching, file watching, and snapshot versioning. `WorkspaceQueryService` provides symbol resolution across projects. The MCP tool pattern (DI injection, load-state checks, structured result records) is well-established.

Milestone 4 adds the ability to run analyzers against workspace compilations and return curated diagnostics via MCP. This is the transition from "Claude can read the codebase" to "Claude can judge the codebase."

## Design Decisions

Resolved during brainstorming:

1. **New project: `Parlance.Analysis`** — orchestrates the diagnostic pipeline. Owns curation set infrastructure and analysis execution. Sits between the workspace engine and consumers (MCP, future CLI). Keeps `Parlance.CSharp.Workspace` focused on compilations, not analysis opinions.

2. **Curation sets are data-driven** — JSON today, database-queryable tomorrow. Not static C# classes, not builder APIs. The shape of the data stays the same regardless of source.

3. **Include-only model** — a curation set is an explicit selection of rules. The default behavior (no named set) is "what the project says" — respects `.editorconfig`, `NoWarn`, `AnalysisLevel`.

4. **Shared rationales via `rationaleId`** — rationales are reusable across rules. Update the message once, every rule referencing it picks it up.

5. **Fix classification is expandable** — string field, not closed enum. Start with `auto-fixable`, `needs-review`, `info-only`. Add more without breaking changes.

6. **File-scoped analysis only** — the `analyze` tool accepts file paths. No project or solution scope parameters. A project is just "all files in a labeled group." Claude decides which files to analyze (from its edits, `git diff`, etc.). We resolve files to projects internally.

7. **Unified analyzer loading** — all analyzers (PARL stub + NetAnalyzers + Roslynator) through `AnalyzerLoader`. No special-casing. PARL trimmed from 8 rules to 1 stub.

8. **Structured model internally, formatted per consumer** — the diagnostic model is shared between MCP and future CLI. MCP returns JSON records. CLI (Milestone 5) will format for terminals, CI, or SARIF. No format optimization at the model level.

9. **Default curation set ships in M4. `ai-agent` set deferred** — infrastructure + default set now. Named sets built empirically from dogfooding the `analyze` tool.

## Project Structure After M4

```
Parlance.Abstractions              (diagnostic models — unchanged)
    ↑
Parlance.CSharp.Workspace          (compilations, session, file watching)
Parlance.CSharp                    (DiagnosticEnricher, IdiomaticScoreCalculator)
Parlance.Analyzers.Upstream        (unified analyzer loading)
    ↑
Parlance.Analysis                  (NEW — orchestration, curation sets)
    ↑
Parlance.Mcp                      (tool handler — thin)
Parlance.Cli                      (commands — thin, unchanged until M5)
```

### `Parlance.Analysis` Dependencies

- `Parlance.Abstractions` — `Diagnostic`, `AnalysisResult`, `AnalysisSummary`, `Location`
- `Parlance.CSharp.Workspace` — `WorkspaceSessionHolder`, `WorkspaceQueryService`, `CSharpWorkspaceSession`
- `Parlance.CSharp` — `DiagnosticEnricher.ToParlanceDiagnostics()`, `IdiomaticScoreCalculator.Calculate()`
- `Parlance.Analyzers.Upstream` — `AnalyzerLoader.LoadAll()`

## Curation Sets

### Data Model

A curation set is a named, opinionated selection of analyzer rules with severity overrides, fix classification, and shared rationales.

**Rule entry:**

```json
{
  "ruleId": "CA1062",
  "severity": "warning",
  "fixClassification": "auto-fixable",
  "rationaleId": "null-safety"
}
```

- `ruleId` — upstream rule ID. The key. Stable in practice.
- `severity` — overrides the project default for this rule.
- `fixClassification` — `auto-fixable`, `needs-review`, `info-only` (expandable string, not enum).
- `rationaleId` — references a shared rationale entry.

**Rationale entry:**

```json
{
  "rationaleId": "null-safety",
  "message": "AI-generated code frequently omits null parameter guards"
}
```

Rationales are their own entity. Update the message once, every rule referencing it picks up the change. Clean for database-driven scenarios.

**Curation set:**

```json
{
  "name": "ai-agent",
  "description": "Tuned for validating AI-generated code",
  "rules": [
    { "ruleId": "CA1062", "severity": "warning", "fixClassification": "auto-fixable", "rationaleId": "null-safety" }
  ],
  "rationales": [
    { "rationaleId": "null-safety", "message": "AI-generated code frequently omits null parameter guards" }
  ]
}
```

### Filtering Model

Rule entries can target rules by:

- **Exact ID** — `"ruleId": "CA1062"`
- **Prefix pattern** — `"ruleId": "CA*"` (all Code Analysis rules)
- **Category** — `"category": "Design"` (all rules in that category)

The `CurationRule` record uses `RuleId` for exact and prefix matching. Category-based matching is a separate field:

```csharp
public sealed record CurationRule(
    string? RuleId,
    string? Category,
    string? Severity,
    string? FixClassification,
    string? RationaleId);
```

A rule entry matches if `RuleId` matches (exact or prefix with `*`) OR `Category` matches. At least one of `RuleId` or `Category` must be set.

This keeps the model flexible for database-driven scenarios where filtering by category or prefix is natural.

### Default Set

The default set is the absence of a named set. It means: run all loaded analyzers, respect the project's `.editorconfig`, `NoWarn`, and `AnalysisLevel`. Zero Parlance opinions. No JSON file — it's the baseline behavior.

### C# Representation

```csharp
namespace Parlance.Analysis.Curation;

public sealed record CurationSet(
    string Name,
    string Description,
    ImmutableList<CurationRule> Rules,
    ImmutableList<CurationRationale> Rationales);

public sealed record CurationRule(
    string? RuleId,
    string? Category,
    string? Severity,
    string? FixClassification,
    string? RationaleId);

public sealed record CurationRationale(
    string RationaleId,
    string Message);
```

### Loading

```csharp
namespace Parlance.Analysis.Curation;

public sealed class CurationSetProvider(ILogger<CurationSetProvider> logger)
{
    /// Load a named curation set. Returns null if not found.
    /// Loads from embedded JSON resources for v1. Future: database, API.
    public CurationSet? Load(string name);

    /// List available curation set names.
    public ImmutableList<string> Available();
}
```

Sets are loaded from embedded JSON resources in v1. The provider abstraction allows swapping to database/API later without changing consumers.

## Analysis Pipeline

### `AnalysisService`

The core orchestrator. Lives in `Parlance.Analysis`.

```csharp
namespace Parlance.Analysis;

public sealed class AnalysisService(
    WorkspaceSessionHolder holder,
    WorkspaceQueryService query,
    CurationSetProvider curationProvider,
    ILogger<AnalysisService> logger)
{
    /// Analyze the given files. Resolves files to projects, runs analyzers,
    /// applies curation, enriches diagnostics, and scores.
    public Task<FileAnalysisResult> AnalyzeFilesAsync(
        ImmutableList<string> filePaths,
        AnalyzeOptions? options = null,
        CancellationToken ct = default);
}

public sealed record AnalyzeOptions(
    string? CurationSetName = null,
    int? MaxDiagnostics = null);
```

### Pipeline Flow

```
AnalyzeFilesAsync(files, options)
    │
    ├─ Resolve file paths to projects via workspace Solution
    │   (each file belongs to one project — use Solution.GetDocumentIdsWithFilePath)
    │
    ├─ Group files by project
    │
    ├─ For each project:
    │   ├─ Get compilation via WorkspaceQueryService.GetCompilationAsync(project)
    │   ├─ Load analyzers via AnalyzerLoader.LoadAll(targetFramework)
    │   ├─ compilation.WithAnalyzers(analyzers)
    │   ├─ GetAnalyzerDiagnosticsAsync()
    │   └─ Filter diagnostics to requested files only
    │
    ├─ Merge diagnostics across projects
    │
    ├─ Apply curation set (if named set specified):
    │   ├─ Filter to included rules only
    │   ├─ Override severities
    │   └─ Attach fix classification + rationale from set
    │
    ├─ Enrich via DiagnosticEnricher.ToParlanceDiagnostics()
    │   (converts Roslyn Diagnostic → Parlance Diagnostic with location, severity mapping)
    │
    ├─ Score via IdiomaticScoreCalculator.Calculate()
    │
    ├─ Cap to maxDiagnostics (after scoring, so score reflects true quality)
    │
    └─ Return FileAnalysisResult
```

### Enrichment Order

Curation set application happens **before** `DiagnosticEnricher` because:

- Curation filters out rules we don't want — no point enriching diagnostics we'll discard
- Curation overrides severity — the enricher should see the curated severity, not the raw one
- Fix classification and rationale from the curation set augment the enriched diagnostic

This means `DiagnosticEnricher` evolves to accept curation context alongside the raw Roslyn diagnostic. The existing `ToParlanceDiagnostics()` extension method gains an optional curation parameter.

### Result Types

```csharp
namespace Parlance.Analysis;

public sealed record FileAnalysisResult(
    string CurationSet,
    AnalysisSummary Summary,
    ImmutableList<FileDiagnostic> Diagnostics);

public sealed record FileDiagnostic(
    string RuleId,
    string Category,
    string Severity,
    string Message,
    string FilePath,
    int Line,
    int EndLine,
    int Column,
    int EndColumn,
    string? FixClassification,
    string? Rationale);
```

`FileDiagnostic` extends the existing `Diagnostic` concept with file path (multi-file analysis needs it) and curation metadata (fix classification, rationale). It's a flat record — no nesting — for clean serialization.

`FileAnalysisResult` wraps the diagnostics with summary and curation set name. Same structure the MCP tool returns as JSON.

## Analyze MCP Tool

### Tool Definition

```csharp
[McpServerToolType]
public sealed class AnalyzeTool
{
    [McpServerTool(Name = "analyze", ReadOnly = true)]
    [Description("Run diagnostics on C# files. Returns analyzer findings with severity, fix classification, and rationale.")]
    public static async Task<AnalyzeToolResult> Analyze(
        WorkspaceSessionHolder holder,
        AnalysisService analysis,
        ILogger<AnalyzeTool> logger,
        string[] files,
        string? curationSet = null,
        int? maxDiagnostics = null,
        CancellationToken ct = default)
    {
        // Standard load-state checks (same pattern as all tools)
        // Delegate to AnalysisService.AnalyzeFilesAsync()
        // Format into AnalyzeToolResult
    }
}
```

### Tool Parameters

- `files` — array of absolute file paths. Required.
- `curationSet` — optional named curation set. Omit for project defaults.
- `maxDiagnostics` — optional cap on results returned. Score always reflects all diagnostics.

### Tool Result

```csharp
public sealed record AnalyzeToolResult
{
    // Standard status fields (matching other tools)
    public string Status { get; init; }
    public string? Error { get; init; }

    // Analysis results
    public string? CurationSet { get; init; }
    public AnalyzeSummary? Summary { get; init; }
    public ImmutableList<AnalyzeDiagnostic>? Diagnostics { get; init; }

    // Factory methods for error states
    public static AnalyzeToolResult LoadFailed(string message) => ...;
    public static AnalyzeToolResult NotLoaded() => ...;
}

public sealed record AnalyzeSummary(
    int Total, int Errors, int Warnings, int Suggestions,
    double Score);

public sealed record AnalyzeDiagnostic(
    string RuleId, string Severity, string Message,
    string File, int Line,
    string? FixClassification, string? Rationale);
```

The tool result is shaped for LLM consumption — compact field names, flat structure, no redundant nesting. `AnalyzeDiagnostic` drops `EndLine`/`Column`/`EndColumn` from the MCP output since the agent rarely needs character-level precision. The underlying `FileDiagnostic` retains full location data for CLI/SARIF consumers.

### DI Wiring (Program.cs additions)

```csharp
builder.Services.AddSingleton<CurationSetProvider>();
builder.Services.AddSingleton<AnalysisService>();

// ... existing tool registrations ...
    .WithTools<AnalyzeTool>();
```

## Unified Analyzer Loading

### Current State

Two paths exist today:

1. `CSharpAnalysisEngine` — instantiates 8 PARL analyzers directly via `new PARL0001_...()` array
2. `AnalyzerLoader.LoadAll()` — discovers PARL via reflection + loads upstream DLLs from `analyzer-dlls/`

### Target State

One path: `AnalyzerLoader.LoadAll()`. All analyzers (1 PARL stub + NetAnalyzers + Roslynator) loaded through it. `CSharpAnalysisEngine` continues to exist for the CLI (until Milestone 5 replaces it) but uses `AnalyzerLoader` instead of hardcoded array.

### PARL Trimming

Delete 7 of 8 PARL rules. Keep the simplest one as a stub to keep the `Parlance.CSharp.Analyzers` project alive and the analyzer package buildable. Candidate: `PARL9003_UseDefaultLiteral` — smallest, simplest, no code fix provider.

Deleted rules and their tests are removed. The existing `CSharpAnalysisEngine` test suite updates to reflect 1 rule.

## Error Handling

### Analyzer Failures

An analyzer throwing during execution must not crash the pipeline. `WithAnalyzers()` has built-in analyzer exception handling — Roslyn reports analyzer exceptions as `AD0001` diagnostics. These are logged and filtered from results (they're infrastructure noise, not code quality findings).

### File Not in Workspace

If a requested file isn't in any project, the tool returns a clear message listing which files were found and which weren't. Partial results are returned for files that do resolve.

### Compilation Failures

A project that fails to compile still produces diagnostics (compiler errors are diagnostics). The pipeline runs `GetAnalyzerDiagnosticsAsync()` which excludes compiler diagnostics — only analyzer diagnostics are returned. Compiler errors are the province of the build system, not the analysis tool.

### Curation Set Not Found

If a named curation set is requested but doesn't exist, the tool returns an error with the list of available sets.

## What's NOT in Milestone 4

- **No project or solution scope parameter** — files only, Claude decides which files
- **No `ai-agent` curation set content** — infrastructure only, content from dogfooding
- **No CLI changes** — that's Milestone 5
- **No "changed files" auto-discovery** — Claude handles that via git
- **No `.editorconfig` generation from curation sets** — deferred
- **No curation set editing via MCP** — sets are read-only through the tool

## Existing Types Referenced

From exploration via Parlance MCP tools:

- `Parlance.Abstractions.Diagnostic` — sealed record: `RuleId`, `Category`, `Severity`, `Message`, `Location`, `Rationale?`, `SuggestedFix?`
- `Parlance.Abstractions.AnalysisResult` — sealed record: `ImmutableList<Diagnostic> Diagnostics`, `AnalysisSummary Summary`
- `Parlance.Abstractions.AnalysisSummary` — sealed record: `TotalDiagnostics`, `Errors`, `Warnings`, `Suggestions`, `ImmutableDictionary<string, int> ByCategory`, `double IdiomaticScore`
- `Parlance.CSharp.Workspace.WorkspaceSessionHolder` — `Session`, `IsLoaded`, `LoadFailure`, `SetSession()`, `SetLoadFailure()`
- `Parlance.CSharp.Workspace.WorkspaceQueryService` — `FindSymbolsAsync()`, `GetCompilationAsync()`, `GetCompilationsAsync()`, `GetSemanticModelAsync()`
- `Parlance.CSharp.DiagnosticEnricher` — internal static, `ToParlanceDiagnostics()` extension method
- `Parlance.CSharp.IdiomaticScoreCalculator` — internal static, `Calculate(ImmutableList<Diagnostic>)`
- `Parlance.Analyzers.Upstream.AnalyzerLoader` — internal static, `LoadAll(string targetFramework)`
