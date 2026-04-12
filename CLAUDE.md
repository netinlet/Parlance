# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Test

```bash
# Build individual projects (not the full solution — Parlance.CSharp.Package is pack-only and breaks dotnet build)
dotnet build src/Parlance.Cli/Parlance.Cli.csproj
dotnet build src/Parlance.Mcp/Parlance.Mcp.csproj

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

**Note:** `dotnet build Parlance.sln` fails because `Parlance.CSharp.Package` has `NoBuild=true` (pack-only metapackage). Build individual projects instead.

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
├── Parlance.Mcp                MCP server — 15+ tools, stdio transport, Microsoft.Extensions.Hosting
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

Tools are static methods on `[McpServerToolType]` classes. All tools are `ReadOnly = true`. They receive services via DI parameters:

```csharp
[McpServerToolType]
public sealed class MyTool
{
    [McpServerTool(Name = "my-tool", ReadOnly = true)]
    [Description("...")]
    public static MyResult Execute(WorkspaceSessionHolder holder, WorkspaceQueryService query, ILogger<MyTool> logger)
    {
        using var _ = ToolDiagnostics.TimeToolCall(logger, "my-tool");
        // ...
    }
}
```

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
