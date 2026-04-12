# Tool Usage Analytics

> **Implementation note:** The delivered implementation diverges from this spec. The `TimeToolCall`/per-tool injection approach was replaced by an MCP `CallToolFilters` pipeline filter (`AnalyticsFilter`). `ToolAnalytics` exposes `RecordCall(toolName, elapsed, success, args)` and is called only from the filter — tools are analytics-free. Arguments are serialized as JSON (not `key=value` pairs). This spec reflects the original design intent; see `AnalyticsFilter.cs` and `ToolAnalytics.cs` for the actual implementation.

## Problem

Tool call timing data is logged to stderr at Debug level and disappears when the session ends. There is no persistent record of tool usage, performance, or parameters — making it impossible to track latency regressions, understand usage patterns, or debug past sessions.

## Solution

Evolve `ToolDiagnostics` from a static helper into a DI-registered `ToolAnalytics` singleton that writes per-call entries to a session-scoped log file, while preserving existing stderr logging.

## Design

### ToolAnalytics service

A sealed class registered as a DI singleton, replacing the static `ToolDiagnostics`:

- Injected with `ILogger<ToolAnalytics>` for stderr (preserves existing behavior) and a `StreamWriter` for the analytics file
- `TimeToolCall(string toolName, object? parameters = null)` returns an `IDisposable` that logs to both destinations on dispose
- The `parameters` argument accepts an anonymous object; properties are serialized as `key=value` pairs
- Implements `IAsyncDisposable` to flush and close the file on shutdown

### Log file location

- Default: `.parlance/logs/` relative to the solution directory
- Configurable via `--analytics-path` CLI arg or `PARLANCE_ANALYTICS_PATH` env var (same pattern as `--solution-path` / `PARLANCE_SOLUTION_PATH`)
- Filename: `session-{timestamp:yyyyMMdd-HHmmss}.log`
- The service creates the directory if it does not exist

### Entry format

Plain text, one line per tool call, pipe-delimited:

```
2026-04-08T14:32:01.123Z | describe-type | 45.2ms | OK | typeName=Parlance.Mcp.ToolDiagnostics
2026-04-08T14:32:03.456Z | find-references | 120.8ms | OK | symbolName=TimeToolCall, filePath=/src/Parlance.Mcp/ToolDiagnostics.cs
2026-04-08T14:32:05.789Z | search-symbols | 12.1ms | OK | searchQuery=Handler, kind=method
2026-04-08T14:32:07.000Z | workspace-status | 8.3ms | OK |
```

Fields: `timestamp | tool-name | elapsed | status | parameters`

- Timestamp: UTC ISO 8601 with milliseconds
- Elapsed: formatted as `{n}ms` with one decimal place
- Status: `OK` or `Error`
- Parameters: comma-separated `key=value` pairs from the anonymous object, empty if none provided

### Tool callsite change

Every tool changes from:

```csharp
// Before — static, tool passes its own logger
using var _ = ToolDiagnostics.TimeToolCall(logger, "describe-type");
```

To:

```csharp
// After — DI-injected, service owns its own logger
using var _ = toolAnalytics.TimeToolCall("describe-type", new { typeName });
```

Each `[McpServerToolType]` class receives `ToolAnalytics` as a DI parameter instead of using the static class.

### Configuration

`ParlanceMcpConfiguration` gains a new `AnalyticsPath` property:

- Parsed from `--analytics-path` CLI arg or `PARLANCE_ANALYTICS_PATH` env var
- Default: `Path.Combine(Path.GetDirectoryName(solutionPath), ".parlance", "logs")`

### DI registration

In `Program.cs`:

```csharp
builder.Services.AddSingleton<ToolAnalytics>();
```

The service reads `ParlanceMcpConfiguration` from DI to determine the log path and opens the file on first use or construction.

### Error handling

Analytics is non-critical. If the log file cannot be created or written to (permissions, disk full, invalid path), the service logs a warning to stderr and continues operating without file logging. Tool calls are never blocked or failed by analytics errors.

### What stays the same

- stderr logging at Debug level (now owned by `ToolAnalytics`, not each tool)
- `using var` dispose pattern at each callsite
- All tools remain `ReadOnly = true` static methods

### What is explicitly out of scope

- Session summary on shutdown
- Aggregation or querying of log files
- Result-level logging (level C) — extensible later via a `.WithResult()` method on the disposable
- Log rotation or retention — one file per session, user manages cleanup

## Files to change

- `src/Parlance.Mcp/ToolDiagnostics.cs` — replace with `ToolAnalytics.cs`
- `src/Parlance.Mcp/ParlanceMcpConfiguration.cs` — add `AnalyticsPath` property and parsing
- `src/Parlance.Mcp/Program.cs` — register `ToolAnalytics` as singleton
- `src/Parlance.Mcp/Tools/*.cs` — every tool file: swap static call to injected service, add parameters
- `tests/Parlance.Mcp.Tests/` — tests for the new service
