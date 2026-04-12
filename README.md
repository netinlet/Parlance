# Parlance

**An opinionated C# code intelligence server for AI agents, built on Roslyn.**

Parlance exposes semantic understanding of your .NET codebase via the [Model Context Protocol](https://modelcontextprotocol.io/) (MCP). Where raw Roslyn gives you a compiler API, Parlance gives you a senior developer — curation, scoring, fix safety classification, and idiomatic direction on top of a full MSBuildWorkspace.

The primary consumer is the AI agent. The CLI is a thin client for CI and one-shot reporting.

## Features

18 MCP tools covering:

**Semantic navigation**
- `describe-type` — members, signatures, base types, interfaces
- `search-symbols` — find types, methods, properties by name
- `goto-definition` — locate where a symbol is defined
- `find-references` — all usages of a symbol
- `find-implementations` — concrete implementations of interfaces/abstract members
- `type-hierarchy` — inheritance and implementation chains
- `call-hierarchy` — who calls what
- `outline-file` — file structure without reading every line
- `get-type-at` — resolve what type a `var` actually is
- `get-symbol-docs` — XML documentation for any symbol
- `get-type-dependencies` — dependencies of a type
- `decompile-type` — inspect types from NuGet packages

**Diagnostics & fixes**
- `analyze` — run curated analyzer rules, get enriched diagnostics
- `get-code-fixes` — available code fixes for a diagnostic
- `get-refactorings` — available refactorings at a location
- `preview-code-action` — preview a fix or refactoring before applying

**Safety**
- `safe-to-delete` — verify a symbol has zero references before removing it

**Workspace**
- `workspace-status` — health, loaded projects, target frameworks, project graph

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
        "--project", "/absolute/path/to/parlance/src/Parlance.Mcp",
        "--",
        "--solution-path", "/absolute/path/to/YourSolution.sln"
      ]
    }
  }
}
```

> **Note:** Always build before running to avoid build output corrupting the MCP stdio stream:
> ```bash
> dotnet build src/Parlance.Mcp
> ```
> Then add `--no-build` to the args above.

### CLI

```bash
# Analyze a solution
dotnet run --project src/Parlance.Cli -- analyze /path/to/Solution.sln

# List available rules
dotnet run --project src/Parlance.Cli -- rules /path/to/Solution.sln
```

## Building

```bash
# Build individual projects (dotnet build Parlance.sln fails — Parlance.CSharp.Package is pack-only)
dotnet build src/Parlance.Cli/Parlance.Cli.csproj
dotnet build src/Parlance.Mcp/Parlance.Mcp.csproj

# Run tests
dotnet test Parlance.sln

# Check formatting
dotnet format Parlance.sln --verify-no-changes
```

## License

[Apache-2.0](LICENSE) — © Netinlet
