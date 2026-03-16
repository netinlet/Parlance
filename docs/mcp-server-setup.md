# Parlance MCP Server Setup

Parlance exposes its workspace analysis capabilities via the [Model Context Protocol](https://modelcontextprotocol.io/) (MCP). This allows Claude and other MCP-compatible AI assistants to query workspace status, project structure, and (in future milestones) run diagnostics and navigate code.

## Prerequisites

- .NET 10.0 SDK
- A .NET solution file (`.sln`)

## Running the Server

### Via `dotnet run`

```bash
dotnet run --project src/Parlance.Mcp -- --solution-path /path/to/YourSolution.sln
```

### Via built executable

```bash
dotnet build src/Parlance.Mcp -c Release
./src/Parlance.Mcp/bin/Release/net10.0/Parlance.Mcp --solution-path /path/to/YourSolution.sln
```

## Claude Code Configuration

Add to your `claude_desktop_config.json`:

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

Or using a pre-built executable:

```json
{
  "mcpServers": {
    "parlance": {
      "command": "/absolute/path/to/parlance/src/Parlance.Mcp/bin/Release/net10.0/Parlance.Mcp",
      "args": [
        "--solution-path", "/absolute/path/to/YourSolution.sln"
      ]
    }
  }
}
```

## Command-Line Options

| Option | Env Var | Description |
|--------|---------|-------------|
| `--solution-path <path>` | `PARLANCE_SOLUTION_PATH` | Path to the .sln file (required) |
| `--log-level <level>` | — | Minimum log level: Trace, Debug, Information (default), Warning, Error |

The `--solution-path` argument takes precedence over the environment variable.

## Available Tools

### `workspace-status`

Returns workspace health, loaded projects, target frameworks, language versions, and project dependencies.

Example response:

```json
{
  "status": "Loaded",
  "solutionPath": "/path/to/Parlance.sln",
  "snapshotVersion": 1,
  "projectCount": 7,
  "projects": [
    {
      "name": "Parlance.Abstractions",
      "path": "/path/to/src/Parlance.Abstractions/Parlance.Abstractions.csproj",
      "status": "Loaded",
      "targetFramework": "net10.0",
      "targetFrameworks": ["net10.0"],
      "langVersion": "13.0",
      "dependsOn": []
    }
  ],
  "diagnostics": []
}
```

## Troubleshooting

### Logs

All server logs go to **stderr** (stdout is reserved for the MCP protocol). To see logs:

```bash
dotnet run --project src/Parlance.Mcp -- --solution-path /path/to/Solution.sln --log-level Debug 2>parlance.log
```

### Common Issues

- **"Solution file not found"**: Verify the path is absolute and the file exists.
- **Workspace status "Degraded"**: Some projects failed to load. Check the `diagnostics` array in the workspace-status response.
- **No output on stdout**: This is correct behavior. The server communicates via JSON-RPC on stdout. Human-readable logs go to stderr.
