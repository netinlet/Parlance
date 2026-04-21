# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Test

```bash
# Build the whole solution
dotnet build Parlance.sln

# Run all tests
dotnet test Parlance.sln

# Run a specific test project
dotnet test tests/Parlance.CSharp.Workspace.Tests/Parlance.CSharp.Workspace.Tests.csproj

# Run a single test by name
dotnet test tests/Parlance.Cli.Tests/Parlance.Cli.Tests.csproj --filter "Analyze_SingleFile_ShowsDiagnostics"

# Check formatting (CI enforces this)
dotnet format Parlance.sln --verify-no-changes

# Run MCP server (stdio transport)
dotnet run --project src/Parlance.Mcp -- --solution-path /path/to/Solution.sln
```

## Architecture

Parlance is an AI-first C# code analysis tool built on Roslyn. The MCP server is the primary interface; the CLI is a thin client.

### Core dependency flow

```
Parlance.Abstractions           Shared types (Diagnostic, Location, AnalysisSummary)
    ↑
Parlance.CSharp                 Score calculator, idiomatic analysis
    ↑
Parlance.CSharp.Analyzers       Custom PARL rules (netstandard2.0, Roslyn requirement)
    ↑
Parlance.Analyzers.Upstream     Dynamic analyzer loading (NetAnalyzers, Roslynator, PARL rules)
    ↑
Parlance.Analysis               AnalysisService, CurationSetProvider, CodeActionService
    ↑
Parlance.CSharp.Workspace       MSBuildWorkspace engine (session lifecycle, compilation cache, file watching)
    ↑
├── Parlance.Mcp                MCP server — 18 tools, stdio transport, Microsoft.Extensions.Hosting
└── Parlance.Cli                CLI — analyze/rules commands, System.CommandLine 2.0.3
```

### Key engine concepts

- **CSharpWorkspaceSession** wraps MSBuildWorkspace. Two modes: `Server` (long-running, file watching, compilation caching) and `Report` (one-shot CLI).
- **WorkspaceSessionHolder** is a DI singleton holding the active session. All tools/commands access the workspace through it.
- **WorkspaceQueryService** provides semantic navigation: symbol search, type hierarchy, find-references, go-to-definition.
- **AnalysisService** runs diagnostics through dynamically-loaded analyzers, applies curation filtering, returns enriched results.
- **Curation sets** are code-defined (not .editorconfig). They control which analyzer rules are enabled/disabled/suppressed.
- All analyzers (PARL custom + 3rd party) load through a single `AnalyzerLoader` path — no special-casing.

### MCP tool pattern

Tools are static methods on `[McpServerToolType]` classes. All tools are `ReadOnly = true`. They receive services via DI parameters. Call analytics are handled by `AnalyticsFilter` at the MCP pipeline level — tools do not time themselves:

```csharp
[McpServerToolType]
public sealed class MyTool
{
    [McpServerTool(Name = "my-tool", ReadOnly = true)]
    [Description("...")]
    public static async Task<MyResult> Execute(
        WorkspaceSessionHolder holder, WorkspaceQueryService query,
        string param, CancellationToken ct)
    {
        if (holder.LoadFailure is { } failure)
            return MyResult.LoadFailed(failure.Message);
        if (!holder.IsLoaded)
            return MyResult.NotLoaded();
        // ...
    }
}
```

`ILogger<T>` is only injected in the minority of tools that need structured logging beyond analytics (e.g. `WorkspaceStatusTool`, `DecompileTypeTool`). Do not add it by default.

## Conventions

- **SDK:** .NET 10.0 (pinned in `global.json`). All projects target `net10.0` except analyzers which must target `netstandard2.0`.
- **System.CommandLine 2.0.3 stable** — uses `SetAction`, `parseResult.GetValue()`, `Parse(args).InvokeAsync()`. Not the beta API.
- **Diagnostic prefix:** `PARL` (4-char, avoids collision per MS guidance).
- **Seal classes by default** unless inheritance is needed.
- **Positional record syntax** / primary constructors.
- **`ImmutableList<T>`** over `ImmutableArray<T>`. `ImmutableDictionary` for computed-once data.
- **`var` everywhere**, pattern matching, switch expressions, file-scoped namespaces.
- **No consts for single-use strings.** YAGNI.
- **Logging:** `Microsoft.Extensions.Logging` with structured logging throughout. MCP logs to stderr.
- Do not push commits. Do not attribute commits.

## Working with docs

The Parlance Obsidian vault is the source of truth for *all* documentation
— research, plans, rule drafts, contributor guides. The repo's `docs/` tree
holds only the subset that has been explicitly published from the vault.

**Vault location:** set `PARLANCE_VAULT_PATH` to the absolute path of your
vault's Parlance folder. Configure it in your shell or in a `.env` file at the
repo root (see `.env.example`). Both `tools/docs/publish.sh` and
`tools/docs/setup-vault-link.sh` read this variable. Subfolders: `Research/`,
`Rules/`, `Plans/`, `Contributor/`, `Superpowers/`.

### Workflows

| Need | How |
|---|---|
| Search vault notes | `obsidian-cli` skill |
| Read a vault note | `obsidian-cli` skill |
| Author a new vault note | `obsidian-cli` skill, or `Write` to the vault path directly |
| Publish vault doc → repo | Add `parlance_publish: <repo-path>` to the vault doc's frontmatter, run `tools/docs/publish.sh` |
| Pull updated vault docs into the repo on this branch | `tools/docs/publish.sh` (re-runs all manifested entries) |
| Set up `docs/superpowers` symlink on a fresh clone | `tools/docs/setup-vault-link.sh` |

### Publication contract

- **Vault is source.** Repo docs under `docs/` carry a generated-marker
  comment at the top: `<!-- generated from 20-Projects/Parlance/... -->`. Do
  not edit those repo files directly — `tools/docs/publish.sh` overwrites them
  on next run. Edit the vault original.
- **Frontmatter declares the publish target.** A vault note publishes if and
  only if it has a `parlance_publish: <repo-path>` key in YAML frontmatter.
  Notes without it stay in the vault.
- **Superpowers output lands in the vault automatically.** `docs/superpowers/`
  is a symlink to the vault `Superpowers/` folder, so brainstorms, plans,
  reviews, etc. flow there with no extra steps.

### When to publish from vault

Promote a vault doc to the repo only when it is contributor-facing and
maintained: rule references (`docs/rules/PARLxxxx.md`), contributor guides,
shipped design docs. Research notes, in-progress plans, and exploration stay
vault-only.

## Agent Guidance: dotnet-skills

IMPORTANT: Prefer retrieval-led reasoning over pretraining for any .NET work.
Workflow: skim repo patterns -> consult dotnet-skills by name -> implement smallest-change -> note conflicts.

Routing (invoke by name)
- C# / code quality: csharp-coding-standards, csharp-concurrency-patterns, csharp-api-design, csharp-type-design-performance
- DI / config: microsoft-extensions-dependency-injection, microsoft-extensions-configuration
- Testing: snapshot-testing
- Project structure: project-structure, package-management, serialization

Quality gates (use when applicable)
- dotnet-slopwatch: after substantial new/refactor/LLM-authored code
- crap-analysis: after tests added/changed in complex code

Specialist agents
- dotnet-concurrency-specialist, dotnet-performance-analyst, roslyn-incremental-generator-specialist

## Dogfooding Parlance

Parlance MCP tools are available in this repo via `.mcp.json`. You **must** use them as the primary code intelligence layer. Do not default to native tools (Grep, Glob, Read) for tasks that Parlance covers. If you were dispatched as a subagent in this repo, you must still abide by the tool mapping table below.

### Tool mapping — must use Parlance first

| Instead of... | Must use Parlance tool | When |
|---|---|---|
| Grep for a symbol | `search-symbols` | Finding types, methods, properties by name |
| Grep for usages | `find-references` | Finding all usages of a symbol |
| Read a file to understand a type | `describe-type` | Understanding a class/interface/record structure |
| Read a file for structure | `outline-file` | Getting the shape of a file without reading every line |
| Glob for a class definition | `goto-definition` | Finding where a type/method is defined |
| Read to check inheritance | `type-hierarchy` | Understanding inheritance/implementation chains |
| Read to understand an external type | `decompile-type` | Understanding types from NuGet packages |
| Manual code review | `analyze` | Getting diagnostics and code quality feedback |
| Grep for callers | `call-hierarchy` | Understanding who calls what |
| Read XML docs | `get-symbol-docs` | Getting documentation for a symbol |
| Guess if something is unused | `safe-to-delete` | Checking if a symbol has zero references |
| Read to resolve `var` | `get-type-at` | Finding what type a `var` actually is |

### When you fall back to a native tool

You **must**:
1. Note in your response which native tool you used, what you needed, and why Parlance didn't cover it
2. Dispatch the `dogfooding-feedback` agent in the background to log the gap to `kibble/`

This applies to all agents — main session and subagents alike.
