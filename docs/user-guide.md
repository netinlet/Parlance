# Parlance User Guide

Last updated: 2026-03-08

This guide describes the product as it exists in the repository today. It focuses on the implemented user-facing surfaces, not the roadmap.

## What Parlance is right now

Parlance is currently a C#-focused code quality toolkit with three practical entry points:

| Entry point | Use it when you want | What it currently does |
| --- | --- | --- |
| `parlance` CLI | ad hoc analysis or auto-fixes against files in a workspace | analyzes files, prints diagnostics, reports an idiomatic score, and applies a small set of safe code fixes |
| `Parlance.CSharp.Analyzers` | only the custom Parlance analyzer package | adds the project's custom Roslyn analyzers to a consuming project |
| `Parlance.CSharp` | a batteries-included analyzer package | pulls in the custom analyzer package plus upstream analyzers and ships a default `.editorconfig` starting point |

Current scope:

- C# only
- CLI support for `analyze`, `fix`, and `rules`
- analyzer package support for IDE/build integration
- upstream analyzer integration in the CLI

Current non-goals or not-yet-shipped areas:

- no LSP server
- no multi-language support
- no deep solution-aware MSBuild integration in the CLI

The custom `PARL` rules should be treated as starter rules that help prove out the pipeline. They are useful, but they are not the main long-term story of the product.

## Choosing the right way to use Parlance

Use the CLI if you want:

- a quick scan of a repository or folder
- a dry-run auto-fix preview
- machine-readable JSON output
- a simple quality gate using an idiomatic score threshold

Use `Parlance.CSharp.Analyzers` if you want:

- only the custom Parlance analyzer package
- to keep your analyzer stack small and explicit

Use `Parlance.CSharp` if you want:

- a single package reference that brings in the current bundled analyzer opinion
- Parlance custom analyzers plus upstream analyzers
- a default `.editorconfig` seed file and build-time code-style enforcement

## Prerequisites

For working in this repository:

- the repo pins `.NET SDK 10.0.100` in `global.json`

For using the CLI:

- the CLI itself targets `net10.0`
- analyzer resolution currently supports `--target-framework net8.0` and `--target-framework net10.0`

For using the analyzer packages:

- the analyzer packages are standard NuGet-based Roslyn analyzer packages
- the custom analyzer package targets `netstandard2.0`

## Running the CLI

There are two practical invocation styles:

From source:

```bash
dotnet run --project src/Parlance.Cli -- <command> [options]
```

As an installed tool:

```bash
parlance <command> [options]
```

In the examples below, `parlance` and `dotnet run --project src/Parlance.Cli --` are interchangeable.

### Commands at a glance

| Command | Purpose |
| --- | --- |
| `parlance analyze <paths...>` | run diagnostics and compute a score |
| `parlance fix <paths...>` | preview or apply supported code fixes |
| `parlance rules` | list the currently available rules for a target framework |

### Paths the CLI accepts

The CLI accepts:

- individual `.cs` files
- directories, scanned recursively for `.cs` files
- glob patterns

Examples:

```bash
parlance analyze src
parlance analyze src/Parlance.Cli/Program.cs
parlance analyze "src/**/*.cs"
```

If no files match, the CLI exits with code `2`.

## `analyze`: scan code and score it

Basic usage:

```bash
parlance analyze src
```

Useful examples:

```bash
parlance analyze src tests --fail-below 80
parlance analyze "src/**/*.cs" --format json
parlance analyze src --suppress PARL0004 IDE0055 --max-diagnostics 50
parlance analyze src --target-framework net8.0 --profile default
```

### What `analyze` outputs

Text output includes:

- one block per diagnostic
- file path and line/column
- severity, rule ID, and message
- rationale and suggested fix when the diagnostic has curated metadata
- a summary section with file count, diagnostic totals, and idiomatic score

JSON output includes:

- one object per diagnostic
- file path, rule ID, category, severity, message, and location
- optional rationale and suggested fix
- a summary payload including the idiomatic score and counts by severity/category

### `analyze` options

| Option | Meaning |
| --- | --- |
| `--format`, `-f` | `text` or `json` |
| `--fail-below <0-100>` | exits with code `1` if the idiomatic score is below the threshold |
| `--suppress <RULE...>` | suppresses selected rule IDs |
| `--max-diagnostics <N>` | caps the number of diagnostics printed |
| `--language-version <VERSION>` | chooses the C# language version used for parsing |
| `--target-framework <TFM>` | selects which upstream analyzer set to load; supported values are `net8.0` and `net10.0` |
| `--profile <NAME>` | validates that the named profile exists; currently only `default` exists |

### Exit codes for `analyze`

| Exit code | Meaning |
| --- | --- |
| `0` | analysis completed and no score threshold failed |
| `1` | `--fail-below` was provided and the score was too low |
| `2` | no matching `.cs` files were found |

## `fix`: preview or apply supported fixes

Basic usage:

```bash
parlance fix src
```

Apply changes to disk:

```bash
parlance fix src --apply
```

Typical workflow:

1. Run `parlance fix <paths>` without `--apply`.
2. Review the preview output.
3. Run the same command with `--apply` when the change set looks safe.

### How `fix` behaves today

- dry-run is the default
- without `--apply`, the command prints the rewritten content for files that would change
- with `--apply`, the command writes the updated files to disk
- if no supported fixes are available, it prints `No auto-fixes available.`

### `fix` options

| Option | Meaning |
| --- | --- |
| `--apply` | writes changes to disk; otherwise the command is a dry-run |
| `--suppress <RULE...>` | suppresses selected fixable rule IDs |
| `--language-version <VERSION>` | chooses the C# language version used for parsing |
| `--target-framework <TFM>` | selects the upstream analyzer set used during analysis |
| `--profile <NAME>` | exposed on the command line, but profile-specific fix behavior is not wired up yet |

### What `fix` can actually change today

Auto-fix coverage is intentionally narrow right now. The CLI currently applies only the starter fixes that are backed by code fix providers. Treat `fix` as a conservative helper, not a broad codebase rewrite engine.

## `rules`: inspect the available analyzer surface

Basic usage:

```bash
parlance rules
```

Useful examples:

```bash
parlance rules --fixable
parlance rules --category Design --severity Warning
parlance rules --format json --target-framework net8.0
```

### What `rules` shows

The rules command merges:

- the custom `PARL` analyzers
- upstream analyzer rules loaded for the selected target framework

For each rule, it reports:

- ID
- default severity
- category
- whether an auto-fix exists
- title

Use this command as the primary way to inspect the rule catalog. That is more useful than memorizing specific `PARL` rules at this stage.

### `rules` options

| Option | Meaning |
| --- | --- |
| `--category <NAME>` | filters by category |
| `--severity <NAME>` | filters by severity |
| `--fixable` | shows only rules with auto-fixes |
| `--format`, `-f` | `text` or `json` |
| `--target-framework <TFM>` | chooses the analyzer set to inspect; supported values are `net8.0` and `net10.0` |

## How scoring works

Parlance computes an idiomatic score from the diagnostics it reports.

Current scoring model:

- start from `100`
- subtract `10` per error
- subtract `5` per warning
- subtract `2` per suggestion
- clamp the result at `0`

Use the score as:

- a lightweight trend signal
- a coarse CI gate
- a quick way to compare the effect of cleanup work

Do not use it as a precise measure of code quality. It is intentionally simple.

## Profiles and target frameworks

Parlance has early support for profiles and target-framework-specific analyzer loading.

### Target frameworks

The CLI supports:

- `net8.0`
- `net10.0`

This choice affects which upstream analyzer DLL set is loaded.

### Profiles

What exists today:

- only the `default` profile is implemented
- profile files exist for both supported target frameworks
- profile content is stored under `src/Parlance.Analyzers.Upstream/profiles/<tfm>/default.editorconfig`

Important current limitation:

- the CLI profile system is not a full policy engine yet
- `analyze` currently validates that the named profile exists
- `fix` exposes `--profile`, but does not currently use it to change behavior

For now, treat `.editorconfig` as the authoritative place for team-level rule tuning.

## Using the NuGet packages

The repository currently defines two package IDs:

- `Parlance.CSharp.Analyzers`
- `Parlance.CSharp`

This guide assumes you will publish them to your chosen feed or consume them from a local/internal feed.

### Option 1: custom analyzer package only

Add a package reference:

```xml
<ItemGroup>
  <PackageReference Include="Parlance.CSharp.Analyzers" Version="0.1.0" />
</ItemGroup>
```

Use this package when you want:

- only the custom Parlance analyzers
- no bundled opinion about upstream packages
- the smallest possible entry point

### Option 2: bundled analyzer package

Add a package reference:

```xml
<ItemGroup>
  <PackageReference Include="Parlance.CSharp" Version="0.1.0" />
</ItemGroup>
```

Use this package when you want the current batteries-included setup.

The bundle currently adds:

- `Parlance.CSharp.Analyzers`
- `Microsoft.CodeAnalysis.NetAnalyzers`
- `Roslynator.Analyzers`
- `EnforceCodeStyleInBuild=true` when the consuming project has not set it already
- a reference `parlance-default.editorconfig` file in the package content and in this repository

### Recommended `.editorconfig` workflow

Use `src/Parlance.CSharp.Package/content/parlance-default.editorconfig` as your starting point.

Recommended steps:

1. Copy that file into your repository root as `.editorconfig`.
2. Adjust severities to match your team's tolerance and rollout plan.
3. Let `.editorconfig` be the main source of truth for build and IDE enforcement.

## Recommended workflows

### Local cleanup pass

Use this when you want a quick picture of the codebase:

```bash
parlance analyze src tests
```

### Safe preview before editing files

Use this when you want to see exactly what the current fixers would do:

```bash
parlance fix src
```

### Apply available safe fixes

Use this after reviewing the dry run:

```bash
parlance fix src --apply
```

### CI or PR gate

Use this when you want a lightweight policy:

```bash
parlance analyze src tests --fail-below 75
```

Or emit JSON for downstream tooling:

```bash
parlance analyze src --format json
```

### Discover what is currently enforced

Use this when you want the real rule surface for a specific TFM:

```bash
parlance rules --target-framework net10.0
```

## Current limitations

These are important to understand before you adopt Parlance broadly.

- The CLI works on matched source files, not on full `.sln` or `.csproj` evaluation.
- Compilation references come from SDK reference packs or fallback runtime assemblies, not from your full project dependency graph.
- Upstream analyzer loading is limited to `net8.0` and `net10.0`.
- Only one profile, `default`, exists today.
- CLI profile handling is still early.
- Auto-fix coverage is intentionally small.
- The custom `PARL` rules are seed rules for bootstrapping the project and should not be treated as the complete value proposition.

The practical implication is simple: Parlance is already useful for exploration, small cleanups, and packaging experiments, but it is still early-stage tooling.

## Related repository documents

Use these when you need more detail than this guide is meant to provide:

- `docs/analyzer-development-guide.md` for analyzer and code-fix authoring guidance
- `docs/manifests/rule-manifest-net8.0.json` for the full generated rule manifest for `net8.0`
- `docs/manifests/rule-manifest-net10.0.json` for the full generated rule manifest for `net10.0`

## Summary

If you want the shortest accurate description of the current product:

- the CLI is the best way to try Parlance today
- the analyzer packages are the right integration surface for IDE/build use
- the bundled package is the most convenient way to adopt the current analyzer opinion
- the product is real and usable, but still clearly in an early, bootstrap phase
