# Milestone 5: CLI Pivot — Design Spec

## Context

Milestones 1–4 delivered a working MCP server with a live Roslyn workspace, semantic navigation tools, and diagnostics via `AnalysisService`. The CLI still uses synthetic compilation — `CompilationFactory`, `WorkspaceAnalyzer`, `WorkspaceFixer`, and `CSharpAnalysisEngine`. None of these talk to the real workspace engine.

Milestone 5 replaces the synthetic compilation path entirely. The CLI becomes a thin client over the same workspace engine the MCP server uses. `AnalysisService` is the shared analysis backbone for both interfaces.

## What Gets Deleted

### Source Files

- `src/Parlance.Cli/Analysis/WorkspaceAnalyzer.cs` — replaced by `AnalysisService`
- `src/Parlance.Cli/Analysis/WorkspaceFixer.cs` — replaced by workspace-based fix pipeline; old code discovers fix providers from PARL assembly only, which is wrong
- `src/Parlance.Cli/PathResolver.cs` — glob file expansion, no longer needed
- `src/Parlance.CSharp/CSharpAnalysisEngine.cs` — hardcoded PARL analyzer instantiation, synthetic compilation
- `src/Parlance.CSharp/CompilationFactory.cs` — synthetic ref-pack loading
- `src/Parlance.CSharp/LanguageVersionResolver.cs` — only existed to support synthetic compilation parse options

### Abstractions (String-Based Analysis Layer)

`IAnalysisEngine.AnalyzeSourceAsync(string sourceCode)` — source-string analysis without project context — is removed. It produces worse results than the workspace path (upstream analyzers behave incorrectly on synthetic compilations) and has no legitimate use case in a workspace-first product.

Deleted from `Parlance.Abstractions`:
- `IAnalysisEngine` — no implementations remain
- `AnalysisOptions` — `LanguageVersion`, `SuppressRules` fields only made sense for the string-based path
- `AnalysisResult` — only returned by `CSharpAnalysisEngine`, superseded by `FileAnalysisResult`

Retained in `Parlance.Abstractions` (still used by workspace path):
- `Diagnostic`, `Location`, `DiagnosticSeverity`, `AnalysisSummary`

Note: an AI agent that wants to test a snippet for diagnostics writes it to a file in the project and calls `analyze` — no special string-based path needed.

### CLI Formatting Layer

- `src/Parlance.Cli/Formatting/AnalysisOutput.cs` — defines `AnalysisOutput` and `FileDiagnostic`, both superseded by `Parlance.Analysis.FileAnalysisResult` and `Parlance.Analysis.FileDiagnostic`

Formatters are updated in place to consume `FileAnalysisResult` from `Parlance.Analysis`.

### Tests

- `CSharpAnalysisEngineTests.cs`
- `CompilationFactoryTests.cs`
- `WorkspaceAnalyzerTests.cs`
- `WorkspaceFixerTests.cs`
- `ProfileValidationTests.cs`
- `PathResolverTests.cs`

## New CLI Input Model

All commands take a `.sln` or `.csproj` path. Loose `.cs` file inputs are not supported — the workspace engine requires a project root.

```
parlance analyze <path> [options]

  <path>              Path to .sln or .csproj (required)
  --format, -f        Output format: text | json (default: text)
  --suppress          Rule IDs to suppress
  --max-diagnostics   Cap on diagnostics returned (score always reflects all)
  --curation-set      Named curation set (default: project defaults)

parlance fix <path> [options]

  <path>              Path to .sln or .csproj (required)
  --dry-run           Preview changes without writing (default: apply)
  --suppress          Rule IDs to suppress

parlance rules

  (no options — lists all rules from loaded analyzers)
```

**Retired options:** `--language-version`, `--target-framework`, `--profile`, `--fail-below`

`analyze` is a reporting tool. Exit code is 0 on success regardless of diagnostic count or score. Consumers (CI scripts, other tools) decide what to do with the output data.

`fix` defaults to applying changes. `--dry-run` previews without writing.

`rules` drops `--target-framework` — it's a listing command, not an analysis command. Always loads with the default TFM.

## DI Wiring

The CLI uses `ServiceCollection` without full `IHost` overhead. Program.cs builds a `ServiceProvider` at startup, commands resolve services from it, it disposes on exit.

```csharp
var services = new ServiceCollection();
services.AddLogging(...);
services.AddSingleton<CurationSetProvider>();
services.AddSingleton<WorkspaceSessionHolder>();
services.AddSingleton<WorkspaceQueryService>();
services.AddSingleton<AnalysisService>();

await using var provider = services.BuildServiceProvider();
```

Each command handler:
1. Resolves services from the provider
2. Opens `CSharpWorkspaceSession` with `WorkspaceMode.Report` (no file watching)
3. Calls `holder.SetSession(session)`
4. Delegates to `AnalysisService` or the fix pipeline
5. Session disposes at end of command

`WorkspaceMode.Report` — no file watcher started, workspace loads once and exits cleanly.

### `Parlance.Cli.csproj` Changes

Gains:
- `<ProjectReference>` to `Parlance.Analysis`
- `<ProjectReference>` to `Parlance.CSharp.Workspace`
- `Microsoft.Extensions.DependencyInjection` package

Loses:
- Direct `Parlance.CSharp` reference (accessed transitively)
- Direct `Parlance.Analyzers.Upstream` reference (accessed transitively)
- `Microsoft.CodeAnalysis.CSharp.Workspaces` package (comes from workspace project)
- `Microsoft.Extensions.FileSystemGlobbing` package (PathResolver deleted)

## Fix Pipeline

### `FixProviderLoader`

New class in `Parlance.Analyzers.Upstream`, mirrors `AnalyzerLoader`. Scans the same analyzer DLL directories for `CodeFixProvider` subtypes via reflection. Returns `ImmutableArray<CodeFixProvider>`. No assumptions about PARL — discovers fixers from NetAnalyzers and Roslynator the same way it discovers analyzers.

### Fix Command Flow

```
fixCommand(path, dryRun, suppress)
    │
    ├─ Open CSharpWorkspaceSession (WorkspaceMode.Report)
    │
    ├─ Get all files from session.CurrentSolution
    │
    ├─ AnalysisService.AnalyzeFilesAsync(allFiles)
    │   └─ Returns FileAnalysisResult with diagnostics
    │
    ├─ FixProviderLoader.LoadAll()
    │   └─ Discovers CodeFixProviders from all analyzer DLLs
    │
    ├─ Iterative fix loop (max 50 iterations):
    │   ├─ For each diagnostic with a matching fix provider:
    │   │   ├─ Register code fixes against real workspace document
    │   │   ├─ Apply first available action
    │   │   └─ Re-analyze after each fix
    │   └─ Break when no more fixable diagnostics
    │
    ├─ Diff original vs fixed per file
    │
    ├─ if --dry-run: print diff, exit
    └─ else: write changed files
```

With `FixProviderLoader` scanning all DLLs, NetAnalyzers and Roslynator fixers are available for the first time. `fix` becomes genuinely useful — not a PARL-only stub.

## Output & Formatting

Formatters consume `FileAnalysisResult` from `Parlance.Analysis` directly. The old `AnalysisOutput` wrapper in `Parlance.Cli` is deleted.

### JSON Shape

```json
{
  "curationSet": "default",
  "summary": {
    "total": 3,
    "errors": 0,
    "warnings": 2,
    "suggestions": 1,
    "score": 87.5
  },
  "diagnostics": [
    {
      "ruleId": "CA1822",
      "severity": "warning",
      "message": "Member 'GetValue' does not access instance data...",
      "file": "src/Foo.cs",
      "line": 12,
      "fixClassification": "auto-fixable",
      "rationale": null
    }
  ]
}
```

### SARIF Decision Gate

After M5 ships, review the JSON output shape. Decide at that point whether SARIF is worth adding in this milestone or deferred.

## Test Changes

### Deleted

All tests for deleted code (listed above).

### Updated

**`CliIntegrationTests.cs`** — fully rewritten:
- Input changes from file paths to `.sln`/`.csproj`
- No PARL rule assumptions in expected output (upstream diagnostics only)
- `--fail-below`, `--language-version`, `--target-framework`, `--profile` tests removed
- `--dry-run` tests added for `fix`

**`JsonFormatterTests.cs` / `TextFormatterTests.cs`** — updated to use `FileAnalysisResult`.

### New

**`FixProviderLoaderTests.cs`** — verifies fix providers are discovered from loaded DLLs with no PARL-specific assumptions.

## Project Structure After M5

```
Parlance.Abstractions              (Diagnostic, Location, DiagnosticSeverity, AnalysisSummary)
    ↑
Parlance.CSharp.Workspace          (compilations, session, file watching)
Parlance.CSharp                    (DiagnosticEnricher, IdiomaticScoreCalculator — no synthetic compilation)
Parlance.Analyzers.Upstream        (AnalyzerLoader, FixProviderLoader)
    ↑
Parlance.Analysis                  (AnalysisService, CurationSetProvider — shared by MCP and CLI)
    ↑
Parlance.Mcp                       (MCP server — unchanged)
Parlance.Cli                       (thin client — analyze, fix, rules over workspace engine)
```
