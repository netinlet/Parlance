# Fix File Watcher Feedback Loop Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminate the infinite SnapshotVersion churn caused by `TryApplyChanges` writing changed files back to disk, re-triggering the file watcher.

**Architecture:** `MSBuildWorkspace` is a write-through workspace — `TryApplyChanges` persists text changes to disk by design. Parlance is a read-only analysis server. The fix separates concerns cleanly: `MSBuildWorkspace` becomes a pure loader; `CSharpWorkspaceSession` owns a `_currentSolution` field as the live in-memory snapshot. `OnFileChanges` and `RefreshAsync` update `_currentSolution` directly (no disk write), serialised by a new `_solutionLock` (`SemaphoreSlim(1,1)`) on the session. `ServerCompilationCache` receives a provider for `_currentSolution` instead of `workspace.CurrentSolution`.

**Tech Stack:** C# 13 / net10.0, Microsoft.CodeAnalysis.MSBuild, xUnit

---

## File Map

| File | Change |
|------|--------|
| `src/Parlance.CSharp.Workspace/CSharpWorkspaceSession.cs` | Add `_currentSolution` field + `_solutionLock`; move cache construction into the session constructor (removes `sessionRef` pattern); rewrite `OnFileChanges` and `RefreshAsync` to update `_currentSolution` under `_solutionLock` without calling `TryApplyChanges`; update `LoadAsync` to pass text-cached solution to constructor |
| `tests/Parlance.CSharp.Workspace.Tests/Integration/FileWatcherTests.cs` | Add regression test in a new `FileWatcherSessionTests` class: proves no feedback loop (SnapshotVersion stabilises after a single file edit) |

No other files need changes — `ServerCompilationCache` already accepts `Func<Solution>`; `WorkspaceFileWatcher` is unchanged.

> **Forward note (Milestone 3+):** `ServerCompilationCache.GetAsync` compiles the `Project` object passed to it — `solutionProvider` is only used in `MarkDirty`. Any future caller of `_cache.GetAsync` must source its `Project` from `_currentSolution` (via `session.CurrentSolution.GetProject(...)`) not from `_workspace.CurrentSolution`, otherwise compilation will be based on stale text. No such callers exist today.

---

## Concurrency design note

`OnFileChanges` and `RefreshAsync` both follow a fork-then-assign pattern:
```
var solution = _currentSolution;   // snapshot
solution = solution.WithDocumentText(...); // build new snapshot
_currentSolution = solution;        // write back
```
`volatile` alone does not protect this — two concurrent callers can each snapshot the same base and silently discard the other's changes. A new `SemaphoreSlim(1,1)` named `_solutionLock` on the session serialises both paths. `WorkspaceFileWatcher` has its own internal `_processingLock` that already serialises `OnFileChanges` calls from the watcher; `_solutionLock` additionally prevents a concurrent `RefreshAsync` from racing with the watcher. `volatile` is kept for the `_currentSolution` field so reads in the cache lambda see fresh values without acquiring the lock.

---

### Task 1: Write the regression test (red)

**Files:**
- Modify: `tests/Parlance.CSharp.Workspace.Tests/Integration/FileWatcherTests.cs`

This test exercises the full session (not just `WorkspaceFileWatcher`) and proves the feedback loop. It must fail before the fix and pass after.

Note: this test is timing-sensitive (OS filesystem event buffering). Ideally the assertion would be "version is stable after N watcher cycles" rather than after a wall-clock wait, but that would require exposing test hooks into the watcher internals — not worth it. The 2 s + 2 s approach is pragmatic and more than sufficient for a 300 ms debounce on any CI machine.

- [ ] **Step 1: Add the failing test — new class `FileWatcherSessionTests` at the bottom of the file**

```csharp
public sealed class FileWatcherSessionTests
{
    [Fact]
    public async Task FileChange_DoesNotCreateFeedbackLoop()
    {
        // Regression: OnFileChanges called TryApplyChanges which wrote back to disk,
        // re-triggering the watcher in an infinite loop.
        var tempDir = Path.Combine(Path.GetTempPath(), $"parlance-loop-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var csproj = Path.Combine(tempDir, "TestProject.csproj");
            await File.WriteAllTextAsync(csproj, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);

            var sourceFile = Path.Combine(tempDir, "Class1.cs");
            await File.WriteAllTextAsync(sourceFile, "namespace Test; public class Class1 { }");

            var options = new WorkspaceOpenOptions(
                Mode: WorkspaceMode.Server,
                EnableFileWatching: true);

            await using var session = await CSharpWorkspaceSession.OpenProjectAsync(csproj, options);

            // Edit the file — watcher should fire once then stop
            await File.WriteAllTextAsync(sourceFile,
                "namespace Test; public class Class1 { public int X { get; } }");

            // Wait for the first watcher cycle (debounce = 300ms + processing)
            await Task.Delay(TimeSpan.FromSeconds(2));

            var versionAfterFirstChange = session.SnapshotVersion;
            Assert.True(versionAfterFirstChange > 1,
                "SnapshotVersion should have incremented at least once after file edit");

            // Wait another full debounce window — version must NOT keep climbing
            await Task.Delay(TimeSpan.FromSeconds(2));

            Assert.Equal(versionAfterFirstChange, session.SnapshotVersion);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
```

Add the required `using` at the top of the file if not already present:
```csharp
using Parlance.CSharp.Workspace;
```

- [ ] **Step 2: Run the test — confirm it FAILS**

```bash
dotnet test tests/Parlance.CSharp.Workspace.Tests/Parlance.CSharp.Workspace.Tests.csproj \
    --filter "FileChange_DoesNotCreateFeedbackLoop" -v normal
```

Expected: **FAIL** — `Assert.Equal(versionAfterFirstChange, session.SnapshotVersion)` fails because SnapshotVersion keeps incrementing.

- [ ] **Step 3: Commit the failing test**

```bash
git add tests/Parlance.CSharp.Workspace.Tests/Integration/FileWatcherTests.cs
git commit -m "test: add regression test for watcher feedback loop (currently failing)"
```

---

### Task 2: Fix `CSharpWorkspaceSession` — own the solution snapshot

**Files:**
- Modify: `src/Parlance.CSharp.Workspace/CSharpWorkspaceSession.cs`

---

- [ ] **Step 1: Add `_currentSolution` and `_solutionLock` fields; update the constructor to own cache construction**

Add two fields after `_snapshotVersion`:

```csharp
private volatile Solution _currentSolution;
private readonly SemaphoreSlim _solutionLock = new(1, 1);
```

Update the private constructor: remove the `IProjectCompilationCache cache` parameter, add `Solution initialSolution` after `MSBuildWorkspace workspace`, and create the cache internally using a `() => _currentSolution` lambda. The lambda is deferred — it is never invoked during construction, only when `MarkDirty` is called from `OnFileChanges`/`RefreshAsync` after construction completes. `_currentSolution` must be assigned before `_cache` in the constructor body so the lambda always reads the live field.

```csharp
private CSharpWorkspaceSession(
    string workspacePath,
    MSBuildWorkspace workspace,
    Solution initialSolution,
    CSharpWorkspaceHealth health,
    ImmutableList<CSharpProjectInfo> projects,
    WorkspaceMode mode,
    ILoggerFactory loggerFactory)
{
    WorkspacePath = workspacePath;
    _workspace = workspace;
    _currentSolution = initialSolution;   // must precede _cache initialisation
    _cache = mode switch
    {
        WorkspaceMode.Server => new ServerCompilationCache(() => _currentSolution),
        _ => new ReportCompilationCache()
    };
    Health = health;
    Projects = projects;
    _mode = mode;
    _loggerFactory = loggerFactory;
    _logger = loggerFactory.CreateLogger<CSharpWorkspaceSession>();
}
```

Also add an `internal` accessor for future callers (Milestone 3+ tools that will need to source `Project` objects from the live snapshot — see forward note in File Map):

```csharp
internal Solution CurrentSolution => _currentSolution;
```

- [ ] **Step 2: Fix `RefreshAsync` — update `_currentSolution` without writing to disk**

Replace the entire body of `RefreshAsync` (from the `if (_mode...)` guard through the end of the method) with:

```csharp
if (_mode is WorkspaceMode.Report)
    throw new InvalidOperationException("RefreshAsync is not supported in Report mode");

await _solutionLock.WaitAsync(ct).ConfigureAwait(false);
try
{
    var solution = _currentSolution;
    var affectedProjects = new HashSet<ProjectId>();

    foreach (var project in solution.Projects)
    {
        foreach (var document in project.Documents)
        {
            if (document.FilePath is null || !File.Exists(document.FilePath))
                continue;

            var currentText = await document.GetTextAsync(ct);
            var diskContent = await File.ReadAllTextAsync(document.FilePath, ct);

            if (currentText.ToString() == diskContent)
                continue;

            var newText = SourceText.From(diskContent, currentText.Encoding);
            solution = solution.WithDocumentText(document.Id, newText);
            affectedProjects.Add(project.Id);
        }
    }

    if (affectedProjects.Count == 0)
    {
        _logger.LogDebug("RefreshAsync: no changes detected");
        return;
    }

    _currentSolution = solution;

    foreach (var projectId in affectedProjects)
        _cache.MarkDirty(projectId);

    Interlocked.Increment(ref _snapshotVersion);
    _logger.LogInformation(
        "RefreshAsync: {Count} project(s) updated, SnapshotVersion={Version}",
        affectedProjects.Count, SnapshotVersion);
}
finally
{
    _solutionLock.Release();
}
```

- [ ] **Step 3: Fix `OnFileChanges` — update `_currentSolution` without writing to disk**

Replace the entire body of `OnFileChanges` with:

```csharp
await _solutionLock.WaitAsync().ConfigureAwait(false);
try
{
    var solution = _currentSolution;
    var affectedProjects = new HashSet<ProjectId>();
    var hasChanges = false;

    foreach (var filePath in changedPaths)
    {
        var docIds = solution.GetDocumentIdsWithFilePath(filePath);
        if (docIds.IsEmpty) continue;

        string content;
        try
        {
            content = await File.ReadAllTextAsync(filePath);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Could not read changed file: {Path}", filePath);
            continue;
        }

        var existingDoc = solution.GetDocument(docIds[0]);
        var encoding = existingDoc is not null
            ? (await existingDoc.GetTextAsync()).Encoding
            : Encoding.UTF8;
        var newText = SourceText.From(content, encoding);
        foreach (var docId in docIds)
        {
            solution = solution.WithDocumentText(docId, newText);
            var projectId = solution.GetDocument(docId)?.Project.Id;
            if (projectId is not null)
                affectedProjects.Add(projectId);
            hasChanges = true;
        }
    }

    if (!hasChanges) return;

    _currentSolution = solution;

    foreach (var projectId in affectedProjects)
        _cache.MarkDirty(projectId);

    Interlocked.Increment(ref _snapshotVersion);
    _logger.LogInformation(
        "File changes applied: {Count} file(s), SnapshotVersion={Version}",
        changedPaths.Count, SnapshotVersion);
}
finally
{
    _solutionLock.Release();
}
```

- [ ] **Step 4: Fix `LoadAsync` — pass text-cached solution to constructor; remove `TryApplyChanges`**

In `LoadAsync`, find the initial text-caching block. It currently ends with:
```csharp
if (!workspace.TryApplyChanges(solution))
    logger.LogDebug("Initial text caching: TryApplyChanges returned false");
```
**Delete those two lines entirely.**

Find the line:
```csharp
var projects = MapProjects(workspace.CurrentSolution, logger);
```
Change it to:
```csharp
var projects = MapProjects(solution, logger);
```

Find the cache + session construction block. Delete the entire `IProjectCompilationCache cache = ...` declaration (the constructor now owns it). Update the `new CSharpWorkspaceSession(...)` call — remove the `cache` argument and add `solution` as the third argument:

```csharp
var session = new CSharpWorkspaceSession(
    workspacePath, workspace, solution, health, projects, options.Mode, loggerFactory);
```

No `sessionRef` needed. No invariant to document.

- [ ] **Step 5: Update `DisposeAsync` to dispose `_solutionLock`**

In `DisposeAsync`, after `await _watcher.DisposeAsync()` and before `_workspace.Dispose()`, add:

```csharp
_solutionLock.Dispose();
```

- [ ] **Step 6: Build**

```bash
dotnet build src/Parlance.CSharp.Workspace/Parlance.CSharp.Workspace.csproj
```

Expected: Build succeeded, 0 errors.

---

### Task 3: Run the regression test (green)

- [ ] **Step 1: Run the feedback loop regression test**

```bash
dotnet test tests/Parlance.CSharp.Workspace.Tests/Parlance.CSharp.Workspace.Tests.csproj \
    --filter "FileChange_DoesNotCreateFeedbackLoop" -v normal
```

Expected: **PASS** — SnapshotVersion increments once and then stays stable.

---

### Task 4: Run all tests

- [ ] **Step 1: Full test suite**

```bash
dotnet test Parlance.sln
```

Expected: All tests pass. Key canaries:
- `RefreshAsync_DetectsSourceTextChange` — refresh still works after removing `TryApplyChanges`
- `RefreshAsync_ServerMode_NoChanges_VersionUnchanged` — no spurious increments
- All existing `FileWatcherTests` — watcher logic unchanged
- `FileChange_DoesNotCreateFeedbackLoop` — regression green

---

### Task 5: Commit

- [ ] **Step 1: Stage and commit**

```bash
git add src/Parlance.CSharp.Workspace/CSharpWorkspaceSession.cs \
        tests/Parlance.CSharp.Workspace.Tests/Integration/FileWatcherTests.cs
git commit -m "fix: own solution snapshot in session to eliminate watcher feedback loop

TryApplyChanges on MSBuildWorkspace writes changed document text back to disk,
re-triggering FileSystemWatcher in an infinite loop (SnapshotVersion reaching
300+ from a single file save).

CSharpWorkspaceSession now owns _currentSolution as the live in-memory snapshot.
OnFileChanges and RefreshAsync both update _currentSolution under _solutionLock
(SemaphoreSlim) without calling TryApplyChanges — no disk write, no re-trigger.
MSBuildWorkspace is now a pure loader; workspace.CurrentSolution is no longer
the authoritative state after startup. The session constructor now owns cache
construction, capturing () => _currentSolution directly — no external wiring needed."
```
