# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

use modern C# syntax

do not push commits

do not attribute commits

## Build & Test Commands

```bash
# Build (exclude Package project which is pack-only)
dotnet build src/Parlance.Cli/Parlance.Cli.csproj

# Run all tests
dotnet test Parlance.sln

# Run a specific test project
dotnet test tests/Parlance.CSharp.Tests/Parlance.CSharp.Tests.csproj

# Run a single test by name
dotnet test tests/Parlance.Cli.Tests/Parlance.Cli.Tests.csproj --filter "Analyze_SingleFile_ShowsDiagnostics"

# Check formatting (CI enforces this)
dotnet format Parlance.sln --verify-no-changes
```

Note: `dotnet build Parlance.sln` fails because `Parlance.CSharp.Package` sets `NoBuild=true` (it's a pack-only metapackage). Build individual projects or exclude it.

## Architecture

Parlance is a C# code quality analysis tool built on Roslyn. The dependency flow is:

```
Parlance.Abstractions          (interfaces: IAnalysisEngine, models: Diagnostic, AnalysisResult)
    ↑
Parlance.CSharp.Analyzers      (netstandard2.0 — 8 Roslyn analyzers, PARL0001-PARL9003)
    ↑
Parlance.CSharp                (analysis engine — CompilationFactory, DiagnosticEnricher, ScoreCalculator)
    ↑
Parlance.Analyzers.Upstream    (loads NetAnalyzers + Roslynator via reflection)
    ↑
Parlance.Cli                   (System.CommandLine 2.0.3 — analyze/fix/rules commands)
```

**NuGet packages** (under `src/`):
- `Parlance.CSharp.Analyzers` — Roslyn analyzer package (netstandard2.0, ships in `analyzers/dotnet/cs/`)
- `Parlance.CSharp.Package` (PackageId=`Parlance.CSharp`) — bundle metapackage pulling in custom + upstream analyzers. Must NOT use `DevelopmentDependency` or `SuppressDependenciesWhenPacking`.

## Key Conventions

- **System.CommandLine 2.0.3 stable** — uses `SetAction` (not `SetHandler`), `parseResult.GetValue()`, `Parse(args).InvokeAsync()`. The beta API is completely different.
- **Diagnostic prefix:** `PARL` (4-char, per MS guidance)
- **Analyzer TFM:** netstandard2.0 (Roslyn requirement). All other projects target net10.0.
- **Test framework:** xUnit with `Microsoft.CodeAnalysis.CSharp.Testing.XUnit` for analyzer tests
- **Shared version** (0.1.0) set in `Directory.Build.props`
- **Fixable rules:** PARL0004 (pattern matching), PARL9001 (using declarations)
- **Scoring:** Weighted deduction — error -10, warning -5, suggestion -2, floor 0

## Code Style

Enforced via `.editorconfig` and CI formatting check:
- File-scoped namespaces only
- `var` everywhere
- Pattern matching preferred (switch expressions, `is` patterns, `not` patterns)
- Seal classes by default
- Positional record syntax / primary constructors
- No consts for single-use strings
