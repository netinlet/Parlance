# Move WorkspaceSessionLifecycle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move `WorkspaceSessionLifecycle` from `Parlance.Mcp` into `Parlance.CSharp.Workspace` so `WorkspaceSessionHolder.SetSession` / `SetLoadFailure` remain `internal` and the `InternalsVisibleTo Include="Parlance.Mcp"` entry can be deleted.

**Architecture:** A new `WorkspaceLifecycleOptions` record (solution path + open options) is introduced in the workspace assembly as the only new public surface. `WorkspaceSessionLifecycle` moves there wholesale — same logic, same `IHostedService` contract. `Program.cs` in Mcp registers the options record and the moved service; the old file is deleted.

**Tech Stack:** .NET 10, `Microsoft.Extensions.Hosting.Abstractions` (for `IHostedService`), xUnit

---

## File Map

| Action | File | Notes |
|--------|------|-------|
| Create | `src/Parlance.CSharp.Workspace/WorkspaceLifecycleOptions.cs` | New record: `SolutionPath` + `WorkspaceOpenOptions` |
| Create | `src/Parlance.CSharp.Workspace/WorkspaceSessionLifecycle.cs` | Moved + adapted from Mcp |
| Modify | `src/Parlance.CSharp.Workspace/Parlance.CSharp.Workspace.csproj` | Add `M.E.Hosting.Abstractions`; remove `InternalsVisibleTo Parlance.Mcp` |
| Delete | `src/Parlance.Mcp/WorkspaceSessionLifecycle.cs` | Replaced by workspace version |
| Modify | `src/Parlance.Mcp/Program.cs` | Register `WorkspaceLifecycleOptions`; switch service registration |

---

### Task 1: Add `Microsoft.Extensions.Hosting.Abstractions` to workspace project

The workspace assembly needs `IHostedService`. It's a separate package from `Microsoft.Extensions.Hosting`.

**Files:**
- Modify: `src/Parlance.CSharp.Workspace/Parlance.CSharp.Workspace.csproj`

- [ ] **Step 1: Add the package reference**

In `Parlance.CSharp.Workspace.csproj`, add inside the existing `<ItemGroup>` with package references:

```xml
<PackageReference Include="Microsoft.Extensions.Hosting.Abstractions" Version="10.0.0" />
```

- [ ] **Step 2: Build to confirm it resolves**

```bash
dotnet build src/Parlance.CSharp.Workspace/Parlance.CSharp.Workspace.csproj -nologo -verbosity:quiet
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 3: Commit**

```bash
git add src/Parlance.CSharp.Workspace/Parlance.CSharp.Workspace.csproj
git commit -m "build: add Hosting.Abstractions to workspace project"
```

---

### Task 2: Introduce `WorkspaceLifecycleOptions`

This record is the only new public surface. It carries the two things the lifecycle needs that aren't available via DI from within the workspace assembly: the solution path, and the open options (mode, file watching).

**Files:**
- Create: `src/Parlance.CSharp.Workspace/WorkspaceLifecycleOptions.cs`

- [ ] **Step 1: Create the file**

```csharp
namespace Parlance.CSharp.Workspace;

public sealed record WorkspaceLifecycleOptions(
    string SolutionPath,
    WorkspaceOpenOptions OpenOptions);
```

- [ ] **Step 2: Build to confirm it compiles**

```bash
dotnet build src/Parlance.CSharp.Workspace/Parlance.CSharp.Workspace.csproj -nologo -verbosity:quiet
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 3: Commit**

```bash
git add src/Parlance.CSharp.Workspace/WorkspaceLifecycleOptions.cs
git commit -m "feat: add WorkspaceLifecycleOptions to workspace assembly"
```

---

### Task 3: Move `WorkspaceSessionLifecycle` into the workspace assembly

Port the file from `Parlance.Mcp` into `Parlance.CSharp.Workspace`. The logic is identical; the only changes are the namespace and the dependency: `ParlanceMcpConfiguration` → `WorkspaceLifecycleOptions`.

**Files:**
- Create: `src/Parlance.CSharp.Workspace/WorkspaceSessionLifecycle.cs`
- Reference: `src/Parlance.Mcp/WorkspaceSessionLifecycle.cs` (source to port from)

- [ ] **Step 1: Create the new file**

```csharp
using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Parlance.CSharp.Workspace;

public sealed class WorkspaceSessionLifecycle(
    WorkspaceSessionHolder holder,
    WorkspaceLifecycleOptions options,
    ILoggerFactory loggerFactory,
    ILogger<WorkspaceSessionLifecycle> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Loading workspace: {SolutionPath}", options.SolutionPath);
        var startTimestamp = Stopwatch.GetTimestamp();

        try
        {
            var openOptions = options.OpenOptions with { LoggerFactory = loggerFactory };
            var session = await CSharpWorkspaceSession.OpenSolutionAsync(
                options.SolutionPath, openOptions, cancellationToken);

            holder.SetSession(session);

            var elapsed = Stopwatch.GetElapsedTime(startTimestamp);
            logger.LogInformation(
                "Workspace loaded in {ElapsedMs:F0}ms: {Status}, {Count} project(s)",
                elapsed.TotalMilliseconds, session.Health.Status, session.Projects.Count);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var elapsed = Stopwatch.GetElapsedTime(startTimestamp);
            logger.LogError(ex,
                "Workspace load failed after {ElapsedMs:F0}ms: {SolutionPath}",
                elapsed.TotalMilliseconds, options.SolutionPath);

            holder.SetLoadFailure(new WorkspaceLoadFailure(ex.Message, options.SolutionPath));
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Disposing workspace session");
        await holder.DisposeAsync();
    }
}
```

Note: `options.OpenOptions with { LoggerFactory = loggerFactory }` replaces the original `new WorkspaceOpenOptions(Mode: ..., EnableFileWatching: true, LoggerFactory: loggerFactory)`. The caller sets Mode and EnableFileWatching; the lifecycle injects its own `ILoggerFactory` at startup time.

- [ ] **Step 2: Build workspace to confirm it compiles**

```bash
dotnet build src/Parlance.CSharp.Workspace/Parlance.CSharp.Workspace.csproj -nologo -verbosity:quiet
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 3: Commit**

```bash
git add src/Parlance.CSharp.Workspace/WorkspaceSessionLifecycle.cs
git commit -m "feat: move WorkspaceSessionLifecycle into workspace assembly"
```

---

### Task 4: Update `Program.cs` and delete the old lifecycle file

Switch Mcp to use the workspace-assembly lifecycle. Register `WorkspaceLifecycleOptions` with the solution path and open options. Delete the now-redundant `Parlance.Mcp/WorkspaceSessionLifecycle.cs`.

**Files:**
- Modify: `src/Parlance.Mcp/Program.cs`
- Delete: `src/Parlance.Mcp/WorkspaceSessionLifecycle.cs`

- [ ] **Step 1: Update `Program.cs`**

Replace the existing registrations block. Current state (lines 21-24):

```csharp
builder.Services.AddSingleton(configuration);
builder.Services.AddSingleton<WorkspaceSessionHolder>();
builder.Services.AddHostedService<WorkspaceSessionLifecycle>();
builder.Services.AddSingleton<WorkspaceQueryService>();
```

New state:

```csharp
builder.Services.AddSingleton(configuration);
builder.Services.AddSingleton(new WorkspaceLifecycleOptions(
    configuration.SolutionPath,
    new WorkspaceOpenOptions(Mode: WorkspaceMode.Server, EnableFileWatching: true)));
builder.Services.AddSingleton<WorkspaceSessionHolder>();
builder.Services.AddHostedService<WorkspaceSessionLifecycle>();
builder.Services.AddSingleton<WorkspaceQueryService>();
```

The `LoggerFactory` is no longer passed in `WorkspaceOpenOptions` here — the lifecycle injects it at startup via `options.OpenOptions with { LoggerFactory = loggerFactory }`.

- [ ] **Step 2: Delete the old file**

```bash
rm src/Parlance.Mcp/WorkspaceSessionLifecycle.cs
```

- [ ] **Step 3: Build Mcp to confirm it compiles**

```bash
dotnet build src/Parlance.Mcp/Parlance.Mcp.csproj -nologo -verbosity:quiet
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 4: Commit**

```bash
git add src/Parlance.Mcp/Program.cs
git rm src/Parlance.Mcp/WorkspaceSessionLifecycle.cs
git commit -m "refactor: register workspace lifecycle from workspace assembly in Mcp"
```

---

### Task 5: Remove `InternalsVisibleTo Include="Parlance.Mcp"` from workspace project

This is the payoff. With the lifecycle moved, Mcp no longer calls any `internal` workspace members.

**Files:**
- Modify: `src/Parlance.CSharp.Workspace/Parlance.CSharp.Workspace.csproj`

- [ ] **Step 1: Remove the entry**

In `Parlance.CSharp.Workspace.csproj`, delete this line from the `<ItemGroup>`:

```xml
<InternalsVisibleTo Include="Parlance.Mcp" />
```

Leave the other two entries (`Parlance.CSharp.Workspace.Tests` and `Parlance.Mcp.Tests`) in place — those are for test setup.

- [ ] **Step 2: Build both projects to confirm nothing broke**

```bash
dotnet build src/Parlance.CSharp.Workspace/Parlance.CSharp.Workspace.csproj -nologo -verbosity:quiet
dotnet build src/Parlance.Mcp/Parlance.Mcp.csproj -nologo -verbosity:quiet
```

Expected: both succeed with 0 errors.

- [ ] **Step 3: Run the tool unit tests**

```bash
dotnet test tests/Parlance.Mcp.Tests/Parlance.Mcp.Tests.csproj --filter "FullyQualifiedName~ToolTests" -nologo --verbosity:quiet
```

Expected: `Passed! - Failed: 0, Passed: 54`

- [ ] **Step 4: Commit**

```bash
git add src/Parlance.CSharp.Workspace/Parlance.CSharp.Workspace.csproj
git commit -m "refactor: remove InternalsVisibleTo Parlance.Mcp — boundary now enforced by assembly"
```
