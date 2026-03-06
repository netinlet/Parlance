# Phase 2: CLI Tool — Design

## Overview

Phase 2 adds a dotnet global tool (`parlance`) that analyzes C# source files and applies auto-fixes using a Roslyn workspace pipeline. The fix command is the headline feature, shipping code fix providers only for rules that meet the analyzer development guide's bar for safety.

## Solution Structure

```
parlance/
├── src/
│   ├── Parlance.Abstractions/           # (existing)
│   ├── Parlance.CSharp/                 # (existing) — engine
│   ├── Parlance.CSharp.Analyzers/       # (existing) — add CodeFixProviders here
│   └── Parlance.Cli/                    # NEW — dotnet global tool
├── tests/
│   ├── Parlance.CSharp.Tests/           # (existing) — add code fix tests
│   └── Parlance.Cli.Tests/             # NEW — CLI integration tests
└── Parlance.sln
```

### Parlance.Cli

- Console app: `<OutputType>Exe</OutputType>`, `<PackAsTool>true</PackAsTool>`, `<ToolCommandName>parlance</ToolCommandName>`
- TFM: net10.0
- Dependencies: `System.CommandLine`, `Parlance.CSharp`, `Parlance.CSharp.Analyzers`, `Microsoft.CodeAnalysis.CSharp.Workspaces`

## Commands

```
parlance analyze <paths...> [options]
  paths              One or more files, directories, or globs ("src/**/*.cs")
  --format, -f       Output format: text (default), json
  --fail-below       Exit code 1 if idiomatic score < threshold (0-100)
  --suppress         Suppress rule IDs (e.g. --suppress PARL0001 PARL0003)
  --max-diagnostics  Cap number of diagnostics returned
  --language-version C# language version (default: Latest)

parlance fix <paths...> [options]
  paths              One or more files, directories, or globs
  --apply            Actually write changes (default is dry-run)
  --format, -f       Output format: text (default), json
  --suppress         Suppress rule IDs
  --language-version C# language version

parlance rules [options]
  --category         Filter by category (e.g. Modernization, PatternMatching)
  --severity         Filter by severity (Error, Warning, Suggestion)
  --fixable          Show only rules that have auto-fixes
  --format, -f       Output format: text (default), json
```

### Exit Codes

- 0 — success (or score above threshold)
- 1 — score below `--fail-below` threshold, or analysis errors
- 2 — invalid arguments / file not found

## Workspace Pipeline

The CLI uses `AdhocWorkspace` for both analysis and fixes, separate from the existing `CSharpAnalysisEngine` which remains unchanged for future MCP server use.

### Analyze Flow

1. Resolve input paths (expand globs, recurse directories, filter `*.cs`)
2. Create `AdhocWorkspace` with a single project
3. Add each source file as a `Document`
4. Add net10 reference assemblies (shared helper extracted from `CompilationFactory`)
5. Get `Compilation`, run `CompilationWithAnalyzers.GetAnalyzerDiagnosticsAsync()`
6. Enrich diagnostics via `DiagnosticEnricher`
7. Score via `IdiomaticScoreCalculator`
8. Format output (text or JSON)

### Fix Flow

1. Same steps 1-5 as analyze
2. For each diagnostic with a registered `CodeFixProvider`:
   - Get fix via `CodeFixProvider.RegisterCodeFixesAsync()`
   - Extract the `CodeAction` and apply to workspace
3. Diff original vs fixed documents
4. If `--apply`: write fixed documents to disk
5. If dry-run: display the diff

### Reference Assembly Resolution

Extract reference assembly logic from `CompilationFactory` into a shared internal helper that both the engine and CLI workspace can use.

## Code Fix Providers

Per the analyzer development guide, fixes ship only for rules where the transformation is mechanical, meaning-preserving, and batch-safe.

### Phase 2 Fix Providers

| Rule | Fix | Justification |
|------|-----|---------------|
| PARL0004 | `if (x is Foo) { var y = (Foo)x; }` → `if (x is Foo y) { }` | Mechanical pattern match, no semantic change |
| PARL9001 | `using (var x = ...) { }` → `using var x = ...;` | Mechanical, scoping only narrows (safe) |

### Deferred (No Fix Provider)

| Rule | Why |
|------|-----|
| PARL0001 | Changes construction semantics, complex refactoring |
| PARL0002 | Target-typed, context-dependent |
| PARL0003 | Changes object creation requirements, call sites must change |
| PARL0005 | Moderate complexity, multiple valid transformations possible |
| PARL9002 | Target-typed, needs context verification |
| PARL9003 | Target-typed, needs context verification |

### Fix Provider Requirements

Each fix provider:
- Extends `CodeFixProvider`
- Exported via `[ExportCodeFixProvider]`
- Wired to exact diagnostic ID via `FixableDiagnosticIds`
- Preserves trivia via `Formatter.Annotation`
- Supports cancellation
- Stateless, batch-safe

## Output Formatting

### Text Format (default)

```
src/Example.cs(12,5): warning PARL0004: Consider using pattern matching instead of 'is' followed by cast
  Rationale: Pattern matching is safer and more concise
  Suggested: if (x is Foo y) { ... }

src/Example.cs(25,1): suggestion PARL9001: Consider using a simple using declaration
  Rationale: Using declarations reduce nesting without changing scope behavior

── Summary ──────────────────────────
  Files analyzed: 3
  Diagnostics: 2 (0 errors, 1 warning, 1 suggestion)
  Idiomatic Score: 93/100
```

### JSON Format

```json
{
  "diagnostics": [
    {
      "ruleId": "PARL0004",
      "category": "PatternMatching",
      "severity": "Warning",
      "message": "Consider using pattern matching instead of 'is' followed by cast",
      "location": {
        "path": "src/Example.cs",
        "line": 12,
        "column": 5,
        "endLine": 12,
        "endColumn": 38
      },
      "rationale": "...",
      "suggestedFix": "..."
    }
  ],
  "summary": {
    "filesAnalyzed": 3,
    "totalDiagnostics": 2,
    "errors": 0,
    "warnings": 1,
    "suggestions": 1,
    "byCategory": { "PatternMatching": 1, "Modernization": 1 },
    "idiomaticScore": 93
  }
}
```

Formatting is handled by an `IOutputFormatter` interface with `TextFormatter` and `JsonFormatter` implementations — clean seam for SARIF later.

## Testing

### Unit Tests (Parlance.CSharp.Tests — existing)

Code fix provider tests using `Microsoft.CodeAnalysis.CSharp.Testing.XUnit`:
- Per fix: positive case, no-fix-offered case, trivia preservation case
- Uses Roslyn test markup for expected spans

### Integration Tests (Parlance.Cli.Tests — new)

Invoke `parlance` as a process, assert on exit code and stdout:
- `analyze` single file — text output with diagnostics
- `analyze` directory — aggregated results
- `analyze` glob pattern — correct file matching
- `analyze --format json` — valid JSON, correct schema
- `analyze --fail-below 80` — exit code 1 when score is low
- `analyze --suppress PARL0004` — rule filtered out
- `fix` dry-run — shows diff, files unchanged
- `fix --apply` — files modified on disk, correct content
- `rules` — table output with all rules
- `rules --fixable` — only PARL0004, PARL9001
- Invalid path — exit code 2, error message
- No `.cs` files found — meaningful message

## Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Tool name | `parlance` | Dotnet tool is inherently C#-scoped |
| CLI framework | `System.CommandLine` | Blueprint spec, standard .NET choice |
| Pipeline | `AdhocWorkspace` | Required for code fix application, consistent model |
| Fix scope | PARL0004, PARL9001 only | Only rules meeting analyzer dev guide safety bar |
| Output formats | Text + JSON | SARIF deferred, easy to add via formatter interface |
| Fix default | Dry-run | Safer default, `--apply` to write |
| Score command | Flag on analyze (`--fail-below`) | Same operation, no need for separate command |
| Existing engine | Unchanged | Remains useful for MCP server (Phase 4) |
