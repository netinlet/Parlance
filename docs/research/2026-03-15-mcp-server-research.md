# Milestone 2 Research: MCP Server for Parlance

> Research conducted 2026-03-15 to inform the Milestone 2 implementation plan.

---

## 1. Transport Evaluation

### Recommendation: Use the official `ModelContextProtocol` NuGet SDK

**Package:** `ModelContextProtocol` v1.1.0 (released 2026-03-06)
**Repo:** github.com/modelcontextprotocol/csharp-sdk (4.1k stars, Apache-2.0)
**Downloads:** 4.8M total
**Authors:** ModelContextProtocol (Anthropic + Microsoft collaboration)

### SDK Package Structure

| Package | Purpose | When to use |
|---------|---------|-------------|
| `ModelContextProtocol.Core` | Minimal client/server APIs | Only if you need bare-bones, no DI |
| `ModelContextProtocol` | Hosting + DI extensions | **Use this for Parlance** (stdio server) |
| `ModelContextProtocol.AspNetCore` | HTTP/SSE transport | Add later for HTTP transport |

All packages target netstandard2.0, net8.0, net9.0, **net10.0** — matches Parlance's TFM.

### I/O Model Under the Hood

**stdio transport** (`StdioServerTransport` extends `StreamServerTransport`):
- Uses raw `System.IO.Stream` — not System.IO.Pipelines, not Kestrel
- Wraps `Console.OpenStandardInput()` / `Console.OpenStandardOutput()` with `StreamReader`/`BufferedStream`
- Messages are newline-delimited JSON-RPC over raw byte streams
- A `CancellableStdinStream` wrapper adds cancellation support (neither `WindowsConsoleStream` nor `UnixConsoleStream` respect CancellationTokens natively)
- Inbound messages queued via `System.Threading.Channels.Channel<JsonRpcMessage>`
- Outbound writes protected by `SemaphoreSlim` for thread safety

**Streamable HTTP transport** (`ModelContextProtocol.AspNetCore`):
- Kestrel-based ASP.NET Core middleware via `StreamableHttpHandler`
- SSE for server-to-client streaming, HTTP POST for client-to-server
- Mapped via `app.MapMcp()`
- Multi-session by design with `StatefulSessionManager`
- `IdleTrackingBackgroundService` cleans up idle sessions (default 2hr timeout)

**Transport abstraction:**
```csharp
public interface ITransport : IAsyncDisposable
{
    string? SessionId { get; }
    ChannelReader<JsonRpcMessage> MessageReader { get; }
    Task SendMessageAsync(JsonRpcMessage message, CancellationToken ct = default);
}
```

**For testing:** `StreamServerTransport` accepts any `Stream` pair. `InMemoryTransport` uses `System.IO.Pipelines.Pipe` for in-process client-server pairs — no process spawning needed.

### Cross-Platform Assessment

No known Linux/WSL2 issues. The stdio transport uses standard .NET Console APIs that work identically across platforms. The `CancellableStdinStream` wrapper specifically addresses platform differences in stdin cancellation.

### Performance Assessment

The raw Stream approach is adequate for MCP's message sizes and frequency. MCP is a request-response protocol with human-scale latency — the transport is not the bottleneck. If performance ever matters, the `ITransport` abstraction allows swapping in a Pipelines-based implementation without changing tool code.

**Verdict: Use the SDK as-is. No custom transport needed.**

---

## 2. Hosting Model

### Generic Host (stdio) — Recommended for Parlance

The SDK integrates with `Microsoft.Extensions.Hosting` via `Host.CreateApplicationBuilder`:

```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<WorkspaceTools>();
await builder.Build().RunAsync();
```

- `WithStdioServerTransport()` registers a `SingleSessionMcpServerHostedService` as a `BackgroundService`
- It calls `McpServer.RunAsync()` and signals `IHostApplicationLifetime.StopApplication()` when the session ends
- The generic host handles SIGTERM/SIGINT gracefully via `IHostApplicationLifetime`
- When `RunAsync()` completes (stdin EOF, transport disconnect, or cancellation), shutdown is automatic

### ASP.NET Core (HTTP) — Future

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<WorkspaceTools>();
var app = builder.Build();
app.MapMcp();
app.Run();
```

No tool code changes needed to switch from stdio to HTTP — only startup wiring differs.

### Standalone (no DI)

Available via `McpServer.Create()` for manual wiring, but not recommended for Parlance.

---

## 3. Tool Registration

### Attribute-Based (Primary Pattern)

```csharp
[McpServerToolType]
public sealed class WorkspaceTools
{
    [McpServerTool(Name = "workspace-status", ReadOnly = true)]
    [Description("Returns workspace health, loaded projects, and configuration")]
    public static WorkspaceStatusResult GetStatus(CSharpWorkspaceSession session)
    {
        // session is injected from DI
        return new WorkspaceStatusResult(
            session.Health.Status,
            session.Projects,
            session.SnapshotVersion);
    }
}
```

**`[McpServerTool]` properties:**
- `Name` — tool name (overrides method name)
- `Title` — human-readable display name
- `ReadOnly` — no side effects (default false)
- `Destructive` — might be destructive (default true)
- `Idempotent` — same args = same result (default false)
- `OpenWorld` — interacts with external entities (default true)

**Parameter binding is automatic:**
- Regular parameters → deserialized from `CallToolRequestParams.Arguments` (JSON)
- `CancellationToken` → auto-bound to request cancellation
- `McpServer` → auto-bound to server instance
- `IServiceProvider` → auto-bound from DI container
- `IProgress<ProgressNotificationValue>` → auto-bound for progress reporting
- Any DI-resolvable type → auto-resolved from container
- `[Description]` on parameters → generates JSON Schema descriptions for the AI

**Return type mapping:**
- `string` → `TextContentBlock`
- `CallToolResult` → direct pass-through
- `ContentBlock` / `IEnumerable<ContentBlock>` → direct
- Any other type → JSON-serialized to text content

**This means:** Tool methods can directly accept the workspace session (or a wrapper) via DI injection. Parameters from the AI are deserialized from JSON automatically. Return types are serialized to MCP content blocks automatically.

### Alternative Registration

```csharp
// Assembly scanning
builder.Services.AddMcpServer()
    .WithToolsFromAssembly();  // scans for [McpServerToolType] classes

// Manual/fluent
McpServerTool.Create((string arg) => $"Echo: {arg}", new() { Name = "echo" })

// Dynamic handlers (full control)
builder.Services.AddMcpServer()
    .WithListToolsHandler(handler)
    .WithCallToolHandler(handler);
```

---

## 4. MCP Protocol Key Facts

### Protocol Version

Current: `2025-11-25` (date-based versioning, not semver).

### Message Format

JSON-RPC 2.0 over UTF-8. Three message types:
- **Requests** — `{ jsonrpc, id, method, params? }`
- **Responses** — `{ jsonrpc, id, result }` or `{ jsonrpc, id?, error: { code, message, data? } }`
- **Notifications** — `{ jsonrpc, method, params? }` (no id, fire-and-forget)

### Session Lifecycle

1. **Client sends `initialize`** with `protocolVersion`, `capabilities`, `clientInfo`
2. **Server responds** with `protocolVersion`, `capabilities`, `serverInfo`, optional `instructions`
3. **Client sends `notifications/initialized`** (notification, no response)
4. **Normal operation** — tool calls, resource reads, etc.
5. **Shutdown** — stdio: client closes stdin, SIGTERM, SIGKILL. HTTP: DELETE to endpoint.

### Server Capabilities (declared during init)

| Capability | Description |
|---|---|
| `tools` | Exposes callable tools. `listChanged`: emits notifications when list changes |
| `resources` | Provides readable resources. `subscribe`: per-resource change notifications |
| `prompts` | Offers prompt templates |
| `logging` | Emits structured log messages |
| `completions` | Supports argument autocompletion |

**For Parlance M2:** Declare `tools` and `logging` capabilities only.

### Tool Protocol

**Listing:** `tools/list` → paginated list of tool definitions with JSON Schema `inputSchema`
**Calling:** `tools/call` → `{ name, arguments }` → `{ content: [...], isError: false }`

**Error handling — two levels:**
1. Protocol errors (unknown tool, malformed request) → JSON-RPC error response
2. Tool execution errors → `{ content: [...], isError: true }` — designed for LLM self-correction

### Notifications

| Notification | Direction | Purpose |
|---|---|---|
| `notifications/progress` | Either | Progress updates with token, progress, total |
| `notifications/message` | Server→Client | Log messages (debug through emergency) |
| `notifications/tools/list_changed` | Server→Client | Tool list changed |
| `notifications/cancelled` | Either | Cancel in-progress request |

### stdio Transport Details

- Server reads from **stdin**, writes to **stdout**
- Messages are **newline-delimited** (one JSON object per line, no embedded newlines)
- Server MAY write to **stderr** for logging/diagnostics
- Server MUST NOT write non-MCP content to stdout

---

## 5. SDK Dependencies

**`ModelContextProtocol` v1.1.0 on net10.0 pulls in:**
- `ModelContextProtocol.Core` (>= 1.1.0)
- `Microsoft.Extensions.Caching.Abstractions` (>= 10.0.3)
- `Microsoft.Extensions.Hosting.Abstractions` (>= 10.0.3)

**`ModelContextProtocol.Core` on net10.0 pulls in:**
- `Microsoft.Extensions.AI.Abstractions` (>= 10.3.0)
- `Microsoft.Extensions.Logging.Abstractions` (>= 10.0.3)

The `Microsoft.Extensions.AI.Abstractions` dependency brings AI content types (`AIContent`, `ChatMessage`, etc.) used for tool result marshalling. This is the only potentially surprising dependency — it's lightweight.

**AOT compatible:** `IsAotCompatible=true` on net10.0. Generic `WithTools<T>()` is AOT-safe; `WithToolsFromAssembly()` is not.

---

## 6. Logging Integration

**Full `Microsoft.Extensions.Logging` support.**

Two logging directions:

**A) Server-side (standard .NET logging):**
```csharp
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;  // IMPORTANT for stdio
});
```
For stdio: logs MUST go to **stderr** (stdout is the protocol channel).

**B) MCP client-directed logging:**
The server can forward logs to the connected MCP client as notifications via `McpServer.AsClientLoggerProvider()`. The client can set the minimum level via `logging/setLevel`.

**For Parlance:** Use stderr logging for development/debugging. Consider exposing client-directed logging for Claude to see workspace events.

---

## 7. Existing Codebase State (What M2 Builds On)

### Parlance.Abstractions — The Normalized Result Layer

Current public API surface:
- `IAnalysisEngine` — `AnalyzeSourceAsync(sourceCode, options?, ct)` → `Task<AnalysisResult>`
- `AnalysisResult` — `ImmutableList<Diagnostic> Diagnostics`, `AnalysisSummary Summary`
- `Diagnostic` — `RuleId, Category, Severity, Message, Location, Rationale?, SuggestedFix?`
- `AnalysisSummary` — `TotalDiagnostics, Errors, Warnings, Suggestions, ByCategory, IdiomaticScore`
- `AnalysisOptions` — `SuppressRules, MaxDiagnostics, IncludeFixSuggestions, LanguageVersion`
- `Location` — `Line, Column, EndLine, EndColumn, FilePath?` (FilePath added in M1)
- `DiagnosticSeverity` — `Error, Warning, Suggestion`

**Key observation:** Abstractions is language-neutral and deals only with normalized results. The workspace is not abstracted here — by design, the host/engine boundary (`IWorkspaceSession`) is deferred to M2.

### CSharpWorkspaceSession — The Engine API (M1 Complete)

**Entry points:**
```csharp
CSharpWorkspaceSession.OpenSolutionAsync(solutionPath, options?, ct)
CSharpWorkspaceSession.OpenProjectAsync(projectPath, options?, ct)
```

**Public surface:**
- `string WorkspacePath`
- `long SnapshotVersion` — monotonically increasing, starts at 1
- `CSharpWorkspaceHealth Health` — status + project list + diagnostics
- `ImmutableList<CSharpProjectInfo> Projects`
- `CSharpProjectInfo? GetProject(WorkspaceProjectKey key)`
- `CSharpProjectInfo? GetProjectByPath(string projectPath)`
- `Task RefreshAsync(ct)` — Server mode only
- `ValueTask DisposeAsync()`

**Configuration:** `WorkspaceOpenOptions(Mode, EnableFileWatching?, LoggerFactory?)`

**Health model:** `WorkspaceLoadStatus.Loaded | Degraded | Failed`, per-project `CSharpProjectInfo` with `ProjectLoadStatus.Loaded | Failed`, structured `WorkspaceDiagnostic(Code, Message, Severity)`.

### CLI Architecture (What Gets Replaced in M5)

The CLI currently uses the older `CSharpAnalysisEngine` (snippet-based, not workspace-aware):
1. Parse paths → collect `.cs` files
2. `WorkspaceAnalyzer.AnalyzeAsync(files, ...)` builds synthetic compilation
3. Format output (text or JSON)

**Not connected to `CSharpWorkspaceSession` yet.** That's intentional — CLI becomes a thin client in M5.

### Solution Structure

```
src/
  Parlance.Abstractions/           (interfaces, models — language-neutral)
  Parlance.CSharp/                 (analysis engine — snippet-based)
  Parlance.CSharp.Analyzers/       (8 Roslyn analyzers, netstandard2.0)
  Parlance.CSharp.Workspace/       (NEW — MSBuildWorkspace engine)
  Parlance.Cli/                    (System.CommandLine 2.0.3)
  Parlance.Analyzers.Upstream/     (NetAnalyzers + Roslynator loader)
  Parlance.CSharp.Package/         (NuGet metapackage)
tests/
  (corresponding test projects)
```

Shared version 0.1.0 via `Directory.Build.props`.

---

## 8. Design Implications for M2

### What the MCP Server Needs to Do

1. **Hold a `CSharpWorkspaceSession`** opened in Server mode with file watching
2. **Register it in DI** so tool methods can receive it via parameter injection
3. **Expose `workspace-status` as the first MCP tool** — return health, projects, snapshot version
4. **Route logs to stderr** for stdio transport
5. **Graceful shutdown** — dispose the workspace session when the host shuts down

### Host/Engine Boundary Question

The roadmap says to design `IWorkspaceSession` in Abstractions during M2. The SDK's DI model makes this straightforward — tool methods depend on an interface, the DI container provides the C# implementation:

```csharp
// In Abstractions (designed based on what the host actually needs)
public interface IWorkspaceSession : IAsyncDisposable
{
    string RootPath { get; }
    long SnapshotVersion { get; }
    WorkspaceHealth Health { get; }
    // ... only what the MCP tools actually query
}

// In DI setup
builder.Services.AddSingleton<IWorkspaceSession>(session);
```

However, per the roadmap: "Design the shared `IWorkspaceSession` or equivalent interface in Abstractions based on what the host actually needs." This means we should **build the MCP server first with concrete types**, then extract the interface once we see the real usage patterns. Don't design the interface speculatively.

### workspace-status Tool Shape

The tool should return compact, structured, LLM-optimized JSON:

```json
{
  "status": "Loaded",
  "solutionPath": "/path/to/Parlance.sln",
  "snapshotVersion": 1,
  "projectCount": 7,
  "projects": [
    {
      "name": "Parlance.Abstractions",
      "path": "src/Parlance.Abstractions/Parlance.Abstractions.csproj",
      "status": "Loaded",
      "targetFramework": "net10.0",
      "langVersion": "13.0"
    }
  ],
  "diagnostics": []
}
```

Key: compact enough for context window, includes all info Claude needs to decide what to do next.

### Parlance.Mcp Project Structure

```
src/Parlance.Mcp/
├── Parlance.Mcp.csproj          # net10.0, refs ModelContextProtocol + Workspace
├── Program.cs                   # Host setup, DI, transport selection
└── Tools/
    └── WorkspaceStatusTool.cs   # First MCP tool
```

### Testing Strategy

The SDK's `StreamServerTransport` accepts any `Stream` pair, enabling in-process integration tests without spawning processes:

```csharp
var (clientTransport, serverTransport) = InMemoryTransport.CreatePair();
// Create server with serverTransport, client with clientTransport
// Call tools directly in-process
```

### What's NOT in M2

- Semantic navigation tools (M3)
- Analyzer execution over workspace (M4)
- CLI refactor (M5)
- `IWorkspaceSession` interface extraction (defer until usage patterns are clear)
- HTTP transport (add later with `ModelContextProtocol.AspNetCore`)

---

## 9. Risk Assessment

| Risk | Mitigation |
|------|------------|
| SDK v1.1.0 is young (2 weeks old) | Stable API, 4.8M downloads across versions, Microsoft co-authored |
| stdio logging conflicts | SDK documents the pattern: route all logs to stderr |
| Workspace session lifecycle | Register as singleton, dispose on host shutdown via `IHostApplicationLifetime` |
| MSBuildLocator + Generic Host interaction | MSBuildLocator.RegisterDefaults() must run before any MSBuild types load — call in Program.cs before `builder.Build()` |
| Large solution load time | Workspace loads lazily (compilation on demand). First MCP tool call after startup may be slow if it triggers compilation |

---

## 10. References

- MCP Specification (2025-11-25): modelcontextprotocol.io/specification/2025-11-25
- C# SDK GitHub: github.com/modelcontextprotocol/csharp-sdk
- C# SDK NuGet: nuget.org/packages/ModelContextProtocol (v1.1.0)
- C# SDK API Docs: csharp.sdk.modelcontextprotocol.io
- MCP TypeScript Schema (source of truth): github.com/modelcontextprotocol/specification/blob/main/schema/2025-11-25/schema.ts
- Parlance Roadmap: docs/roadmap/2026-03-14-ide-for-ai-roadmap.md
- Parlance Workspace Design Spec: docs/superpowers/specs/2026-03-14-parlance-workspace-design.md
