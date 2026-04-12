# Tool Usage Analytics Implementation Plan

> **Implementation note:** The delivered implementation diverges from this plan. The per-tool `TimeToolCall` injection approach was replaced by an MCP `CallToolFilters` pipeline filter (`AnalyticsFilter`). `ToolAnalytics` exposes `RecordCall(toolName, elapsed, success, args)` — tools receive no analytics dependency. Arguments are serialized as JSON via the filter. This plan reflects the original approach; see `docs/superpowers/plans/2026-04-11-analytics-filter.md` for the refactor that produced the final design.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the static `ToolDiagnostics` class with a DI-registered `ToolAnalytics` service that logs tool calls (with parameters) to a per-session file while preserving stderr logging.

**Architecture:** `ToolAnalytics` is a singleton service that owns both an `ILogger` for stderr and a `StreamWriter` for the analytics file. Each tool receives it via DI and calls `TimeToolCall("tool-name", new { param1, param2 })`. The disposable timer writes a pipe-delimited line on dispose. Configuration adds `--analytics-path` / `PARLANCE_ANALYTICS_PATH` following the existing pattern.

**Tech Stack:** .NET 10, Microsoft.Extensions.Logging, Microsoft.Extensions.Hosting, xUnit

---

### File Map

| Action | File | Responsibility |
|--------|------|---------------|
| Create | `src/Parlance.Mcp/ToolAnalytics.cs` | Singleton service: timing, stderr + file logging, parameter serialization |
| Modify | `src/Parlance.Mcp/ParlanceMcpConfiguration.cs` | Add `AnalyticsPath` property and CLI/env parsing |
| Modify | `src/Parlance.Mcp/Program.cs` | Register `ToolAnalytics`, remove `ToolDiagnostics` usage |
| Delete | `src/Parlance.Mcp/ToolDiagnostics.cs` | Replaced by `ToolAnalytics` |
| Modify | `src/Parlance.Mcp/Tools/WorkspaceStatusTool.cs` | Swap to `ToolAnalytics` |
| Modify | `src/Parlance.Mcp/Tools/DescribeTypeTool.cs` | Swap to `ToolAnalytics` |
| Modify | `src/Parlance.Mcp/Tools/SearchSymbolsTool.cs` | Swap to `ToolAnalytics` |
| Modify | `src/Parlance.Mcp/Tools/AnalyzeTool.cs` | Swap to `ToolAnalytics` |
| Modify | `src/Parlance.Mcp/Tools/FindReferencesTool.cs` | Swap to `ToolAnalytics` |
| Modify | `src/Parlance.Mcp/Tools/FindImplementationsTool.cs` | Swap to `ToolAnalytics` |
| Modify | `src/Parlance.Mcp/Tools/GotoDefinitionTool.cs` | Swap to `ToolAnalytics` |
| Modify | `src/Parlance.Mcp/Tools/CallHierarchyTool.cs` | Swap to `ToolAnalytics` |
| Modify | `src/Parlance.Mcp/Tools/TypeHierarchyTool.cs` | Swap to `ToolAnalytics` |
| Modify | `src/Parlance.Mcp/Tools/SafeToDeleteTool.cs` | Swap to `ToolAnalytics` |
| Modify | `src/Parlance.Mcp/Tools/OutlineFileTool.cs` | Swap to `ToolAnalytics` |
| Modify | `src/Parlance.Mcp/Tools/GetTypeAtTool.cs` | Swap to `ToolAnalytics` |
| Modify | `src/Parlance.Mcp/Tools/GetSymbolDocsTool.cs` | Swap to `ToolAnalytics` |
| Modify | `src/Parlance.Mcp/Tools/GetCodeFixesTool.cs` | Swap to `ToolAnalytics` |
| Modify | `src/Parlance.Mcp/Tools/GetRefactoringsTool.cs` | Swap to `ToolAnalytics` |
| Modify | `src/Parlance.Mcp/Tools/PreviewCodeActionTool.cs` | Swap to `ToolAnalytics` |
| Modify | `src/Parlance.Mcp/Tools/DecompileTypeTool.cs` | Swap to `ToolAnalytics` |
| Modify | `src/Parlance.Mcp/Tools/GetTypeDependenciesTool.cs` | Swap to `ToolAnalytics` |
| Create | `tests/Parlance.Mcp.Tests/ToolAnalyticsTests.cs` | Unit tests for the analytics service |
| Modify | `tests/Parlance.Mcp.Tests/ParlanceMcpConfigurationTests.cs` | Tests for `AnalyticsPath` config |

---

### Task 1: Add AnalyticsPath to ParlanceMcpConfiguration

**Files:**
- Modify: `src/Parlance.Mcp/ParlanceMcpConfiguration.cs`
- Test: `tests/Parlance.Mcp.Tests/ParlanceMcpConfigurationTests.cs`

- [ ] **Step 1: Write failing tests for AnalyticsPath parsing**

Add these tests to `ParlanceMcpConfigurationTests.cs`:

```csharp
[Fact]
public void FromArgs_DefaultAnalyticsPath_IsRelativeToSolution()
{
    var solutionPath = GetSolutionPath();
    var config = ParlanceMcpConfiguration.FromArgs(["--solution-path", solutionPath]);

    var expected = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(solutionPath))!, ".parlance", "logs");
    Assert.Equal(expected, config.AnalyticsPath);
}

[Fact]
public void FromArgs_ExplicitAnalyticsPath_Overrides()
{
    var solutionPath = GetSolutionPath();
    var customPath = Path.Combine(Path.GetTempPath(), "my-analytics");
    var config = ParlanceMcpConfiguration.FromArgs(
        ["--solution-path", solutionPath, "--analytics-path", customPath]);

    Assert.Equal(Path.GetFullPath(customPath), config.AnalyticsPath);
}

[Fact]
public void FromArgs_AnalyticsPathFlagWithoutValue_ThrowsDescriptiveError()
{
    var solutionPath = GetSolutionPath();
    var ex = Assert.Throws<InvalidOperationException>(
        () => ParlanceMcpConfiguration.FromArgs(["--solution-path", solutionPath, "--analytics-path"]));

    Assert.Contains("--analytics-path requires a value", ex.Message);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Parlance.Mcp.Tests/Parlance.Mcp.Tests.csproj --filter "AnalyticsPath"`
Expected: FAIL — `AnalyticsPath` property does not exist

- [ ] **Step 3: Add AnalyticsPath to ParlanceMcpConfiguration**

In `ParlanceMcpConfiguration.cs`, change the record to:

```csharp
public sealed record ParlanceMcpConfiguration(
    string SolutionPath, string AnalyticsPath, LogLevel MinimumLogLevel = LogLevel.Information)
```

Update `FromArgs`:

```csharp
public static ParlanceMcpConfiguration FromArgs(string[] args)
{
    var solutionPath = GetSolutionPath(args);
    var logLevel = GetLogLevel(args);
    var fullSolutionPath = Path.GetFullPath(solutionPath);
    var analyticsPath = GetAnalyticsPath(args, fullSolutionPath);

    return new ParlanceMcpConfiguration(fullSolutionPath, analyticsPath, logLevel);
}
```

Add the `GetAnalyticsPath` method:

```csharp
private static string GetAnalyticsPath(string[] args, string fullSolutionPath)
{
    for (var i = 0; i < args.Length; i++)
    {
        if (args[i] is not "--analytics-path")
            continue;

        if (i + 1 >= args.Length)
            throw new InvalidOperationException("--analytics-path requires a value.");

        return Path.GetFullPath(args[i + 1]);
    }

    var envValue = Environment.GetEnvironmentVariable("PARLANCE_ANALYTICS_PATH");
    if (!string.IsNullOrWhiteSpace(envValue))
        return Path.GetFullPath(envValue);

    return Path.Combine(Path.GetDirectoryName(fullSolutionPath)!, ".parlance", "logs");
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Parlance.Mcp.Tests/Parlance.Mcp.Tests.csproj --filter "AnalyticsPath"`
Expected: PASS

- [ ] **Step 5: Run all config tests to check for regressions**

Run: `dotnet test tests/Parlance.Mcp.Tests/Parlance.Mcp.Tests.csproj --filter "ParlanceMcpConfiguration"`
Expected: PASS (all existing tests still pass since `AnalyticsPath` is auto-derived)

- [ ] **Step 6: Commit**

```bash
git add src/Parlance.Mcp/ParlanceMcpConfiguration.cs tests/Parlance.Mcp.Tests/ParlanceMcpConfigurationTests.cs
git commit -m "feat: add AnalyticsPath to ParlanceMcpConfiguration"
```

---

### Task 2: Create ToolAnalytics service

**Files:**
- Create: `src/Parlance.Mcp/ToolAnalytics.cs`
- Create: `tests/Parlance.Mcp.Tests/ToolAnalyticsTests.cs`

- [ ] **Step 1: Write failing tests for ToolAnalytics**

Create `tests/Parlance.Mcp.Tests/ToolAnalyticsTests.cs`:

```csharp
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Parlance.Mcp.Tests;

public sealed class ToolAnalyticsTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"parlance-test-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private ToolAnalytics CreateAnalytics() =>
        new(new ParlanceMcpConfiguration("/fake/path.sln", _tempDir), NullLoggerFactory.Instance);

    [Fact]
    public void TimeToolCall_WritesEntryToFile()
    {
        var analytics = CreateAnalytics();

        using (analytics.TimeToolCall("describe-type", new { typeName = "Foo" }))
        {
            // simulate work
        }

        analytics.Flush();

        var files = Directory.GetFiles(_tempDir, "session-*.log");
        Assert.Single(files);

        var content = File.ReadAllText(files[0]);
        Assert.Contains("describe-type", content);
        Assert.Contains("typeName=Foo", content);
        Assert.Contains("OK", content);
    }

    [Fact]
    public void TimeToolCall_NoParams_WritesEmptyParamsField()
    {
        var analytics = CreateAnalytics();

        using (analytics.TimeToolCall("workspace-status"))
        {
        }

        analytics.Flush();

        var files = Directory.GetFiles(_tempDir, "session-*.log");
        var lines = File.ReadAllLines(files[0]).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
        Assert.Single(lines);
        // Line ends with empty params: "... | OK |"
        Assert.EndsWith("|", lines[0].TrimEnd());
    }

    [Fact]
    public void TimeToolCall_MultipleParams_FormatsCorrectly()
    {
        var analytics = CreateAnalytics();

        using (analytics.TimeToolCall("search-symbols", new { searchQuery = "Handler", kind = "method", maxResults = 25 }))
        {
        }

        analytics.Flush();

        var files = Directory.GetFiles(_tempDir, "session-*.log");
        var content = File.ReadAllText(files[0]);
        Assert.Contains("searchQuery=Handler", content);
        Assert.Contains("kind=method", content);
        Assert.Contains("maxResults=25", content);
    }

    [Fact]
    public void TimeToolCall_NullParamValues_Skipped()
    {
        var analytics = CreateAnalytics();

        using (analytics.TimeToolCall("goto-definition", new { symbolName = "Foo", filePath = (string?)null }))
        {
        }

        analytics.Flush();

        var files = Directory.GetFiles(_tempDir, "session-*.log");
        var content = File.ReadAllText(files[0]);
        Assert.Contains("symbolName=Foo", content);
        Assert.DoesNotContain("filePath", content);
    }

    [Fact]
    public void CreatesDirectoryIfMissing()
    {
        var nested = Path.Combine(_tempDir, "nested", "deep");
        var analytics = new ToolAnalytics(
            new ParlanceMcpConfiguration("/fake/path.sln", nested), NullLoggerFactory.Instance);

        using (analytics.TimeToolCall("workspace-status"))
        {
        }

        analytics.Flush();

        Assert.True(Directory.Exists(nested));
        Assert.Single(Directory.GetFiles(nested, "session-*.log"));
    }

    [Fact]
    public void InvalidPath_DoesNotThrow_LogsDegraded()
    {
        // Use an invalid path that can't be created
        var analytics = new ToolAnalytics(
            new ParlanceMcpConfiguration("/fake/path.sln", "/\0invalid/path"), NullLoggerFactory.Instance);

        // Should not throw — analytics is non-critical
        using (analytics.TimeToolCall("workspace-status"))
        {
        }
    }

    [Fact]
    public void MultipleCalls_AllWrittenToSameFile()
    {
        var analytics = CreateAnalytics();

        using (analytics.TimeToolCall("describe-type", new { typeName = "A" })) { }
        using (analytics.TimeToolCall("find-references", new { symbolName = "B" })) { }
        using (analytics.TimeToolCall("workspace-status")) { }

        analytics.Flush();

        var files = Directory.GetFiles(_tempDir, "session-*.log");
        Assert.Single(files);
        var lines = File.ReadAllLines(files[0]).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
        Assert.Equal(3, lines.Length);
    }

    [Fact]
    public void EntryFormat_HasExpectedPipeDelimitedStructure()
    {
        var analytics = CreateAnalytics();

        using (analytics.TimeToolCall("analyze", new { files = "test.cs" })) { }

        analytics.Flush();

        var files = Directory.GetFiles(_tempDir, "session-*.log");
        var line = File.ReadAllLines(files[0]).First(l => !string.IsNullOrWhiteSpace(l));
        var parts = line.Split(" | ");
        Assert.Equal(5, parts.Length); // timestamp | tool | elapsed | status | params
        Assert.Contains("analyze", parts[1]);
        Assert.EndsWith("ms", parts[2].Trim());
        Assert.Equal("OK", parts[3].Trim());
        Assert.Contains("files=test.cs", parts[4]);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Parlance.Mcp.Tests/Parlance.Mcp.Tests.csproj --filter "ToolAnalytics"`
Expected: FAIL — `ToolAnalytics` does not exist

- [ ] **Step 3: Implement ToolAnalytics**

Create `src/Parlance.Mcp/ToolAnalytics.cs`:

```csharp
using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.Logging;

namespace Parlance.Mcp;

internal sealed class ToolAnalytics : IAsyncDisposable
{
    private readonly ILogger<ToolAnalytics> _logger;
    private readonly StreamWriter? _writer;

    public ToolAnalytics(ParlanceMcpConfiguration configuration, ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<ToolAnalytics>();

        try
        {
            Directory.CreateDirectory(configuration.AnalyticsPath);
            var fileName = $"session-{DateTime.UtcNow:yyyyMMdd-HHmmss}.log";
            var filePath = Path.Combine(configuration.AnalyticsPath, fileName);
            _writer = new StreamWriter(filePath, append: false) { AutoFlush = false };
            _logger.LogInformation("Analytics logging to {Path}", filePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to open analytics log file at {Path}. Analytics will be stderr-only",
                configuration.AnalyticsPath);
            _writer = null;
        }
    }

    public IDisposable TimeToolCall(string toolName, object? parameters = null)
    {
        var startTimestamp = Stopwatch.GetTimestamp();
        _logger.LogDebug("Tool call started: {ToolName}", toolName);
        return new ToolTimer(this, toolName, parameters, startTimestamp);
    }

    public void Flush() => _writer?.Flush();

    public async ValueTask DisposeAsync()
    {
        if (_writer is not null)
            await _writer.DisposeAsync();
    }

    private void WriteEntry(string toolName, object? parameters, TimeSpan elapsed, bool success)
    {
        _logger.LogDebug("Tool call completed: {ToolName} in {ElapsedMs:F1}ms", toolName, elapsed.TotalMilliseconds);

        if (_writer is null) return;

        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        var elapsedStr = $"{elapsed.TotalMilliseconds:F1}ms";
        var status = success ? "OK" : "Error";
        var paramsStr = FormatParameters(parameters);

        try
        {
            _writer.WriteLine($"{timestamp} | {toolName} | {elapsedStr} | {status} | {paramsStr}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write analytics entry for {ToolName}", toolName);
        }
    }

    private static string FormatParameters(object? parameters)
    {
        if (parameters is null) return "";

        var props = parameters.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var parts = new List<string>();
        foreach (var prop in props)
        {
            var value = prop.GetValue(parameters);
            if (value is null) continue;
            parts.Add($"{prop.Name}={value}");
        }
        return string.Join(", ", parts);
    }

    private sealed class ToolTimer(ToolAnalytics analytics, string toolName, object? parameters, long startTimestamp) : IDisposable
    {
        public void Dispose()
        {
            var elapsed = Stopwatch.GetElapsedTime(startTimestamp);
            analytics.WriteEntry(toolName, parameters, elapsed, success: true);
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Parlance.Mcp.Tests/Parlance.Mcp.Tests.csproj --filter "ToolAnalytics"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Parlance.Mcp/ToolAnalytics.cs tests/Parlance.Mcp.Tests/ToolAnalyticsTests.cs
git commit -m "feat: add ToolAnalytics service with file logging"
```

---

### Task 3: Register ToolAnalytics and delete ToolDiagnostics

**Files:**
- Modify: `src/Parlance.Mcp/Program.cs`
- Delete: `src/Parlance.Mcp/ToolDiagnostics.cs`

- [ ] **Step 1: Register ToolAnalytics in Program.cs**

In `Program.cs`, add after the existing `builder.Services.AddSingleton<CodeActionService>();` line:

```csharp
builder.Services.AddSingleton<ToolAnalytics>();
```

- [ ] **Step 2: Delete ToolDiagnostics.cs**

```bash
rm src/Parlance.Mcp/ToolDiagnostics.cs
```

- [ ] **Step 3: Verify it builds (will fail — tool callsites still reference ToolDiagnostics)**

Run: `dotnet build src/Parlance.Mcp/Parlance.Mcp.csproj`
Expected: FAIL with CS0103 errors for `ToolDiagnostics` in all tool files. This confirms the next task is needed.

Do NOT commit yet — proceed to Task 4 to fix all callsites first.

---

### Task 4: Update all tool callsites

**Files:** All 18 files in `src/Parlance.Mcp/Tools/`

Every tool follows the same mechanical transformation:
1. Remove `ILogger<ToolClassName> logger` parameter
2. Add `ToolAnalytics analytics` parameter
3. Replace `using var _ = ToolDiagnostics.TimeToolCall(logger, "tool-name");` with `using var _ = analytics.TimeToolCall("tool-name", new { param1, param2 });`
4. Replace any remaining `logger.LogXxx(...)` calls with the logger from another injected service, or remove if only used for the timing hook

**Important:** Some tools use `logger` for more than just the timing hook (e.g., `WorkspaceStatusTool` logs workspace load warnings, `DecompileTypeTool` logs decompilation failures). For those tools, keep `ILogger<ToolClassName> logger` AND add `ToolAnalytics analytics`.

Below is the exact change for each tool. The tools are grouped by complexity.

#### Group A: Tools with only the timing hook (remove logger entirely)

- [ ] **Step 1: Update FindReferencesTool.cs**

Change method signature from:
```csharp
    public static async Task<FindReferencesResult> FindReferences(
        WorkspaceSessionHolder holder, WorkspaceQueryService query,
        ILogger<FindReferencesTool> logger, string symbolName, CancellationToken ct)
```
To:
```csharp
    public static async Task<FindReferencesResult> FindReferences(
        WorkspaceSessionHolder holder, WorkspaceQueryService query,
        ToolAnalytics analytics, string symbolName, CancellationToken ct)
```

Change timing line from:
```csharp
        using var _ = ToolDiagnostics.TimeToolCall(logger, "find-references");
```
To:
```csharp
        using var _ = analytics.TimeToolCall("find-references", new { symbolName });
```

Remove `using Microsoft.Extensions.Logging;` if no longer needed.

- [ ] **Step 2: Update FindImplementationsTool.cs**

Same pattern. Signature change:
```csharp
        ToolAnalytics analytics, string typeName, CancellationToken ct)
```
Timing line:
```csharp
        using var _ = analytics.TimeToolCall("find-implementations", new { typeName });
```

- [ ] **Step 3: Update DescribeTypeTool.cs**

Signature change:
```csharp
        ToolAnalytics analytics, string typeName, CancellationToken ct)
```
Timing line:
```csharp
        using var _ = analytics.TimeToolCall("describe-type", new { typeName });
```

- [ ] **Step 4: Update SafeToDeleteTool.cs**

Signature change:
```csharp
        ToolAnalytics analytics, string symbolName, CancellationToken ct)
```
Timing line:
```csharp
        using var _ = analytics.TimeToolCall("safe-to-delete", new { symbolName });
```

- [ ] **Step 5: Update OutlineFileTool.cs**

Signature change:
```csharp
        ToolAnalytics analytics, string filePath, CancellationToken ct)
```
Timing line:
```csharp
        using var _ = analytics.TimeToolCall("outline-file", new { filePath });
```

- [ ] **Step 6: Update GetTypeAtTool.cs**

Signature change:
```csharp
        ToolAnalytics analytics, string filePath, int line, int column, CancellationToken ct)
```
Timing line:
```csharp
        using var _ = analytics.TimeToolCall("get-type-at", new { filePath, line, column });
```

- [ ] **Step 7: Update CallHierarchyTool.cs**

Signature change:
```csharp
        ToolAnalytics analytics, string methodName, CancellationToken ct)
```
Timing line:
```csharp
        using var _ = analytics.TimeToolCall("call-hierarchy", new { methodName });
```

- [ ] **Step 8: Update GetTypeDependenciesTool.cs**

Signature change:
```csharp
        ToolAnalytics analytics, string typeName, CancellationToken ct)
```
Timing line:
```csharp
        using var _ = analytics.TimeToolCall("get-type-dependencies", new { typeName });
```

- [ ] **Step 9: Update SearchSymbolsTool.cs**

Signature change:
```csharp
        ToolAnalytics analytics,
```
(remove `ILogger<SearchSymbolsTool> logger,`)

Timing line:
```csharp
        using var _ = analytics.TimeToolCall("search-symbols", new { searchQuery, kind, maxResults });
```

- [ ] **Step 10: Update TypeHierarchyTool.cs**

Signature change:
```csharp
        ToolAnalytics analytics,
```
(remove `ILogger<TypeHierarchyTool> logger,`)

Timing line:
```csharp
        using var _ = analytics.TimeToolCall("type-hierarchy", new { typeName, maxDepth });
```

- [ ] **Step 11: Update AnalyzeTool.cs**

Signature change:
```csharp
        ToolAnalytics analytics,
```
(remove `ILogger<AnalyzeTool> logger,`)

Timing line:
```csharp
        using var _ = analytics.TimeToolCall("analyze", new { files = string.Join(", ", files), curationSet, maxDiagnostics });
```

Note: `files` is a `string[]` — join for readable logging.

- [ ] **Step 12: Update GetCodeFixesTool.cs**

Signature change:
```csharp
        ToolAnalytics analytics,
```
(remove `ILogger<GetCodeFixesTool> logger,`)

Timing line:
```csharp
        using var _ = analytics.TimeToolCall("get-code-fixes", new { filePath, line, diagnosticId });
```

- [ ] **Step 13: Update GetRefactoringsTool.cs**

Signature change:
```csharp
        ToolAnalytics analytics,
```
(remove `ILogger<GetRefactoringsTool> logger,`)

Timing line:
```csharp
        using var _ = analytics.TimeToolCall("get-refactorings", new { filePath, line, column, endLine, endColumn });
```

- [ ] **Step 14: Update PreviewCodeActionTool.cs**

Signature change:
```csharp
        ToolAnalytics analytics,
```
(remove `ILogger<PreviewCodeActionTool> logger,`)

Timing line:
```csharp
        using var _ = analytics.TimeToolCall("preview-code-action", new { actionId });
```

- [ ] **Step 15: Update GotoDefinitionTool.cs**

Signature change:
```csharp
        ToolAnalytics analytics,
```
(remove `ILogger<GotoDefinitionTool> logger,`)

Timing line:
```csharp
        using var _ = analytics.TimeToolCall("goto-definition", new { symbolName, filePath, line, column });
```

- [ ] **Step 16: Update GetSymbolDocsTool.cs**

This tool uses `logger` in the private `GetDocs` and `GetInheritedDocs` methods. Keep `ILogger<GetSymbolDocsTool> logger` AND add `ToolAnalytics analytics`.

Signature:
```csharp
        ToolAnalytics analytics,
        ILogger<GetSymbolDocsTool> logger, string symbolName, CancellationToken ct)
```

Timing line:
```csharp
        using var _ = analytics.TimeToolCall("get-symbol-docs", new { symbolName });
```

#### Group B: Tools that use logger for more than timing (keep logger + add analytics)

- [ ] **Step 17: Update WorkspaceStatusTool.cs**

This tool logs `LogWarning` and `LogDebug` beyond the timing hook. Keep the logger.

Signature:
```csharp
    public static WorkspaceStatusResult GetStatus(
        WorkspaceSessionHolder holder,
        ParlanceMcpConfiguration configuration,
        ToolAnalytics analytics,
        ILogger<WorkspaceStatusTool> logger)
```

Timing line:
```csharp
        using var _ = analytics.TimeToolCall("workspace-status");
```

- [ ] **Step 18: Update DecompileTypeTool.cs**

This tool logs `LogWarning` on decompilation failure. Keep the logger.

Signature:
```csharp
    public static async Task<DecompileTypeResult> DecompileType(
        WorkspaceSessionHolder holder, WorkspaceQueryService query,
        ToolAnalytics analytics, ILogger<DecompileTypeTool> logger,
        string typeName, CancellationToken ct)
```

Timing line:
```csharp
        using var _ = analytics.TimeToolCall("decompile-type", new { typeName });
```

- [ ] **Step 19: Build to verify all callsites compile**

Run: `dotnet build src/Parlance.Mcp/Parlance.Mcp.csproj`
Expected: PASS — zero errors

- [ ] **Step 20: Run full test suite**

Run: `dotnet test Parlance.sln`
Expected: PASS — all existing tests still pass. Tool tests that previously created `NullLogger<T>` instances may need updating to pass a `ToolAnalytics` instance instead. If so, create a helper in the test project:

```csharp
// In test files that need it:
var analytics = new ToolAnalytics(
    new ParlanceMcpConfiguration("/fake/path.sln", Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid():N}")),
    NullLoggerFactory.Instance);
```

- [ ] **Step 21: Commit**

```bash
git add -A
git commit -m "feat: replace ToolDiagnostics with ToolAnalytics across all tools"
```

---

### Task 5: Verify end-to-end and clean up

- [ ] **Step 1: Run full test suite one final time**

Run: `dotnet test Parlance.sln`
Expected: PASS

- [ ] **Step 2: Check formatting**

Run: `dotnet format Parlance.sln --verify-no-changes`
Expected: PASS — if not, run `dotnet format Parlance.sln` and commit.

- [ ] **Step 3: Verify .gitignore covers analytics logs**

Check if `.parlance/` is in `.gitignore`. If not, add it:

```
.parlance/
```

- [ ] **Step 4: Commit any cleanup**

```bash
git add -A
git commit -m "chore: formatting and gitignore for analytics logs"
```
