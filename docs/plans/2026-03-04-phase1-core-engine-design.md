# Phase 1: Core Engine + Custom Rules — Design

## Overview

Phase 1 builds the foundation: `Parlance.Abstractions` (language-agnostic interfaces) and `Parlance.CSharp` (first language engine). Given a C# source string, the engine returns structured diagnostics with rationale and an idiomatic score.

No upstream analyzer packages, no profiles, no `.editorconfig` loading, no CLI, no MCP server. Just the pipeline and 5 custom rules.

## Solution Structure

```
parlance/
├── src/
│   ├── Parlance.Abstractions/          # Language-agnostic interfaces & shared types
│   └── Parlance.CSharp/               # C# language engine
│       ├── Rules/                      # Custom PARL analyzers
│       └── (internal helpers)
├── tests/
│   └── Parlance.CSharp.Tests/
│       └── Rules/                      # Per-rule test classes
└── Parlance.sln
```

## Target Framework

net10.0 for all projects.

## Abstractions (`Parlance.Abstractions`)

Zero dependencies. Sealed positional records. `ImmutableDictionary` for computed-once data.

```csharp
namespace Parlance.Abstractions;

public interface IAnalysisEngine
{
    string Language { get; }

    Task<AnalysisResult> AnalyzeSourceAsync(
        string sourceCode,
        AnalysisOptions? options = null,
        CancellationToken ct = default);
}

public sealed record AnalysisOptions(
    string[] SuppressRules,
    int? MaxDiagnostics = null,
    bool IncludeFixSuggestions = true)
{
    public AnalysisOptions() : this(SuppressRules: []) { }
}

public sealed record AnalysisResult(
    IReadOnlyList<Diagnostic> Diagnostics,
    AnalysisSummary Summary);

public sealed record Diagnostic(
    string RuleId,
    string Category,
    DiagnosticSeverity Severity,
    string Message,
    Location Location,
    string? Rationale = null,
    string? SuggestedFix = null);

public enum DiagnosticSeverity { Error, Warning, Suggestion, Silent }

public sealed record Location(
    int Line,
    int Column,
    int EndLine,
    int EndColumn);

public sealed record AnalysisSummary(
    int TotalDiagnostics,
    int Errors,
    int Warnings,
    int Suggestions,
    ImmutableDictionary<string, int> ByCategory,
    double IdiomaticScore);
```

## C# Engine (`Parlance.CSharp`)

### Dependencies

- `Microsoft.CodeAnalysis.CSharp`
- `Parlance.Abstractions`

### Components

**`CSharpAnalysisEngine`** — implements `IAnalysisEngine`. Pipeline:
1. Parse source to SyntaxTree
2. `CompilationFactory.Create()` — build CSharpCompilation with net10 reference assemblies
3. Load PARL analyzers (direct instantiation, no assembly scanning)
4. `CompilationWithAnalyzers.GetAnalyzerDiagnosticsAsync()`
5. Filter by `SuppressRules`, cap by `MaxDiagnostics`
6. `DiagnosticEnricher.Enrich()` — map Roslyn diagnostics to Parlance types, add rationale
7. `IdiomaticScoreCalculator.Calculate()` — compute score and summary
8. Return `AnalysisResult`

```csharp
public sealed class CSharpAnalysisEngine : IAnalysisEngine
{
    public string Language { get; } = "csharp";
    // ...
}
```

**`CompilationFactory`** (`internal static`) — resolves net10 reference assemblies from installed SDK, caches them.

**`DiagnosticEnricher`** (`internal static`) — maps Roslyn diagnostics to Parlance model, looks up rationale and suggested fix text by rule ID from a static dictionary.

**`IdiomaticScoreCalculator`** (`internal static`) — simple weighted deduction:
- Start at 100
- Error: -10, Warning: -5, Suggestion: -2
- Floor at 0

## Custom Rules

Diagnostic ID prefix: **`PARL`** (chosen to avoid collision with known prefixes per Microsoft guidance to use 3+ character prefixes).

| Rule | Name | Detects | Severity | Min C# |
|------|------|---------|----------|--------|
| PARL0001 | Prefer Primary Constructors | Constructor only assigns params to fields/properties | Suggestion | 12 |
| PARL0002 | Prefer Collection Expressions | `new List<T>{...}`, `new[]{...}`, etc. | Suggestion | 12 |
| PARL0003 | Prefer Required Properties | Constructor params only assigned to public init/set props | Suggestion | 11 |
| PARL0004 | Use Pattern Matching Over Is+Cast | `if (x is Foo) { var y = (Foo)x; }` | Warning | 7 |
| PARL0005 | Use Switch Expression | Switch statement returning value from every branch | Suggestion | 8 |

Each rule is a `sealed class` inheriting `DiagnosticAnalyzer`. PARL0001-0003 are language-version-aware (suppress if project targets older C#).

Note: PARL0001 overlaps with IDE0290. Deduplication deferred to when upstream analyzers are integrated.

Note: PARL0001 and PARL0003 can fire on the same type when a constructor assigns to public settable properties (both "prefer primary constructor" and "prefer required properties" apply). These are contradictory recommendations. Future work should add a guard so only the more specific rule (PARL0003) fires in this case.

## Testing

- **Framework:** xUnit + `Microsoft.CodeAnalysis.CSharp.Testing.XUnit`
- **Per-rule tests:** positive case (flags anti-pattern) + negative case (no false positives)
- **Integration tests** (`CSharpAnalysisEngineTests`):
  - Multi-issue source → correct diagnostics, rationale, and score
  - Clean source → score 100, empty diagnostics
  - `SuppressRules` filters correctly
  - `MaxDiagnostics` caps output
- **Score tests** (`IdiomaticScoreCalculatorTests`):
  - No diagnostics → 100
  - Mixed severities → correct deductions
  - Enough diagnostics → floor at 0

## Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Product name | Parlance | Working name |
| Diagnostic prefix | PARL | 4 chars, no known collision, follows MS guidance |
| TFM | net10.0 | Current target |
| Upstream analyzers | Deferred | Phase 1 is custom rules only |
| Profiles | Deferred | No `.editorconfig` loading in Phase 1 |
| Scoring | Simple weighted deduction | Tune empirically later |
| Reference resolution | net10 ref assemblies from SDK | Configurable TFM comes later |
| Records | Sealed, positional syntax | Immutable by default, no inheritance |
| Internal helpers | `internal static` | Implementation details, not part of public API |
