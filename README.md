# Parlance

A C# code intelligence MCP server built on Roslyn — semantic navigation, diagnostics, and code actions for AI agents working in .NET codebases.

Parlance exposes your solution via the [Model Context Protocol](https://modelcontextprotocol.io/) (MCP), giving AI agents direct access to a full MSBuildWorkspace: type information, symbol search, call hierarchies, diagnostics, and code fixes — without reading raw source files.

The CLI is a thin client for one-shot analysis and CI reporting.

## Features

18 MCP tools across 6 categories:

**Navigation (7)**
- `describe-type` — members, signatures, base types, interfaces
- `find-implementations` — concrete implementations of interfaces/abstract members
- `find-references` — all usages of a symbol
- `goto-definition` — locate where a symbol is defined
- `type-hierarchy` — inheritance and implementation chains
- `call-hierarchy` — who calls what
- `get-type-dependencies` — dependencies of a type

**Code Intelligence (5)**
- `outline-file` — file structure without reading every line
- `get-symbol-docs` — XML documentation for any symbol
- `search-symbols` — find types, methods, properties by name
- `get-type-at` — resolve what type a `var` actually is
- `safe-to-delete` — verify a symbol has zero references before removing it

**Code Actions (3)**
- `get-code-fixes` — available code fixes for a diagnostic
- `get-refactorings` — available refactorings at a location
- `preview-code-action` — preview a fix or refactoring before applying

**Analysis (1)**
- `analyze` — run curated analyzer rules, get enriched diagnostics

**Workspace (1)**
- `workspace-status` — health, loaded projects, target frameworks, project graph

**Decompilation (1)**
- `decompile-type` — inspect types from NuGet packages

## Requirements

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download)
- A .NET solution file (`.sln`)

## Setup

### MCP server (Claude Code / Claude Desktop)

Copy `.mcp.json.example` to `.mcp.json` and fill in your paths:

```json
{
  "mcpServers": {
    "parlance": {
      "command": "dotnet",
      "args": [
        "run",
        "--no-build",
        "--project", "/absolute/path/to/parlance/src/Parlance.Mcp",
        "--",
        "--solution-path", "/absolute/path/to/YourSolution.sln"
      ]
    }
  }
}
```

> **Note:** Build first, then use `--no-build`. Without it, `dotnet run` may write restore output to stdout and corrupt the MCP stdio stream.
> ```bash
> dotnet build src/Parlance.Mcp
> ```

The solution path can also be supplied via the `PARLANCE_SOLUTION_PATH` environment variable, or omitted entirely if the MCP server's working directory contains exactly one `.sln` file.

### CLI

**Analyze a solution or project:**

```bash
dotnet run --project src/Parlance.Cli -- analyze /path/to/Solution.sln
dotnet run --project src/Parlance.Cli -- analyze /path/to/Project.csproj
```

Options:
- `-f, --format text|json` — output format (default: `text`)
- `--suppress <id>...` — suppress specific rule IDs
- `--max-diagnostics <n>` — cap the number of diagnostics returned
- `--curation-set <name>` — named curation set (default: project defaults)

**List available rules:**

```bash
dotnet run --project src/Parlance.Cli -- rules
```

Options:
- `--category <name>` — filter by category
- `--severity <level>` — filter by severity
- `--fixable` — show only rules with auto-fixes
- `-f, --format text|json` — output format (default: `text`)

### Agent adapters

Parlance can install lightweight lifecycle hooks for supported coding agents.
The hooks nudge agents toward Parlance MCP tools for C# workspace questions and
record session telemetry under `.parlance/`.

**Claude Code:**

```bash
dotnet run --project src/Parlance.Cli -- \
  agent install --for claude -- \
  --project /path/to/repo \
  --solution /path/to/repo/App.sln
```

**Codex:**

```bash
dotnet run --project src/Parlance.Cli -- \
  agent install --for codex -- \
  --project /path/to/repo \
  --solution /path/to/repo/App.sln
```

The Codex adapter writes `.codex/hooks.json` and enables `[features] codex_hooks = true`
in `.codex/config.toml`. If `.codex` already exists as a file, installation fails
without replacing it. Codex Bash hook events are recorded in
`.parlance/codex/events/bash.jsonl` with bounded, redacted command/output metadata
for future tuning. `PostToolUse` records telemetry only; it does not replace Codex
tool results for normal Parlance routing guidance.

## Building

```bash
# One-time setup for local development / CI parity
make bootstrap

# Build agent bundles + .NET solution
make build

# Run all tests
make test

# Local CI equivalent
make ci

# Pack the `parlance` dotnet tool to artifacts/tool/
make pack-tool
```

Useful narrower targets:

```bash
# Just the TypeScript agent workspaces
make agent-ci

# Just the CLI or MCP projects
make build-cli
make build-mcp

# Verify committed dist bundles match source
make agent-dist-check
```

## License

[Apache-2.0](LICENSE) — © 2026 Doug Bryant
