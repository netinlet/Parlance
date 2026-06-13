# Parlance

A C# code intelligence MCP server built on Roslyn — semantic navigation, diagnostics, and code actions for AI agents working in .NET codebases.

Parlance exposes your solution via the [Model Context Protocol](https://modelcontextprotocol.io/) (MCP), giving AI agents direct access to a full MSBuildWorkspace: type information, symbol search, call hierarchies, diagnostics, and code fixes — without reading raw source files.

The CLI is a thin client for one-shot analysis and CI reporting.

## Features

21 MCP tools across 7 categories:

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

**Code Actions (4)**
- `get-code-fixes` — available code fixes for a diagnostic
- `get-refactorings` — available refactorings at a location
- `preview-code-action` — preview a fix or refactoring before applying
- `apply-code-action` — return the complete, applyable edit (LSP `WorkspaceEdit`) for a fix or refactoring; the agent persists it (Parlance never writes to disk)

**Analysis (1)**
- `analyze` — run curated analyzer rules, get enriched diagnostics

**Live Editing (2)**
- `sync-buffer` — overlay unsaved buffer text so analysis/navigation reflect an edit, without writing to disk
- `close-buffer` — drop the overlay and revert the file to its on-disk contents

**Workspace (1)**
- `workspace-status` — health, loaded projects, target frameworks, project graph

**Decompilation (1)**
- `decompile-type` — inspect types from NuGet packages

### Live editing & versioning

Parlance steals the *concepts* of a language server without the LSP wire. Agents
push unsaved buffers and read back version-stamped results, so an edit can be
analyzed before it ever touches disk:

- **Buffer overlay.** `sync-buffer` applies full-text buffer replacement in
  memory; the overlay wins over disk until `close-buffer` reverts it. Disk is
  never written. `sync-buffer` / `close-buffer` are the only non–read-only tools.
- **Compute edits, never write them.** `apply-code-action` is the LSP
  `workspace/applyEdit` half of the preview/apply split: it returns a complete,
  machine-applyable `WorkspaceEdit` — ordered per-file text edits plus
  create/delete/rename resource operations — stamped with the snapshot it was
  computed against (pass `expectedSnapshotVersion` to get `stale` instead of an
  outdated edit). The loop is `get-code-fixes`/`get-refactorings` →
  `preview-code-action` (look) → `apply-code-action` (get the edit) → **the agent
  applies and saves it with its own tools** → the file watcher (or a
  `sync-buffer`) re-syncs Parlance. Parlance never writes to disk.
- **Version stamping.** Every tool result carries a `snapshotVersion`; open
  buffers also get a per-document version. Pass `expectedSnapshotVersion` to
  `analyze` for a best-effort staleness check — a mismatch yields status `stale`
  (never a hard error), with the actual version always stamped.
- **Pull-based diagnostics.** After an edit, call `analyze`; there are no push
  notifications.
- **Compact payloads.** Output paths are emitted workspace-relative;
  `find-references` snippets are opt-in via `includeSnippets` (default off).

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
without replacing it. The `--solution` path is used to write
`.parlance/codex/mcp-setup.md`, which contains the `codex mcp add parlance -- parlance mcp --solution-path ...`
command to run in your Codex shell after hook installation. Codex Bash hook
events are recorded in `.parlance/codex/events/bash.jsonl` with bounded,
redacted command/output metadata for future tuning. `PostToolUse` records
telemetry only; it does not replace Codex tool results for normal Parlance
routing guidance.

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
