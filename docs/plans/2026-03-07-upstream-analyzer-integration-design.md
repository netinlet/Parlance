# Upstream Analyzer Integration Design

## Overview

Integrate upstream analyzer packages (Microsoft CA rules, IDE code style rules, Roslynator) into Parlance's analysis engine with version-aware, profile-based configuration. This replaces the current hardcoded PARL-only analyzer arrays with dynamic discovery of all analyzers — PARL and upstream alike.

## Supported TFMs

- net8.0 (current LTS)
- net10.0 (current STS, our build target)

Additional TFMs can be added later by pinning package versions and generating manifests.

## Upstream Analyzer Packages

All sourced from NuGet, version-pinned per TFM:

| Rules | Package | net8.0 Version | net10.0 Version |
|-------|---------|----------------|-----------------|
| CA (code quality) | `Microsoft.CodeAnalysis.NetAnalyzers` | 8.0.x | 10.0.101 |
| IDE (code style) | `Microsoft.CodeAnalysis.CSharp.CodeStyle` | 4.8.x | 5.0.0 |
| RCS (Roslynator) | `Roslynator.Analyzers` | 4.15.0 | 4.15.0 |

Roslynator is TFM-agnostic (minimum Roslyn 3.8.0), so the same version serves both TFMs. Microsoft packages are version-pinned to match the corresponding SDK release.

## Architecture

```
NuGet Packages (pinned per TFM)
  │
  ├── Microsoft.CodeAnalysis.NetAnalyzers (CA rules)
  ├── Microsoft.CodeAnalysis.CSharp.CodeStyle (IDE rules)
  └── Roslynator.Analyzers (RCS rules)
        │
        ▼
MSBuild Targets (extract analyzer DLLs from NuGet cache)
        │
        ▼
Output Directory (organized by TFM)
  ├── net8.0/*.dll
  └── net10.0/*.dll
        │
        ├──► Manifest Generator (build-time tool)
        │       └── rule-manifest.json per TFM
        │
        └──► AnalyzerLoader (runtime)
                ├── Loads upstream DLLs via AssemblyLoadContext
                ├── Discovers PARL analyzers from Parlance.CSharp.Analyzers
                └── Returns all DiagnosticAnalyzer instances
                        │
                        ▼
              CompilationWithAnalyzers
                ├── Profile .editorconfig applied via AnalyzerConfigOptionsProvider
                └── Existing pipeline (enrich → score → format)
```

## New Project: Parlance.Analyzers.Upstream

A class library that manages upstream analyzer packages and provides runtime loading.

```
src/Parlance.Analyzers.Upstream/
├── Parlance.Analyzers.Upstream.csproj
├── AnalyzerLoader.cs
└── profiles/
    ├── net8.0/
    │   ├── default.editorconfig
    │   ├── strict.editorconfig
    │   ├── library.editorconfig
    │   ├── minimal.editorconfig
    │   └── ai-agent.editorconfig
    └── net10.0/
        ├── default.editorconfig
        ├── strict.editorconfig
        ├── library.editorconfig
        ├── minimal.editorconfig
        └── ai-agent.editorconfig
```

### Parlance.Analyzers.Upstream.csproj

References upstream analyzer NuGet packages with `PrivateAssets="all"`. MSBuild targets copy analyzer DLLs from the NuGet cache `analyzers/dotnet/cs/` folder to the output directory, organized by TFM.

Profile `.editorconfig` files are embedded resources or content files shipped with the assembly.

### AnalyzerLoader

Loads analyzer assemblies at runtime using `AssemblyLoadContext` for isolation.

```csharp
internal static class AnalyzerLoader
{
    /// Returns all analyzers (PARL + upstream) for the given TFM.
    /// PARL analyzers discovered via reflection on Parlance.CSharp.Analyzers assembly.
    /// Upstream analyzers loaded from vendored NuGet DLLs.
    static ImmutableArray<DiagnosticAnalyzer> LoadAll(string targetFramework);
}
```

No hardcoded arrays anywhere. Adding a new PARL analyzer means writing the class — it's discovered automatically. Updating upstream versions means changing the NuGet package pin and regenerating manifests.

## New Tool: Parlance.ManifestGenerator

A console app that produces `rule-manifest.json` per TFM. Run whenever vendored package versions change.

```
tools/Parlance.ManifestGenerator/
├── Parlance.ManifestGenerator.csproj
└── Program.cs
```

### What It Does

1. Loads all analyzer assemblies for a given TFM (same logic as `AnalyzerLoader`)
2. Reflects over every `DiagnosticAnalyzer`, iterates `SupportedDiagnostics`
3. Emits a JSON manifest per rule

### Manifest Schema

```json
{
  "targetFramework": "net10.0",
  "generatedAt": "2026-03-07",
  "rules": [
    {
      "id": "CA1822",
      "title": "Mark members as static",
      "category": "Performance",
      "defaultSeverity": "suggestion",
      "description": "Members that do not access instance data...",
      "helpUrl": "https://learn.microsoft.com/...",
      "source": "Microsoft.CodeAnalysis.NetAnalyzers",
      "hasRationale": false,
      "hasSuggestedFix": false
    }
  ]
}
```

### Consumers

- **DiagnosticEnricher** — metadata skeleton. Curated rationale/fix suggestions stored separately and merged.
- **RulesCommand** — lists rules from manifest instead of hardcoded array.
- **Profile authoring** — starting point for writing `.editorconfig` profiles.
- **Diffing** — compare manifests across package version upgrades to see added/removed/changed rules.

## Profile System

Profiles are `.editorconfig` files per TFM, applied via Roslyn's native `AnalyzerConfigOptionsProvider`.

### Profile Definitions

| Profile | Intent |
|---------|--------|
| default | Balanced — warnings for bugs, suggestions for idioms |
| strict | Elevates most suggestions to warnings |
| library | Adds ConfigureAwait, public API docs, allocation warnings |
| minimal | Only bugs and security. For legacy codebases. |
| ai-agent | Correctness + idiomatic patterns, suppresses style-only rules |

### How Profiles Work

- Profiles only specify overrides — rules not mentioned use their default severity from the manifest
- A rule set to `none` in a profile doesn't run
- User's project `.editorconfig` takes precedence over the profile (standard Roslyn `.editorconfig` precedence)

### Authoring Workflow

1. Run manifest generator to see all rules with defaults
2. Write `.editorconfig` entries for rules that differ from defaults
3. Regenerate when package versions change and review diffs

## Enrichment Metadata

### Current State

`DiagnosticEnricher` has a hardcoded `FrozenDictionary<string, RuleMetadata>` with rationale and fix suggestions for each PARL rule.

### New Approach

- Manifest generator emits a metadata skeleton with upstream-provided `Title`, `Description`, and `HelpLinkUri`
- Curated rationale and fix suggestions stored in separate files, merged at build time or runtime
- Rules without curated rationale fall back to upstream descriptions
- Curated content added incrementally over time — not a launch blocker

## Changes to Existing Components

### WorkspaceAnalyzer

- Remove hardcoded `AllAnalyzers` array
- Accept `targetFramework` and `profile` parameters
- Call `AnalyzerLoader.LoadAll(targetFramework)` for analyzers
- Apply profile `.editorconfig` via `AnalyzerConfigOptionsProvider`

### DiagnosticEnricher

- Remove hardcoded `FrozenDictionary<string, RuleMetadata>`
- Load metadata from generated manifest + curated overrides
- Fall back to upstream `Description` and `HelpLinkUri` for rules without curated rationale

### RulesCommand

- Remove hardcoded `AllAnalyzers` array and `FixableRuleIds`
- Read from manifest for the selected TFM
- Add `--target-framework` option (default: net10.0)
- Add `--profile` option (default: default)

### AnalyzeCommand / FixCommand

- Add `--target-framework` option (default: net10.0)
- Add `--profile` option (default: default)

### Unchanged

- **IdiomaticScoreCalculator** — works with any `Diagnostic` list regardless of source
- **CompilationFactory** — reference assembly loading is independent of which analyzers run

## Testing Strategy

### AnalyzerLoader Tests

- Loading for net8.0 returns analyzers
- Loading for net10.0 returns analyzers
- PARL analyzers are discovered automatically (no hardcoded registration)
- Unknown TFM produces a clear error
- Analyzer counts match expected ranges (catches accidentally dropped assemblies)

### Manifest Generator Tests

- Generated manifest contains expected rule IDs (spot-check known CA/IDE/RCS rules)
- Manifest structure is valid JSON with required fields
- Regenerating from same assemblies produces identical output (deterministic)
- net8.0 vs net10.0 manifests differ where expected

### Integration Tests

- Analysis with `default` profile produces diagnostics from upstream analyzers (not just PARL)
- Profile severity overrides work (a rule set to `none` in `minimal` doesn't fire)
- `--target-framework net8.0` loads different rule set than `net10.0`
- User `.editorconfig` overrides take precedence over profile
- `rules` command lists upstream rules with correct metadata
- Existing PARL analyzer tests continue to pass unchanged

### Out of Scope

Every individual upstream rule — that's the upstream maintainers' responsibility. We test that our loading, configuration, and enrichment pipeline works correctly with their analyzers.

## Version Awareness

Rules change between SDK versions. The version-aware design handles this:

- **Package versions pinned per TFM** — net8.0 gets the analyzer versions that shipped with .NET 8 SDK, net10.0 gets the .NET 10 SDK versions
- **Manifests generated per TFM** — captures exactly which rules exist at each version with their default severities
- **Profiles authored per TFM** — a rule that doesn't exist in net8.0 won't appear in net8.0 profiles
- **Diffing across versions** — regenerating manifests after a package upgrade shows exactly what changed

## Open Questions

1. **Exact net8.0 package versions** — need to determine which `Microsoft.CodeAnalysis.NetAnalyzers` and `Microsoft.CodeAnalysis.CSharp.CodeStyle` versions shipped with the .NET 8 SDK
2. **Roslynator.Formatting.Analyzers** — include formatting rules (RCS0xxx) or defer? The blueprint lists them but they may be noisy for initial launch
3. **Scoring weights** — should upstream rule categories use different weights than PARL rules in `IdiomaticScoreCalculator`?
