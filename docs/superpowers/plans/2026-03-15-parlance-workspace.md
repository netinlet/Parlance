# Parlance.CSharp.Workspace Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the C# workspace engine (Milestone 1) — a sealed `CSharpWorkspaceSession` wrapping `MSBuildWorkspace` with solution/project loading, health reporting, compilation caching, file watching, and structured logging.

**Architecture:** `CSharpWorkspaceSession` is a sealed class with static factory methods (`OpenSolutionAsync`, `OpenProjectAsync`). It wraps `MSBuildWorkspace`, loads real .sln/.csproj files, reports health via `CSharpWorkspaceHealth`, tracks freshness via `SnapshotVersion`, and delegates compilation caching to an internal `IProjectCompilationCache` selected by `WorkspaceMode` (Report = one-shot, Server = dirty-tracking). File watching detects source text changes in server mode and marks affected projects dirty with dependency-aware cascade.

**Tech Stack:** .NET 10, Microsoft.CodeAnalysis.Workspaces.MSBuild 5.0.0, Microsoft.Build.Locator 1.7.8, Microsoft.Extensions.Logging.Abstractions 10.0.0, xUnit 2.9.3

**Spec:** `docs/superpowers/specs/2026-03-14-parlance-workspace-design.md`
**Roadmap:** `docs/plans/2026-03-14-ide-for-ai-roadmap.md`

**Setup:** Create a git worktree in `.worktrees/` before starting implementation:

```bash
git worktree add .worktrees/workspace development
cd .worktrees/workspace
```

All file paths in this plan are relative to the worktree root.

---

## File Structure

### New Source Files

```
src/Parlance.CSharp.Workspace/
├── Parlance.CSharp.Workspace.csproj    # net10.0, MSBuild + Roslyn + Logging deps
├── WorkspaceMode.cs                    # Report/Server enum
├── WorkspaceOpenOptions.cs             # Mode + file watching + logging config
├── WorkspaceProjectKey.cs              # Strongly typed project identity (wraps Guid)
├── WorkspaceLoadStatus.cs              # Loaded/Degraded/Failed (session-level)
├── ProjectLoadStatus.cs                # Loaded/Failed (project-level)
├── WorkspaceDiagnosticSeverity.cs      # Error/Warning/Info enum
├── WorkspaceDiagnostic.cs              # Structured diagnostic for workspace issues
├── CSharpProjectInfo.cs                # Project metadata + status + diagnostics
├── CSharpWorkspaceHealth.cs            # Session-level health with project list
├── WorkspaceLoadException.cs           # Exception for total load failures
├── CSharpWorkspaceSession.cs           # Main session — loading, health, refresh, dispose
└── Internal/
    ├── IProjectCompilationCache.cs     # Internal cache interface
    ├── ProjectCompilationState.cs      # Cached compilation + dirty state
    ├── ReportCompilationCache.cs       # One-shot cache (Report mode)
    ├── ServerCompilationCache.cs       # Dirty-tracking cache (Server mode)
    └── WorkspaceFileWatcher.cs         # FileSystemWatcher wrapper with debouncing
```

### New Test Files

```
tests/Parlance.CSharp.Workspace.Tests/
├── Parlance.CSharp.Workspace.Tests.csproj
├── WorkspaceModelTests.cs              # Enums + WorkspaceProjectKey
├── WorkspaceOpenOptionsTests.cs        # FileWatchingEnabled logic + validation
├── CSharpWorkspaceHealthTests.cs       # FromProjects factory + status derivation
├── WorkspaceLoadExceptionTests.cs      # Construction, properties
├── LocationFilePathTests.cs            # Location backward compat with FilePath
├── Internal/
│   ├── ReportCompilationCacheTests.cs  # Cache-once, no-op dirty
│   └── ServerCompilationCacheTests.cs  # Dirty tracking, dependency cascade
└── Integration/
    ├── TestPaths.cs                    # Helper to locate Parlance.sln
    ├── SolutionLoadingTests.cs         # Load real solution, verify health
    ├── ProjectLoadingTests.cs          # Load single .csproj
    ├── RefreshTests.cs                 # RefreshAsync behavior
    └── FileWatcherTests.cs             # File change detection
```

### Modified Files

- `src/Parlance.Abstractions/Location.cs` — add `string? FilePath = null` parameter
- `Parlance.sln` — add 2 new projects via `dotnet sln add`

---

## Chunk 1: Scaffolding and Model Types

### Task 1: Create projects and add to solution

**Files:**
- Create: `src/Parlance.CSharp.Workspace/Parlance.CSharp.Workspace.csproj`
- Create: `tests/Parlance.CSharp.Workspace.Tests/Parlance.CSharp.Workspace.Tests.csproj`
- Modify: `Parlance.sln`

- [ ] **Step 1: Create workspace project directory**

```bash
mkdir -p src/Parlance.CSharp.Workspace/Internal
```

- [ ] **Step 2: Create workspace project file**

Create `src/Parlance.CSharp.Workspace/Parlance.CSharp.Workspace.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Parlance.Abstractions\Parlance.Abstractions.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Build.Locator" Version="1.7.8" />
    <PackageReference Include="Microsoft.CodeAnalysis.Workspaces.MSBuild" Version="5.0.0" />
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp.Workspaces" Version="5.0.0" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.0" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="Parlance.CSharp.Workspace.Tests" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Create test project directory**

```bash
mkdir -p tests/Parlance.CSharp.Workspace.Tests/Internal
mkdir -p tests/Parlance.CSharp.Workspace.Tests/Integration
```

- [ ] **Step 4: Create test project file**

Create `tests/Parlance.CSharp.Workspace.Tests/Parlance.CSharp.Workspace.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.4" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Parlance.CSharp.Workspace\Parlance.CSharp.Workspace.csproj" />
    <ProjectReference Include="..\..\src\Parlance.Abstractions\Parlance.Abstractions.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 5: Add projects to solution**

```bash
dotnet sln Parlance.sln add src/Parlance.CSharp.Workspace/Parlance.CSharp.Workspace.csproj --solution-folder src
dotnet sln Parlance.sln add tests/Parlance.CSharp.Workspace.Tests/Parlance.CSharp.Workspace.Tests.csproj --solution-folder tests
```

- [ ] **Step 6: Verify build**

```bash
dotnet build src/Parlance.CSharp.Workspace/Parlance.CSharp.Workspace.csproj
```

Expected: Build succeeds (empty project, NuGet packages restore).

- [ ] **Step 7: Commit**

```bash
git add src/Parlance.CSharp.Workspace/Parlance.CSharp.Workspace.csproj \
        tests/Parlance.CSharp.Workspace.Tests/Parlance.CSharp.Workspace.Tests.csproj \
        Parlance.sln
git commit -m "Add Parlance.CSharp.Workspace project and test project scaffolding"
```

---

### Task 2: Simple model types — enums and WorkspaceProjectKey

**Files:**
- Create: `src/Parlance.CSharp.Workspace/WorkspaceMode.cs`
- Create: `src/Parlance.CSharp.Workspace/WorkspaceDiagnosticSeverity.cs`
- Create: `src/Parlance.CSharp.Workspace/ProjectLoadStatus.cs`
- Create: `src/Parlance.CSharp.Workspace/WorkspaceLoadStatus.cs`
- Create: `src/Parlance.CSharp.Workspace/WorkspaceProjectKey.cs`
- Create: `tests/Parlance.CSharp.Workspace.Tests/WorkspaceModelTests.cs`

- [ ] **Step 1: Write tests for enums and WorkspaceProjectKey**

Create `tests/Parlance.CSharp.Workspace.Tests/WorkspaceModelTests.cs`:

```csharp
using Parlance.CSharp.Workspace;

namespace Parlance.CSharp.Workspace.Tests;

public sealed class WorkspaceModelTests
{
    [Fact]
    public void WorkspaceMode_HasReportAndServer()
    {
        Assert.Equal(0, (int)WorkspaceMode.Report);
        Assert.Equal(1, (int)WorkspaceMode.Server);
    }

    [Fact]
    public void WorkspaceDiagnosticSeverity_HasExpectedValues()
    {
        Assert.Equal(0, (int)WorkspaceDiagnosticSeverity.Error);
        Assert.Equal(1, (int)WorkspaceDiagnosticSeverity.Warning);
        Assert.Equal(2, (int)WorkspaceDiagnosticSeverity.Info);
    }

    [Fact]
    public void ProjectLoadStatus_HasLoadedAndFailed()
    {
        Assert.Equal(0, (int)ProjectLoadStatus.Loaded);
        Assert.Equal(1, (int)ProjectLoadStatus.Failed);
    }

    [Fact]
    public void WorkspaceLoadStatus_HasLoadedDegradedFailed()
    {
        Assert.Equal(0, (int)WorkspaceLoadStatus.Loaded);
        Assert.Equal(1, (int)WorkspaceLoadStatus.Degraded);
        Assert.Equal(2, (int)WorkspaceLoadStatus.Failed);
    }

    [Fact]
    public void WorkspaceProjectKey_Default_HasEmptyGuid()
    {
        var key = default(WorkspaceProjectKey);
        Assert.Equal(Guid.Empty, key.Value);
    }

    [Fact]
    public void WorkspaceProjectKey_Equality_SameGuid()
    {
        var guid = Guid.NewGuid();
        var key1 = new WorkspaceProjectKey(guid);
        var key2 = new WorkspaceProjectKey(guid);
        Assert.Equal(key1, key2);
    }

    [Fact]
    public void WorkspaceProjectKey_Inequality_DifferentGuid()
    {
        var key1 = new WorkspaceProjectKey(Guid.NewGuid());
        var key2 = new WorkspaceProjectKey(Guid.NewGuid());
        Assert.NotEqual(key1, key2);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/Parlance.CSharp.Workspace.Tests/Parlance.CSharp.Workspace.Tests.csproj --filter "WorkspaceModelTests"
```

Expected: Build failure — types don't exist yet.

- [ ] **Step 3: Implement all simple model types**

Create `src/Parlance.CSharp.Workspace/WorkspaceMode.cs`:

```csharp
namespace Parlance.CSharp.Workspace;

public enum WorkspaceMode
{
    Report,
    Server
}
```

Create `src/Parlance.CSharp.Workspace/WorkspaceDiagnosticSeverity.cs`:

```csharp
namespace Parlance.CSharp.Workspace;

public enum WorkspaceDiagnosticSeverity
{
    Error,
    Warning,
    Info
}
```

Create `src/Parlance.CSharp.Workspace/ProjectLoadStatus.cs`:

```csharp
namespace Parlance.CSharp.Workspace;

public enum ProjectLoadStatus
{
    Loaded,
    Failed
}
```

Create `src/Parlance.CSharp.Workspace/WorkspaceLoadStatus.cs`:

```csharp
namespace Parlance.CSharp.Workspace;

public enum WorkspaceLoadStatus
{
    Loaded,
    Degraded,
    Failed
}
```

Create `src/Parlance.CSharp.Workspace/WorkspaceProjectKey.cs`:

```csharp
namespace Parlance.CSharp.Workspace;

public readonly record struct WorkspaceProjectKey(Guid Value);
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test tests/Parlance.CSharp.Workspace.Tests/Parlance.CSharp.Workspace.Tests.csproj --filter "WorkspaceModelTests"
```

Expected: All 7 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Parlance.CSharp.Workspace/WorkspaceMode.cs \
        src/Parlance.CSharp.Workspace/WorkspaceDiagnosticSeverity.cs \
        src/Parlance.CSharp.Workspace/ProjectLoadStatus.cs \
        src/Parlance.CSharp.Workspace/WorkspaceLoadStatus.cs \
        src/Parlance.CSharp.Workspace/WorkspaceProjectKey.cs \
        tests/Parlance.CSharp.Workspace.Tests/WorkspaceModelTests.cs
git commit -m "Add workspace model enums and WorkspaceProjectKey"
```

---

### Task 3: WorkspaceDiagnostic, CSharpProjectInfo, and CSharpWorkspaceHealth records

**Files:**
- Create: `src/Parlance.CSharp.Workspace/WorkspaceDiagnostic.cs`
- Create: `src/Parlance.CSharp.Workspace/CSharpProjectInfo.cs`
- Create: `src/Parlance.CSharp.Workspace/CSharpWorkspaceHealth.cs`
- Create: `tests/Parlance.CSharp.Workspace.Tests/CSharpWorkspaceHealthTests.cs`

- [ ] **Step 1: Write tests for CSharpWorkspaceHealth status derivation**

The interesting behavior is the `FromProjects` factory that derives `WorkspaceLoadStatus` from the project list. Workspace-level diagnostics are still retained on the health record, but they do not redefine completeness status by themselves. `WorkspaceDiagnostic` and `CSharpProjectInfo` are straightforward records exercised through the health tests.

Create `tests/Parlance.CSharp.Workspace.Tests/CSharpWorkspaceHealthTests.cs`:

```csharp
using Parlance.CSharp.Workspace;

namespace Parlance.CSharp.Workspace.Tests;

public sealed class CSharpWorkspaceHealthTests
{
    private static CSharpProjectInfo MakeProject(
        ProjectLoadStatus status,
        string name = "TestProject") =>
        new(
            Key: new WorkspaceProjectKey(Guid.NewGuid()),
            Name: name,
            ProjectPath: $"/path/to/{name}.csproj",
            TargetFrameworks: ["net10.0"],
            ActiveTargetFramework: "net10.0",
            LangVersion: "13.0",
            Status: status,
            Diagnostics: []);

    [Fact]
    public void FromProjects_AllLoaded_NoDiagnostics_StatusIsLoaded()
    {
        var projects = new[]
        {
            MakeProject(ProjectLoadStatus.Loaded, "A"),
            MakeProject(ProjectLoadStatus.Loaded, "B")
        };

        var health = CSharpWorkspaceHealth.FromProjects(projects);

        Assert.Equal(WorkspaceLoadStatus.Loaded, health.Status);
        Assert.Equal(2, health.Projects.Count);
        Assert.Empty(health.Diagnostics);
    }

    [Fact]
    public void FromProjects_AllFailed_StatusIsFailed()
    {
        var projects = new[]
        {
            MakeProject(ProjectLoadStatus.Failed, "A"),
            MakeProject(ProjectLoadStatus.Failed, "B")
        };

        var health = CSharpWorkspaceHealth.FromProjects(projects);

        Assert.Equal(WorkspaceLoadStatus.Failed, health.Status);
    }

    [Fact]
    public void FromProjects_AllLoaded_WithWorkspaceWarning_StatusIsLoaded()
    {
        var projects = new[]
        {
            MakeProject(ProjectLoadStatus.Loaded, "A"),
            MakeProject(ProjectLoadStatus.Loaded, "B")
        };
        var diagnostics = new[]
        {
            new WorkspaceDiagnostic("MSB3277", "Found conflicts", WorkspaceDiagnosticSeverity.Warning)
        };

        var health = CSharpWorkspaceHealth.FromProjects(projects, diagnostics);

        Assert.Equal(WorkspaceLoadStatus.Loaded, health.Status);
        Assert.Single(health.Diagnostics);
    }

    [Fact]
    public void FromProjects_Mixed_StatusIsDegraded()
    {
        var projects = new[]
        {
            MakeProject(ProjectLoadStatus.Loaded, "A"),
            MakeProject(ProjectLoadStatus.Failed, "B")
        };

        var health = CSharpWorkspaceHealth.FromProjects(projects);

        Assert.Equal(WorkspaceLoadStatus.Degraded, health.Status);
    }

    [Fact]
    public void FromProjects_Empty_StatusIsFailed()
    {
        var health = CSharpWorkspaceHealth.FromProjects([]);

        Assert.Equal(WorkspaceLoadStatus.Failed, health.Status);
        Assert.Empty(health.Projects);
        Assert.Empty(health.Diagnostics);
    }

    [Fact]
    public void WorkspaceDiagnostic_ConstructionSetsProperties()
    {
        var diag = new WorkspaceDiagnostic("MSB4236", "SDK not found", WorkspaceDiagnosticSeverity.Error);

        Assert.Equal("MSB4236", diag.Code);
        Assert.Equal("SDK not found", diag.Message);
        Assert.Equal(WorkspaceDiagnosticSeverity.Error, diag.Severity);
    }

    [Fact]
    public void CSharpProjectInfo_ConstructionSetsProperties()
    {
        var key = new WorkspaceProjectKey(Guid.NewGuid());
        var diags = new[] { new WorkspaceDiagnostic("W001", "warn", WorkspaceDiagnosticSeverity.Warning) };

        var info = new CSharpProjectInfo(
            Key: key,
            Name: "MyProject",
            ProjectPath: "/path/to/MyProject.csproj",
            TargetFrameworks: ["net8.0", "net10.0"],
            ActiveTargetFramework: "net10.0",
            LangVersion: "13.0",
            Status: ProjectLoadStatus.Loaded,
            Diagnostics: diags);

        Assert.Equal(key, info.Key);
        Assert.Equal("MyProject", info.Name);
        Assert.Equal("/path/to/MyProject.csproj", info.ProjectPath);
        Assert.Equal(["net8.0", "net10.0"], info.TargetFrameworks);
        Assert.Equal("net10.0", info.ActiveTargetFramework);
        Assert.Equal("13.0", info.LangVersion);
        Assert.Equal(ProjectLoadStatus.Loaded, info.Status);
        Assert.Single(info.Diagnostics);
    }

    [Fact]
    public void CSharpProjectInfo_FailedProject_NullOptionalFields()
    {
        var info = new CSharpProjectInfo(
            Key: new WorkspaceProjectKey(Guid.NewGuid()),
            Name: "Broken",
            ProjectPath: "/path/to/Broken.csproj",
            TargetFrameworks: [],
            ActiveTargetFramework: null,
            LangVersion: null,
            Status: ProjectLoadStatus.Failed,
            Diagnostics: [new WorkspaceDiagnostic("ERR", "Load failed", WorkspaceDiagnosticSeverity.Error)]);

        Assert.Null(info.ActiveTargetFramework);
        Assert.Null(info.LangVersion);
        Assert.Empty(info.TargetFrameworks);
        Assert.Equal(ProjectLoadStatus.Failed, info.Status);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/Parlance.CSharp.Workspace.Tests/Parlance.CSharp.Workspace.Tests.csproj --filter "CSharpWorkspaceHealthTests"
```

Expected: Build failure — records don't exist yet.

- [ ] **Step 3: Implement WorkspaceDiagnostic**

Create `src/Parlance.CSharp.Workspace/WorkspaceDiagnostic.cs`:

```csharp
namespace Parlance.CSharp.Workspace;

public sealed record WorkspaceDiagnostic(
    string Code,
    string Message,
    WorkspaceDiagnosticSeverity Severity);
```

- [ ] **Step 4: Implement CSharpProjectInfo**

Create `src/Parlance.CSharp.Workspace/CSharpProjectInfo.cs`:

```csharp
namespace Parlance.CSharp.Workspace;

public sealed record CSharpProjectInfo(
    WorkspaceProjectKey Key,
    string Name,
    string ProjectPath,
    IReadOnlyList<string> TargetFrameworks,
    string? ActiveTargetFramework,
    string? LangVersion,
    ProjectLoadStatus Status,
    IReadOnlyList<WorkspaceDiagnostic> Diagnostics);
```

- [ ] **Step 5: Implement CSharpWorkspaceHealth with FromProjects factory**

Create `src/Parlance.CSharp.Workspace/CSharpWorkspaceHealth.cs`:

```csharp
namespace Parlance.CSharp.Workspace;

public sealed record CSharpWorkspaceHealth(
    WorkspaceLoadStatus Status,
    IReadOnlyList<CSharpProjectInfo> Projects,
    IReadOnlyList<WorkspaceDiagnostic> Diagnostics)
{
    public static CSharpWorkspaceHealth FromProjects(
        IReadOnlyList<CSharpProjectInfo> projects,
        IReadOnlyList<WorkspaceDiagnostic>? diagnostics = null)
    {
        diagnostics ??= [];
        return new(DeriveStatus(projects), projects, diagnostics);
    }

    private static WorkspaceLoadStatus DeriveStatus(
        IReadOnlyList<CSharpProjectInfo> projects)
    {
        return projects switch
        {
            { Count: 0 } => WorkspaceLoadStatus.Failed,
            _ when projects.All(p => p.Status is ProjectLoadStatus.Failed) => WorkspaceLoadStatus.Failed,
            _ when projects.All(p => p.Status is ProjectLoadStatus.Loaded) => WorkspaceLoadStatus.Loaded,
            _ => WorkspaceLoadStatus.Degraded
        };
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

```bash
dotnet test tests/Parlance.CSharp.Workspace.Tests/Parlance.CSharp.Workspace.Tests.csproj --filter "CSharpWorkspaceHealthTests"
```

Expected: All 8 tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/Parlance.CSharp.Workspace/WorkspaceDiagnostic.cs \
        src/Parlance.CSharp.Workspace/CSharpProjectInfo.cs \
        src/Parlance.CSharp.Workspace/CSharpWorkspaceHealth.cs \
        tests/Parlance.CSharp.Workspace.Tests/CSharpWorkspaceHealthTests.cs
git commit -m "Add WorkspaceDiagnostic, CSharpProjectInfo, and CSharpWorkspaceHealth"
```

---

### Task 4: WorkspaceOpenOptions with validation

**Files:**
- Create: `src/Parlance.CSharp.Workspace/WorkspaceOpenOptions.cs`
- Create: `tests/Parlance.CSharp.Workspace.Tests/WorkspaceOpenOptionsTests.cs`

- [ ] **Step 1: Write tests for FileWatchingEnabled logic**

Create `tests/Parlance.CSharp.Workspace.Tests/WorkspaceOpenOptionsTests.cs`:

```csharp
using Parlance.CSharp.Workspace;

namespace Parlance.CSharp.Workspace.Tests;

public sealed class WorkspaceOpenOptionsTests
{
    [Fact]
    public void Default_IsReportMode_NoFileWatching()
    {
        var options = new WorkspaceOpenOptions();

        Assert.Equal(WorkspaceMode.Report, options.Mode);
        Assert.False(options.FileWatchingEnabled);
    }

    [Fact]
    public void ReportMode_FileWatchingNull_ReturnsFalse()
    {
        var options = new WorkspaceOpenOptions(Mode: WorkspaceMode.Report);

        Assert.False(options.FileWatchingEnabled);
    }

    [Fact]
    public void ReportMode_FileWatchingTrue_Throws()
    {
        var options = new WorkspaceOpenOptions(
            Mode: WorkspaceMode.Report,
            EnableFileWatching: true);

        Assert.Throws<ArgumentException>(() => options.FileWatchingEnabled);
    }

    [Fact]
    public void ServerMode_FileWatchingNull_DefaultsTrue()
    {
        var options = new WorkspaceOpenOptions(Mode: WorkspaceMode.Server);

        Assert.True(options.FileWatchingEnabled);
    }

    [Fact]
    public void ServerMode_FileWatchingFalse_ReturnsFalse()
    {
        var options = new WorkspaceOpenOptions(
            Mode: WorkspaceMode.Server,
            EnableFileWatching: false);

        Assert.False(options.FileWatchingEnabled);
    }

    [Fact]
    public void ServerMode_FileWatchingTrue_ReturnsTrue()
    {
        var options = new WorkspaceOpenOptions(
            Mode: WorkspaceMode.Server,
            EnableFileWatching: true);

        Assert.True(options.FileWatchingEnabled);
    }

    [Fact]
    public void ReportMode_FileWatchingExplicitlyFalse_ReturnsFalse()
    {
        var options = new WorkspaceOpenOptions(
            Mode: WorkspaceMode.Report,
            EnableFileWatching: false);

        Assert.False(options.FileWatchingEnabled);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/Parlance.CSharp.Workspace.Tests/Parlance.CSharp.Workspace.Tests.csproj --filter "WorkspaceOpenOptionsTests"
```

Expected: Build failure — `WorkspaceOpenOptions` doesn't exist.

- [ ] **Step 3: Implement WorkspaceOpenOptions**

Create `src/Parlance.CSharp.Workspace/WorkspaceOpenOptions.cs`:

```csharp
using Microsoft.Extensions.Logging;

namespace Parlance.CSharp.Workspace;

public sealed record WorkspaceOpenOptions(
    WorkspaceMode Mode = WorkspaceMode.Report,
    bool? EnableFileWatching = null,
    ILoggerFactory? LoggerFactory = null)
{
    public bool FileWatchingEnabled => Mode switch
    {
        WorkspaceMode.Report => EnableFileWatching == true
            ? throw new ArgumentException("File watching is not supported in Report mode")
            : false,
        WorkspaceMode.Server => EnableFileWatching ?? true,
        _ => false
    };
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test tests/Parlance.CSharp.Workspace.Tests/Parlance.CSharp.Workspace.Tests.csproj --filter "WorkspaceOpenOptionsTests"
```

Expected: All 7 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Parlance.CSharp.Workspace/WorkspaceOpenOptions.cs \
        tests/Parlance.CSharp.Workspace.Tests/WorkspaceOpenOptionsTests.cs
git commit -m "Add WorkspaceOpenOptions with FileWatchingEnabled validation"
```

---

### Task 5: WorkspaceLoadException

**Files:**
- Create: `src/Parlance.CSharp.Workspace/WorkspaceLoadException.cs`
- Create: `tests/Parlance.CSharp.Workspace.Tests/WorkspaceLoadExceptionTests.cs`

- [ ] **Step 1: Write tests for WorkspaceLoadException**

Create `tests/Parlance.CSharp.Workspace.Tests/WorkspaceLoadExceptionTests.cs`:

```csharp
using Parlance.CSharp.Workspace;

namespace Parlance.CSharp.Workspace.Tests;

public sealed class WorkspaceLoadExceptionTests
{
    [Fact]
    public void Construction_SetsMessageAndPath()
    {
        var ex = new WorkspaceLoadException("Load failed", "/path/to/Solution.sln");

        Assert.Equal("Load failed", ex.Message);
        Assert.Equal("/path/to/Solution.sln", ex.WorkspacePath);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public void Construction_WithInnerException()
    {
        var inner = new FileNotFoundException("Not found");
        var ex = new WorkspaceLoadException("Load failed", "/path/to/Solution.sln", inner);

        Assert.Equal("Load failed", ex.Message);
        Assert.Equal("/path/to/Solution.sln", ex.WorkspacePath);
        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public void IsException()
    {
        var ex = new WorkspaceLoadException("fail", "/path");
        Assert.IsAssignableFrom<Exception>(ex);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/Parlance.CSharp.Workspace.Tests/Parlance.CSharp.Workspace.Tests.csproj --filter "WorkspaceLoadExceptionTests"
```

Expected: Build failure.

- [ ] **Step 3: Implement WorkspaceLoadException**

Create `src/Parlance.CSharp.Workspace/WorkspaceLoadException.cs`:

```csharp
namespace Parlance.CSharp.Workspace;

public sealed class WorkspaceLoadException(
    string message,
    string workspacePath,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string WorkspacePath { get; } = workspacePath;
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test tests/Parlance.CSharp.Workspace.Tests/Parlance.CSharp.Workspace.Tests.csproj --filter "WorkspaceLoadExceptionTests"
```

Expected: All 3 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Parlance.CSharp.Workspace/WorkspaceLoadException.cs \
        tests/Parlance.CSharp.Workspace.Tests/WorkspaceLoadExceptionTests.cs
git commit -m "Add WorkspaceLoadException"
```

---

### Task 6: Add FilePath to Location in Abstractions

**Files:**
- Modify: `src/Parlance.Abstractions/Location.cs`
- Create: `tests/Parlance.CSharp.Workspace.Tests/LocationFilePathTests.cs`

This adds `string? FilePath = null` as the last parameter to the existing `Location` record in Abstractions. Because it's optional with a default, existing call sites (72 occurrences across 33 files) continue to compile unchanged. Record equality now includes `FilePath` but this is acceptable — no existing code uses `FilePath` in equality comparisons.

- [ ] **Step 1: Write tests for Location backward compat and new FilePath**

Create `tests/Parlance.CSharp.Workspace.Tests/LocationFilePathTests.cs`:

```csharp
using Parlance.Abstractions;

namespace Parlance.CSharp.Workspace.Tests;

public sealed class LocationFilePathTests
{
    [Fact]
    public void ExistingFourArgConstruction_StillWorks()
    {
        var loc = new Location(1, 2, 3, 4);

        Assert.Equal(1, loc.Line);
        Assert.Equal(2, loc.Column);
        Assert.Equal(3, loc.EndLine);
        Assert.Equal(4, loc.EndColumn);
        Assert.Null(loc.FilePath);
    }

    [Fact]
    public void FiveArgConstruction_SetsFilePath()
    {
        var loc = new Location(1, 2, 3, 4, "/path/to/file.cs");

        Assert.Equal("/path/to/file.cs", loc.FilePath);
    }

    [Fact]
    public void NamedFilePath_Works()
    {
        var loc = new Location(1, 2, 3, 4, FilePath: "/path/to/file.cs");

        Assert.Equal("/path/to/file.cs", loc.FilePath);
    }

    [Fact]
    public void Equality_WithSameFilePath()
    {
        var loc1 = new Location(1, 2, 3, 4, "/file.cs");
        var loc2 = new Location(1, 2, 3, 4, "/file.cs");

        Assert.Equal(loc1, loc2);
    }

    [Fact]
    public void Equality_DifferentFilePath_NotEqual()
    {
        var loc1 = new Location(1, 2, 3, 4, "/a.cs");
        var loc2 = new Location(1, 2, 3, 4, "/b.cs");

        Assert.NotEqual(loc1, loc2);
    }

    [Fact]
    public void Equality_NullVsSetFilePath_NotEqual()
    {
        var loc1 = new Location(1, 2, 3, 4);
        var loc2 = new Location(1, 2, 3, 4, "/file.cs");

        Assert.NotEqual(loc1, loc2);
    }

    [Fact]
    public void Equality_BothNullFilePath_Equal()
    {
        var loc1 = new Location(1, 2, 3, 4);
        var loc2 = new Location(1, 2, 3, 4);

        Assert.Equal(loc1, loc2);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/Parlance.CSharp.Workspace.Tests/Parlance.CSharp.Workspace.Tests.csproj --filter "LocationFilePathTests"
```

Expected: Build failure — `Location` doesn't have `FilePath` parameter yet. The 4-arg test should pass once updated, but tests referencing `FilePath` will fail.

- [ ] **Step 3: Add FilePath to Location**

Modify `src/Parlance.Abstractions/Location.cs` — change the record to:

```csharp
namespace Parlance.Abstractions;

public sealed record Location(
    int Line,
    int Column,
    int EndLine,
    int EndColumn,
    string? FilePath = null);
```

- [ ] **Step 4: Run new tests to verify they pass**

```bash
dotnet test tests/Parlance.CSharp.Workspace.Tests/Parlance.CSharp.Workspace.Tests.csproj --filter "LocationFilePathTests"
```

Expected: All 7 tests pass.

- [ ] **Step 5: Run ALL existing tests to verify backward compat**

```bash
dotnet test Parlance.sln
```

Expected: All 120+ existing tests still pass. The optional `FilePath` parameter doesn't break any existing `Location(line, col, endLine, endCol)` call sites.

- [ ] **Step 6: Commit**

```bash
git add src/Parlance.Abstractions/Location.cs \
        tests/Parlance.CSharp.Workspace.Tests/LocationFilePathTests.cs
git commit -m "Add optional FilePath to Location record in Abstractions"
```

---

## Chunk 2: Compilation Cache and Session Loading

### Task 7: Internal cache interface and state type

**Files:**
- Create: `src/Parlance.CSharp.Workspace/Internal/IProjectCompilationCache.cs`
- Create: `src/Parlance.CSharp.Workspace/Internal/ProjectCompilationState.cs`

These are internal types using Roslyn types directly. No tests needed for the interface or state type — they are exercised through the cache implementation tests.

- [ ] **Step 1: Implement IProjectCompilationCache**

Create `src/Parlance.CSharp.Workspace/Internal/IProjectCompilationCache.cs`:

```csharp
using Microsoft.CodeAnalysis;

namespace Parlance.CSharp.Workspace.Internal;

internal interface IProjectCompilationCache
{
    Task<ProjectCompilationState> GetAsync(Project project, CancellationToken ct = default);
    void MarkDirty(ProjectId projectId);
    void MarkAllDirty();
}
```

- [ ] **Step 2: Implement ProjectCompilationState**

Create `src/Parlance.CSharp.Workspace/Internal/ProjectCompilationState.cs`:

```csharp
using Microsoft.CodeAnalysis;

namespace Parlance.CSharp.Workspace.Internal;

internal sealed record ProjectCompilationState(Compilation Compilation);
```

- [ ] **Step 3: Verify build**

```bash
dotnet build src/Parlance.CSharp.Workspace/Parlance.CSharp.Workspace.csproj
```

Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/Parlance.CSharp.Workspace/Internal/IProjectCompilationCache.cs \
        src/Parlance.CSharp.Workspace/Internal/ProjectCompilationState.cs
git commit -m "Add internal IProjectCompilationCache interface and ProjectCompilationState"
```

---

### Task 8: ReportCompilationCache

**Files:**
- Create: `src/Parlance.CSharp.Workspace/Internal/ReportCompilationCache.cs`
- Create: `tests/Parlance.CSharp.Workspace.Tests/Internal/ReportCompilationCacheTests.cs`

Report cache: compile once on first request, cache forever. `MarkDirty`/`MarkAllDirty` are no-ops. Report mode is immutable by contract.

- [ ] **Step 1: Write tests for ReportCompilationCache**

Create `tests/Parlance.CSharp.Workspace.Tests/Internal/ReportCompilationCacheTests.cs`:

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Parlance.CSharp.Workspace.Internal;

namespace Parlance.CSharp.Workspace.Tests.Internal;

public sealed class ReportCompilationCacheTests
{
    private static (AdhocWorkspace Workspace, Project Project) CreateTestProject()
    {
        var workspace = new AdhocWorkspace();
        var projectInfo = ProjectInfo.Create(
            ProjectId.CreateNewId(), VersionStamp.Default, "Test", "Test", LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .WithParseOptions(new CSharpParseOptions(LanguageVersion.Latest));

        var project = workspace.AddProject(projectInfo);
        workspace.AddDocument(project.Id, "Class1.cs",
            SourceText.From("namespace Test; public class Class1 { }"));

        return (workspace, workspace.CurrentSolution.GetProject(project.Id)!);
    }

    [Fact]
    public async Task GetAsync_FirstCall_ReturnsCompilation()
    {
        var (workspace, project) = CreateTestProject();
        using var _ = workspace;

        var cache = new ReportCompilationCache();
        var state = await cache.GetAsync(project);

        Assert.NotNull(state.Compilation);
    }

    [Fact]
    public async Task GetAsync_SecondCall_ReturnsSameInstance()
    {
        var (workspace, project) = CreateTestProject();
        using var _ = workspace;

        var cache = new ReportCompilationCache();
        var state1 = await cache.GetAsync(project);
        var state2 = await cache.GetAsync(project);

        Assert.Same(state1, state2);
    }

    [Fact]
    public async Task MarkDirty_IsNoOp_StillReturnsCached()
    {
        var (workspace, project) = CreateTestProject();
        using var _ = workspace;

        var cache = new ReportCompilationCache();
        var state1 = await cache.GetAsync(project);

        cache.MarkDirty(project.Id);

        var state2 = await cache.GetAsync(project);
        Assert.Same(state1, state2);
    }

    [Fact]
    public async Task MarkAllDirty_IsNoOp_StillReturnsCached()
    {
        var (workspace, project) = CreateTestProject();
        using var _ = workspace;

        var cache = new ReportCompilationCache();
        var state1 = await cache.GetAsync(project);

        cache.MarkAllDirty();

        var state2 = await cache.GetAsync(project);
        Assert.Same(state1, state2);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/Parlance.CSharp.Workspace.Tests/Parlance.CSharp.Workspace.Tests.csproj --filter "ReportCompilationCacheTests"
```

Expected: Build failure — `ReportCompilationCache` doesn't exist.

- [ ] **Step 3: Implement ReportCompilationCache**

Create `src/Parlance.CSharp.Workspace/Internal/ReportCompilationCache.cs`:

```csharp
using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;

namespace Parlance.CSharp.Workspace.Internal;

internal sealed class ReportCompilationCache : IProjectCompilationCache
{
    private readonly ConcurrentDictionary<ProjectId, ProjectCompilationState> _cache = new();

    public async Task<ProjectCompilationState> GetAsync(Project project, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(project.Id, out var state))
            return state;

        var compilation = await project.GetCompilationAsync(ct)
            ?? throw new InvalidOperationException($"Compilation returned null for project '{project.Name}'");

        var newState = new ProjectCompilationState(compilation);
        return _cache.GetOrAdd(project.Id, newState);
    }

    public void MarkDirty(ProjectId projectId) { }

    public void MarkAllDirty() { }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test tests/Parlance.CSharp.Workspace.Tests/Parlance.CSharp.Workspace.Tests.csproj --filter "ReportCompilationCacheTests"
```

Expected: All 4 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Parlance.CSharp.Workspace/Internal/ReportCompilationCache.cs \
        tests/Parlance.CSharp.Workspace.Tests/Internal/ReportCompilationCacheTests.cs
git commit -m "Add ReportCompilationCache — compile once, cache forever"
```

---

### Task 9: ServerCompilationCache with dependency-aware cascade

**Files:**
- Create: `src/Parlance.CSharp.Workspace/Internal/ServerCompilationCache.cs`
- Create: `tests/Parlance.CSharp.Workspace.Tests/Internal/ServerCompilationCacheTests.cs`

Server cache: per-project dirty tracking with dependency cascade via `ProjectDependencyGraph`. When project A is marked dirty, all projects that transitively depend on A are also marked dirty. Reads recompile dirty projects on demand.

- [ ] **Step 1: Write tests for ServerCompilationCache**

Create `tests/Parlance.CSharp.Workspace.Tests/Internal/ServerCompilationCacheTests.cs`:

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Parlance.CSharp.Workspace.Internal;

namespace Parlance.CSharp.Workspace.Tests.Internal;

public sealed class ServerCompilationCacheTests
{
    private static (AdhocWorkspace Workspace, Project Project) CreateSingleProject()
    {
        var workspace = new AdhocWorkspace();
        var projectInfo = ProjectInfo.Create(
            ProjectId.CreateNewId(), VersionStamp.Default, "Test", "Test", LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .WithParseOptions(new CSharpParseOptions(LanguageVersion.Latest));

        var project = workspace.AddProject(projectInfo);
        workspace.AddDocument(project.Id, "Class1.cs",
            SourceText.From("namespace Test; public class Class1 { }"));

        return (workspace, workspace.CurrentSolution.GetProject(project.Id)!);
    }

    private static (AdhocWorkspace Workspace, Project ProjectA, Project ProjectB)
        CreateDependentProjects()
    {
        var workspace = new AdhocWorkspace();

        var projectAInfo = ProjectInfo.Create(
            ProjectId.CreateNewId(), VersionStamp.Default, "A", "A", LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .WithParseOptions(new CSharpParseOptions(LanguageVersion.Latest));

        var projectA = workspace.AddProject(projectAInfo);
        workspace.AddDocument(projectA.Id, "A.cs",
            SourceText.From("namespace A; public class ClassA { }"));

        var projectBInfo = ProjectInfo.Create(
            ProjectId.CreateNewId(), VersionStamp.Default, "B", "B", LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .WithParseOptions(new CSharpParseOptions(LanguageVersion.Latest))
            .WithProjectReferences([new ProjectReference(projectA.Id)]);

        var projectB = workspace.AddProject(projectBInfo);
        workspace.AddDocument(projectB.Id, "B.cs",
            SourceText.From("namespace B; public class ClassB : A.ClassA { }"));

        var solution = workspace.CurrentSolution;
        return (workspace, solution.GetProject(projectA.Id)!, solution.GetProject(projectB.Id)!);
    }

    [Fact]
    public async Task GetAsync_FirstCall_ReturnsCompilation()
    {
        var (workspace, project) = CreateSingleProject();
        using var _ = workspace;

        var cache = new ServerCompilationCache(() => workspace.CurrentSolution);
        var state = await cache.GetAsync(project);

        Assert.NotNull(state.Compilation);
    }

    [Fact]
    public async Task GetAsync_SecondCall_ReturnsSameInstance()
    {
        var (workspace, project) = CreateSingleProject();
        using var _ = workspace;

        var cache = new ServerCompilationCache(() => workspace.CurrentSolution);
        var state1 = await cache.GetAsync(project);
        var state2 = await cache.GetAsync(project);

        Assert.Same(state1, state2);
    }

    [Fact]
    public async Task MarkDirty_CausesRecompilation()
    {
        var (workspace, project) = CreateSingleProject();
        using var _ = workspace;

        var cache = new ServerCompilationCache(() => workspace.CurrentSolution);
        var state1 = await cache.GetAsync(project);

        cache.MarkDirty(project.Id);

        var state2 = await cache.GetAsync(project);
        Assert.NotSame(state1, state2);
    }

    [Fact]
    public async Task MarkDirty_CascadesToTransitiveDependents()
    {
        var (workspace, projectA, projectB) = CreateDependentProjects();
        using var _ = workspace;

        var cache = new ServerCompilationCache(() => workspace.CurrentSolution);

        await cache.GetAsync(projectA);
        var stateB1 = await cache.GetAsync(projectB);

        // Marking A dirty should cascade to B (B depends on A)
        cache.MarkDirty(projectA.Id);

        var stateB2 = await cache.GetAsync(projectB);
        Assert.NotSame(stateB1, stateB2);
    }

    [Fact]
    public async Task MarkDirty_DoesNotCascadeUpstream()
    {
        var (workspace, projectA, projectB) = CreateDependentProjects();
        using var _ = workspace;

        var cache = new ServerCompilationCache(() => workspace.CurrentSolution);

        var stateA1 = await cache.GetAsync(projectA);
        await cache.GetAsync(projectB);

        // Marking B dirty should NOT cascade to A (A does not depend on B)
        cache.MarkDirty(projectB.Id);

        var stateA2 = await cache.GetAsync(projectA);
        Assert.Same(stateA1, stateA2);
    }

    [Fact]
    public async Task MarkAllDirty_MarksAllProjects()
    {
        var (workspace, projectA, projectB) = CreateDependentProjects();
        using var _ = workspace;

        var cache = new ServerCompilationCache(() => workspace.CurrentSolution);
        var stateA1 = await cache.GetAsync(projectA);
        var stateB1 = await cache.GetAsync(projectB);

        cache.MarkAllDirty();

        var stateA2 = await cache.GetAsync(projectA);
        var stateB2 = await cache.GetAsync(projectB);
        Assert.NotSame(stateA1, stateA2);
        Assert.NotSame(stateB1, stateB2);
    }

    [Fact]
    public async Task ConcurrentQueries_DifferentProjects_BothSucceed()
    {
        var (workspace, projectA, projectB) = CreateDependentProjects();
        using var _ = workspace;

        var cache = new ServerCompilationCache(() => workspace.CurrentSolution);

        var task1 = cache.GetAsync(projectA);
        var task2 = cache.GetAsync(projectB);

        var results = await Task.WhenAll(task1, task2);
        Assert.NotNull(results[0].Compilation);
        Assert.NotNull(results[1].Compilation);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/Parlance.CSharp.Workspace.Tests/Parlance.CSharp.Workspace.Tests.csproj --filter "ServerCompilationCacheTests"
```

Expected: Build failure — `ServerCompilationCache` doesn't exist.

- [ ] **Step 3: Implement ServerCompilationCache**

Create `src/Parlance.CSharp.Workspace/Internal/ServerCompilationCache.cs`:

```csharp
using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;

namespace Parlance.CSharp.Workspace.Internal;

// Concurrent reads to the same dirty project may both recompile. This is correct
// (both produce equivalent compilations from the same project snapshot) but wasteful.
// Acceptable trade-off: avoids lock contention for the common case (different projects).
internal sealed class ServerCompilationCache(Func<Solution> solutionProvider) : IProjectCompilationCache
{
    private readonly ConcurrentDictionary<ProjectId, ProjectCompilationState> _cache = new();
    private readonly ConcurrentDictionary<ProjectId, byte> _dirty = new();

    public async Task<ProjectCompilationState> GetAsync(Project project, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(project.Id, out var state) && !_dirty.ContainsKey(project.Id))
            return state;

        var compilation = await project.GetCompilationAsync(ct)
            ?? throw new InvalidOperationException($"Compilation returned null for project '{project.Name}'");

        var newState = new ProjectCompilationState(compilation);
        _cache[project.Id] = newState;
        _dirty.TryRemove(project.Id, out _);
        return newState;
    }

    public void MarkDirty(ProjectId projectId)
    {
        _dirty[projectId] = 0;

        var solution = solutionProvider();
        var graph = solution.GetProjectDependencyGraph();
        foreach (var dependent in graph.GetProjectsThatTransitivelyDependOnThisProject(projectId))
            _dirty[dependent] = 0;
    }

    public void MarkAllDirty()
    {
        var solution = solutionProvider();
        foreach (var projectId in solution.ProjectIds)
            _dirty[projectId] = 0;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test tests/Parlance.CSharp.Workspace.Tests/Parlance.CSharp.Workspace.Tests.csproj --filter "ServerCompilationCacheTests"
```

Expected: All 7 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Parlance.CSharp.Workspace/Internal/ServerCompilationCache.cs \
        tests/Parlance.CSharp.Workspace.Tests/Internal/ServerCompilationCacheTests.cs
git commit -m "Add ServerCompilationCache with dependency-aware dirty cascade"
```

---

### Task 10: CSharpWorkspaceSession — loading, health, and lookup

**Files:**
- Create: `src/Parlance.CSharp.Workspace/CSharpWorkspaceSession.cs`
- Create: `tests/Parlance.CSharp.Workspace.Tests/Integration/TestPaths.cs`
- Create: `tests/Parlance.CSharp.Workspace.Tests/Integration/SolutionLoadingTests.cs`
- Create: `tests/Parlance.CSharp.Workspace.Tests/Integration/ProjectLoadingTests.cs`

This is the core session class. It includes:
- Private constructor (no public `new`)
- Static factory methods `OpenSolutionAsync` and `OpenProjectAsync`
- MSBuildLocator registration guard
- MSBuildWorkspace creation and diagnostic subscription
- Project mapping: Roslyn Project → CSharpProjectInfo (declared TFMs from evaluated MSBuild properties, active TFM from the Roslyn project MSBuildWorkspace actually evaluated, LangVersion from CSharpParseOptions)
- Health derivation, workspace diagnostics capture, project lookups, SnapshotVersion, DisposeAsync
- No RefreshAsync or file watcher yet — those are added in Chunk 3

**Important:** `MSBuildLocator.RegisterDefaults()` must be called before any MSBuild types are loaded into the AppDomain. The `EnsureMSBuildRegistered` method uses double-checked locking to ensure thread-safe one-time registration.

**Multi-targeting note:** MSBuildWorkspace surfaces multi-targeted SDK-style projects as one Roslyn `Project` per evaluated TFM. Roslyn disambiguates those public project names with `(<TFM>)`, so the mapping code should derive `ActiveTargetFramework` from the loaded Roslyn project and use a separate MSBuild evaluation only to collect the declared `TargetFrameworks` list.

- [ ] **Step 1: Create test helper for locating solution**

Create `tests/Parlance.CSharp.Workspace.Tests/Integration/TestPaths.cs`:

```csharp
namespace Parlance.CSharp.Workspace.Tests.Integration;

internal static class TestPaths
{
    public static string FindSolutionPath()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var sln = Path.Combine(dir, "Parlance.sln");
            if (File.Exists(sln)) return sln;
            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException(
            "Could not find Parlance.sln — run tests from within the repo");
    }

    public static string RepoRoot => Path.GetDirectoryName(FindSolutionPath())!;
}
```

- [ ] **Step 2: Write integration tests for solution loading**

Create `tests/Parlance.CSharp.Workspace.Tests/Integration/SolutionLoadingTests.cs`:

```csharp
using Parlance.CSharp.Workspace;

namespace Parlance.CSharp.Workspace.Tests.Integration;

public sealed class SolutionLoadingTests
{
    [Fact]
    public async Task OpenSolutionAsync_LoadsParlanceSln()
    {
        var solutionPath = TestPaths.FindSolutionPath();

        await using var session = await CSharpWorkspaceSession.OpenSolutionAsync(solutionPath);

        Assert.Equal(solutionPath, session.WorkspacePath);
        Assert.NotEmpty(session.Projects);
        Assert.Equal(1, session.SnapshotVersion);
    }

    [Fact]
    public async Task OpenSolutionAsync_ReportsHealthStatusAndDiagnostics()
    {
        var solutionPath = TestPaths.FindSolutionPath();

        await using var session = await CSharpWorkspaceSession.OpenSolutionAsync(solutionPath);

        Assert.True(session.Health.Status is WorkspaceLoadStatus.Loaded or WorkspaceLoadStatus.Degraded);
        Assert.Equal(session.Projects.Count, session.Health.Projects.Count);

        if (session.Health.Status is WorkspaceLoadStatus.Degraded)
            Assert.NotEmpty(session.Health.Diagnostics);
    }

    [Fact]
    public async Task OpenSolutionAsync_ContainsAbstractionsProject()
    {
        var solutionPath = TestPaths.FindSolutionPath();

        await using var session = await CSharpWorkspaceSession.OpenSolutionAsync(solutionPath);

        var abstractions = session.Projects.FirstOrDefault(p => p.Name == "Parlance.Abstractions");
        Assert.NotNull(abstractions);
        Assert.Equal(ProjectLoadStatus.Loaded, abstractions!.Status);
        Assert.Contains("net10.0", abstractions.TargetFrameworks);
        Assert.Equal("net10.0", abstractions.ActiveTargetFramework);
        Assert.NotNull(abstractions.LangVersion);
    }

    [Fact]
    public async Task OpenSolutionAsync_ContainsMultiTargetedProjectPerFramework()
    {
        var solutionPath = TestPaths.FindSolutionPath();
        var upstreamPath = Path.Combine(
            TestPaths.RepoRoot,
            "src",
            "Parlance.Analyzers.Upstream",
            "Parlance.Analyzers.Upstream.csproj");

        await using var session = await CSharpWorkspaceSession.OpenSolutionAsync(solutionPath);

        var upstreamProjects = session.Projects
            .Where(p => string.Equals(p.ProjectPath, upstreamPath, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Equal(2, upstreamProjects.Count);
        Assert.Contains(upstreamProjects, p => p.ActiveTargetFramework == "net8.0");
        Assert.Contains(upstreamProjects, p => p.ActiveTargetFramework == "net10.0");
        Assert.All(upstreamProjects, p =>
        {
            Assert.Equal(ProjectLoadStatus.Loaded, p.Status);
            Assert.Equal(["net8.0", "net10.0"], p.TargetFrameworks);
        });
    }

    [Fact]
    public async Task OpenSolutionAsync_GetProjectByPath()
    {
        var solutionPath = TestPaths.FindSolutionPath();
        var abstractionsPath = Path.Combine(
            TestPaths.RepoRoot, "src", "Parlance.Abstractions", "Parlance.Abstractions.csproj");

        await using var session = await CSharpWorkspaceSession.OpenSolutionAsync(solutionPath);

        var project = session.GetProjectByPath(abstractionsPath);
        Assert.NotNull(project);
        Assert.Equal("Parlance.Abstractions", project!.Name);
    }

    [Fact]
    public async Task OpenSolutionAsync_GetProjectByKey()
    {
        var solutionPath = TestPaths.FindSolutionPath();

        await using var session = await CSharpWorkspaceSession.OpenSolutionAsync(solutionPath);

        var first = session.Projects[0];
        var found = session.GetProject(first.Key);
        Assert.NotNull(found);
        Assert.Equal(first.Name, found!.Name);
    }

    [Fact]
    public async Task OpenSolutionAsync_GetProject_UnknownKey_ReturnsNull()
    {
        var solutionPath = TestPaths.FindSolutionPath();

        await using var session = await CSharpWorkspaceSession.OpenSolutionAsync(solutionPath);

        var found = session.GetProject(new WorkspaceProjectKey(Guid.NewGuid()));
        Assert.Null(found);
    }

    [Fact]
    public async Task OpenSolutionAsync_NotFound_ThrowsWorkspaceLoadException()
    {
        var ex = await Assert.ThrowsAsync<WorkspaceLoadException>(
            () => CSharpWorkspaceSession.OpenSolutionAsync("/nonexistent/path.sln"));

        Assert.Equal("/nonexistent/path.sln", ex.WorkspacePath);
    }

    [Fact]
    public async Task OpenSolutionAsync_ReportModeWithFileWatching_ThrowsArgumentException()
    {
        var solutionPath = TestPaths.FindSolutionPath();
        var options = new WorkspaceOpenOptions(
            Mode: WorkspaceMode.Report,
            EnableFileWatching: true);

        await Assert.ThrowsAsync<ArgumentException>(
            () => CSharpWorkspaceSession.OpenSolutionAsync(solutionPath, options));
    }
}
```

- [ ] **Step 3: Write integration tests for project loading**

Create `tests/Parlance.CSharp.Workspace.Tests/Integration/ProjectLoadingTests.cs`:

```csharp
using Parlance.CSharp.Workspace;

namespace Parlance.CSharp.Workspace.Tests.Integration;

public sealed class ProjectLoadingTests
{
    [Fact]
    public async Task OpenProjectAsync_LoadsSingleProject()
    {
        var projectPath = Path.Combine(
            TestPaths.RepoRoot, "src", "Parlance.Abstractions", "Parlance.Abstractions.csproj");

        await using var session = await CSharpWorkspaceSession.OpenProjectAsync(projectPath);

        Assert.Equal(projectPath, session.WorkspacePath);
        Assert.Single(session.Projects);
        Assert.Equal("Parlance.Abstractions", session.Projects[0].Name);
        Assert.Equal(ProjectLoadStatus.Loaded, session.Projects[0].Status);
        Assert.Equal(WorkspaceLoadStatus.Loaded, session.Health.Status);
        Assert.Empty(session.Health.Diagnostics);
    }

    [Fact]
    public async Task OpenProjectAsync_NotFound_ThrowsWorkspaceLoadException()
    {
        var ex = await Assert.ThrowsAsync<WorkspaceLoadException>(
            () => CSharpWorkspaceSession.OpenProjectAsync("/nonexistent/project.csproj"));

        Assert.Equal("/nonexistent/project.csproj", ex.WorkspacePath);
    }

    [Fact]
    public async Task OpenProjectAsync_MultiTargetedProject_ReportsPerFrameworkActiveTargetFramework()
    {
        var projectPath = Path.Combine(
            TestPaths.RepoRoot, "src", "Parlance.Analyzers.Upstream", "Parlance.Analyzers.Upstream.csproj");

        await using var session = await CSharpWorkspaceSession.OpenProjectAsync(projectPath);

        Assert.Equal(2, session.Projects.Count);
        Assert.Contains(session.Projects, p => p.ActiveTargetFramework == "net8.0");
        Assert.Contains(session.Projects, p => p.ActiveTargetFramework == "net10.0");
        Assert.All(session.Projects, p =>
        {
            Assert.Equal(ProjectLoadStatus.Loaded, p.Status);
            Assert.Equal(["net8.0", "net10.0"], p.TargetFrameworks);
        });
    }
}
```

- [ ] **Step 4: Run tests to verify they fail**

```bash
dotnet test tests/Parlance.CSharp.Workspace.Tests/Parlance.CSharp.Workspace.Tests.csproj --filter "SolutionLoadingTests|ProjectLoadingTests"
```

Expected: Build failure — `CSharpWorkspaceSession` doesn't exist.

- [ ] **Step 5: Implement CSharpWorkspaceSession**

Create `src/Parlance.CSharp.Workspace/CSharpWorkspaceSession.cs`:

```csharp
using Microsoft.Build.Evaluation;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Parlance.CSharp.Workspace.Internal;

namespace Parlance.CSharp.Workspace;

public sealed class CSharpWorkspaceSession : IAsyncDisposable
{
    private readonly MSBuildWorkspace _workspace;
    private readonly IProjectCompilationCache _cache;
    private readonly ILogger<CSharpWorkspaceSession> _logger;
    private readonly WorkspaceMode _mode;
    private long _snapshotVersion = 1;

    private CSharpWorkspaceSession(
        string workspacePath,
        MSBuildWorkspace workspace,
        CSharpWorkspaceHealth health,
        IReadOnlyList<CSharpProjectInfo> projects,
        IProjectCompilationCache cache,
        WorkspaceMode mode,
        ILoggerFactory loggerFactory)
    {
        WorkspacePath = workspacePath;
        _workspace = workspace;
        Health = health;
        Projects = projects;
        _cache = cache;
        _mode = mode;
        _logger = loggerFactory.CreateLogger<CSharpWorkspaceSession>();
    }

    public string WorkspacePath { get; }

    public long SnapshotVersion => Interlocked.Read(ref _snapshotVersion);

    public CSharpWorkspaceHealth Health { get; }

    public IReadOnlyList<CSharpProjectInfo> Projects { get; }

    public CSharpProjectInfo? GetProject(WorkspaceProjectKey key) =>
        Projects.FirstOrDefault(p => p.Key == key);

    public CSharpProjectInfo? GetProjectByPath(string projectPath) =>
        Projects.FirstOrDefault(p =>
            string.Equals(p.ProjectPath, projectPath, StringComparison.OrdinalIgnoreCase));

    public static async Task<CSharpWorkspaceSession> OpenSolutionAsync(
        string solutionPath,
        WorkspaceOpenOptions? options = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionPath);
        if (!File.Exists(solutionPath))
            throw new WorkspaceLoadException(
                $"Solution file not found: {solutionPath}", solutionPath);

        return await LoadAsync(
            solutionPath,
            (ws, token) => ws.OpenSolutionAsync(solutionPath, cancellationToken: token),
            options ?? new WorkspaceOpenOptions(),
            ct);
    }

    public static async Task<CSharpWorkspaceSession> OpenProjectAsync(
        string projectPath,
        WorkspaceOpenOptions? options = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        if (!File.Exists(projectPath))
            throw new WorkspaceLoadException(
                $"Project file not found: {projectPath}", projectPath);

        return await LoadAsync(
            projectPath,
            async (ws, token) =>
            {
                var project = await ws.OpenProjectAsync(projectPath, cancellationToken: token);
                return project.Solution;
            },
            options ?? new WorkspaceOpenOptions(),
            ct);
    }

    public ValueTask DisposeAsync()
    {
        _workspace.Dispose();
        _logger.LogInformation("Workspace session disposed: {Path}", WorkspacePath);
        return ValueTask.CompletedTask;
    }

    private static async Task<CSharpWorkspaceSession> LoadAsync(
        string workspacePath,
        Func<MSBuildWorkspace, CancellationToken, Task<Solution>> loadSolution,
        WorkspaceOpenOptions options,
        CancellationToken ct)
    {
        // Eagerly validate options (triggers ArgumentException for Report + FileWatching=true)
        _ = options.FileWatchingEnabled;

        EnsureMSBuildRegistered();

        var loggerFactory = options.LoggerFactory ?? NullLoggerFactory.Instance;
        var logger = loggerFactory.CreateLogger<CSharpWorkspaceSession>();

        logger.LogInformation("Opening workspace: {Path}, Mode: {Mode}", workspacePath, options.Mode);

        var workspaceDiagnostics = new List<WorkspaceDiagnostic>();
        var diagnosticsLock = new Lock();
        var workspace = MSBuildWorkspace.Create();

        workspace.WorkspaceFailed += (_, args) =>
        {
            var severity = args.Diagnostic.Kind is WorkspaceDiagnosticKind.Failure
                ? WorkspaceDiagnosticSeverity.Error
                : WorkspaceDiagnosticSeverity.Warning;

            var diagnostic = new WorkspaceDiagnostic(
                args.Diagnostic.Kind.ToString(), args.Diagnostic.Message, severity);

            lock (diagnosticsLock)
            {
                workspaceDiagnostics.Add(diagnostic);
            }

            logger.LogWarning("Workspace diagnostic: {Message}", args.Diagnostic.Message);
        };

        Solution solution;
        try
        {
            solution = await loadSolution(workspace, ct);
        }
        catch (Exception ex) when (ex is not WorkspaceLoadException)
        {
            workspace.Dispose();
            throw new WorkspaceLoadException(
                $"Failed to load workspace: {ex.Message}", workspacePath, ex);
        }

        WorkspaceDiagnostic[] diagnosticsSnapshot;
        lock (diagnosticsLock)
        {
            diagnosticsSnapshot = [.. workspaceDiagnostics];
        }

        // WorkspaceFailed diagnostics are surfaced on health. Project-level diagnostics
        // remain reserved for failures we can attribute during project mapping.
        var projects = MapProjects(solution, logger);
        var health = CSharpWorkspaceHealth.FromProjects(projects, diagnosticsSnapshot);

        IProjectCompilationCache cache = options.Mode switch
        {
            WorkspaceMode.Server => new ServerCompilationCache(() => workspace.CurrentSolution),
            _ => new ReportCompilationCache()
        };

        logger.LogInformation(
            "Workspace loaded: {Status}, {Count} project(s)", health.Status, projects.Count);

        return new CSharpWorkspaceSession(
            workspacePath, workspace, health, projects, cache, options.Mode, loggerFactory);
    }

    private static IReadOnlyList<CSharpProjectInfo> MapProjects(
        Solution solution,
        ILogger logger)
    {
        var projects = new List<CSharpProjectInfo>();

        foreach (var project in solution.Projects)
        {
            try
            {
                var (tfms, activeTfm) = EvaluateFrameworkInfo(project);
                var langVersion = (project.ParseOptions as CSharpParseOptions)
                    ?.LanguageVersion.ToDisplayString();

                projects.Add(new CSharpProjectInfo(
                    Key: new WorkspaceProjectKey(project.Id.Id),
                    Name: project.Name,
                    ProjectPath: project.FilePath ?? "",
                    TargetFrameworks: tfms,
                    ActiveTargetFramework: activeTfm,
                    LangVersion: langVersion,
                    Status: ProjectLoadStatus.Loaded,
                    Diagnostics: []));

                logger.LogDebug(
                    "Loaded project: {Name} ({TFM})", project.Name, activeTfm);
            }
            catch (Exception ex)
            {
                projects.Add(new CSharpProjectInfo(
                    Key: new WorkspaceProjectKey(project.Id.Id),
                    Name: project.Name,
                    ProjectPath: project.FilePath ?? "",
                    TargetFrameworks: [],
                    ActiveTargetFramework: null,
                    LangVersion: null,
                    Status: ProjectLoadStatus.Failed,
                    Diagnostics: [new WorkspaceDiagnostic(
                        "MapError", ex.Message, WorkspaceDiagnosticSeverity.Error)]));

                logger.LogError(ex, "Failed to map project: {Name}", project.Name);
            }
        }

        return projects;
    }

    private static (IReadOnlyList<string> TargetFrameworks, string ActiveTargetFramework)
        EvaluateFrameworkInfo(Microsoft.CodeAnalysis.Project project)
    {
        if (project.FilePath is null || !File.Exists(project.FilePath))
            throw new InvalidOperationException($"Project file path unavailable for {project.Name}");

        var projectCollection = new ProjectCollection();
        try
        {
            var msbuildProject = projectCollection.LoadProject(project.FilePath);
            var targetFramework = msbuildProject.GetPropertyValue("TargetFramework");
            var targetFrameworks = msbuildProject.GetPropertyValue("TargetFrameworks");

            var parsedTargetFrameworks = !string.IsNullOrWhiteSpace(targetFrameworks)
                ? targetFrameworks.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : !string.IsNullOrWhiteSpace(targetFramework)
                    ? [targetFramework]
                    : [];

            if (parsedTargetFrameworks.Length == 0)
                throw new InvalidOperationException(
                    $"No TargetFramework information found for {project.Name}");

            var activeTargetFramework = parsedTargetFrameworks.Length == 1
                ? parsedTargetFrameworks[0]
                : ResolveActiveTargetFramework(project, parsedTargetFrameworks)
                    ?? throw new InvalidOperationException(
                        $"Could not determine the evaluated TargetFramework for {project.Name}");

            return (parsedTargetFrameworks, activeTargetFramework);
        }
        finally
        {
            projectCollection.UnloadAllProjects();
        }
    }

    private static string? ResolveActiveTargetFramework(
        Microsoft.CodeAnalysis.Project project,
        IReadOnlyList<string> targetFrameworks)
    {
        foreach (var tfm in targetFrameworks)
        {
            if (project.Name.EndsWith($"({tfm})", StringComparison.Ordinal))
                return tfm;
        }

        if (project.OutputFilePath is { Length: > 0 } outputFilePath)
        {
            foreach (var tfm in targetFrameworks)
            {
                if (outputFilePath.Contains(
                        $"{Path.DirectorySeparatorChar}{tfm}{Path.DirectorySeparatorChar}",
                        StringComparison.OrdinalIgnoreCase) ||
                    outputFilePath.Contains(
                        $"{Path.AltDirectorySeparatorChar}{tfm}{Path.AltDirectorySeparatorChar}",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return tfm;
                }
            }
        }

        return null;
    }

    private static readonly Lock _msbuildLock = new();

    private static void EnsureMSBuildRegistered()
    {
        if (MSBuildLocator.IsRegistered) return;

        lock (_msbuildLock)
        {
            if (MSBuildLocator.IsRegistered) return;
            MSBuildLocator.RegisterDefaults();
        }
    }
}
```

- [ ] **Step 6: Run integration tests**

```bash
dotnet test tests/Parlance.CSharp.Workspace.Tests/Parlance.CSharp.Workspace.Tests.csproj --filter "SolutionLoadingTests|ProjectLoadingTests"
```

Expected: All 12 tests pass. Solution loading may take a few seconds — this is normal for MSBuildWorkspace.

- [ ] **Step 7: Run ALL tests to verify nothing is broken**

```bash
dotnet test Parlance.sln
```

Expected: All existing tests plus all new workspace tests pass.

- [ ] **Step 8: Commit**

```bash
git add src/Parlance.CSharp.Workspace/CSharpWorkspaceSession.cs \
        tests/Parlance.CSharp.Workspace.Tests/Integration/TestPaths.cs \
        tests/Parlance.CSharp.Workspace.Tests/Integration/SolutionLoadingTests.cs \
        tests/Parlance.CSharp.Workspace.Tests/Integration/ProjectLoadingTests.cs
git commit -m "Add CSharpWorkspaceSession with solution/project loading and health reporting"
```

---

## Chunk 3: File Watching, RefreshAsync, and Final Verification

### Task 11: WorkspaceFileWatcher (internal)

**Files:**
- Create: `src/Parlance.CSharp.Workspace/Internal/WorkspaceFileWatcher.cs`
- Create: `tests/Parlance.CSharp.Workspace.Tests/Integration/FileWatcherTests.cs`

Internal `FileSystemWatcher` wrapper that monitors the parent directories of already-loaded `.cs` documents, including linked files outside the project directory, debounces rapid saves, and invokes a callback with the changed file paths. Created/deleted/renamed files are intentionally ignored in Milestone 1 because they are structural changes requiring session rebuild.

- [ ] **Step 1: Write tests for WorkspaceFileWatcher**

Create `tests/Parlance.CSharp.Workspace.Tests/Integration/FileWatcherTests.cs`:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Parlance.CSharp.Workspace.Internal;

namespace Parlance.CSharp.Workspace.Tests.Integration;

public sealed class FileWatcherTests
{
    [Fact]
    public async Task DetectsTrackedFileChange()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"parlance-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "Test.cs");
            await File.WriteAllTextAsync(filePath, "// original");

            var tcs = new TaskCompletionSource<IReadOnlyList<string>>();
            await using var watcher = new WorkspaceFileWatcher(
                [filePath],
                changes => { tcs.TrySetResult(changes); return Task.CompletedTask; },
                NullLoggerFactory.Instance);

            await Task.Delay(200); // Let watcher start
            await File.WriteAllTextAsync(filePath, "// modified");

            var result = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Contains(result, p => p.EndsWith("Test.cs"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task DetectsTrackedFileChangeInLinkedDirectory()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"parlance-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(rootDir);
        try
        {
            var sharedDir = Path.Combine(rootDir, "Shared");
            Directory.CreateDirectory(sharedDir);

            var linkedFile = Path.Combine(sharedDir, "Shared.cs");
            await File.WriteAllTextAsync(linkedFile, "// original");

            var tcs = new TaskCompletionSource<IReadOnlyList<string>>();
            await using var watcher = new WorkspaceFileWatcher(
                [linkedFile],
                changes => { tcs.TrySetResult(changes); return Task.CompletedTask; },
                NullLoggerFactory.Instance);

            await Task.Delay(200);
            await File.WriteAllTextAsync(linkedFile, "// modified");

            var result = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Contains(linkedFile, result);
        }
        finally
        {
            Directory.Delete(rootDir, true);
        }
    }

    [Fact]
    public async Task IgnoresUntrackedCsFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"parlance-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var trackedFile = Path.Combine(dir, "Tracked.cs");
            await File.WriteAllTextAsync(trackedFile, "// tracked");

            var untrackedFile = Path.Combine(dir, "Untracked.cs");
            await File.WriteAllTextAsync(untrackedFile, "// original");

            var callbackFired = false;
            await using var watcher = new WorkspaceFileWatcher(
                [trackedFile],
                _ => { callbackFired = true; return Task.CompletedTask; },
                NullLoggerFactory.Instance);

            await Task.Delay(200);
            await File.WriteAllTextAsync(untrackedFile, "// modified");

            await Task.Delay(1000);
            Assert.False(callbackFired);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task IgnoresNonCsFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"parlance-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var trackedFile = Path.Combine(dir, "Tracked.cs");
            await File.WriteAllTextAsync(trackedFile, "// tracked");

            var txtFile = Path.Combine(dir, "readme.txt");
            await File.WriteAllTextAsync(txtFile, "original");

            var callbackFired = false;
            await using var watcher = new WorkspaceFileWatcher(
                [trackedFile],
                _ => { callbackFired = true; return Task.CompletedTask; },
                NullLoggerFactory.Instance);

            await Task.Delay(200);
            await File.WriteAllTextAsync(txtFile, "modified");

            await Task.Delay(1000); // Wait longer than debounce window
            Assert.False(callbackFired);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task DisposeStopsWatching()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"parlance-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "Test.cs");
            await File.WriteAllTextAsync(filePath, "// original");

            var callbackCount = 0;
            var watcher = new WorkspaceFileWatcher(
                [filePath],
                _ => { Interlocked.Increment(ref callbackCount); return Task.CompletedTask; },
                NullLoggerFactory.Instance);

            await watcher.DisposeAsync();

            await File.WriteAllTextAsync(filePath, "// modified after dispose");
            await Task.Delay(1000);
            Assert.Equal(0, callbackCount);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/Parlance.CSharp.Workspace.Tests/Parlance.CSharp.Workspace.Tests.csproj --filter "FileWatcherTests"
```

Expected: Build failure — `WorkspaceFileWatcher` doesn't exist.

- [ ] **Step 3: Implement WorkspaceFileWatcher**

Create `src/Parlance.CSharp.Workspace/Internal/WorkspaceFileWatcher.cs`:

```csharp
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Parlance.CSharp.Workspace.Internal;

internal sealed class WorkspaceFileWatcher : IAsyncDisposable
{
    private readonly FileSystemWatcher[] _watchers;
    private readonly Func<IReadOnlyList<string>, Task> _onChanges;
    private readonly Timer _debounceTimer;
    private readonly ConcurrentBag<string> _pendingChanges = new();
    private readonly HashSet<string> _watchedFiles;
    private readonly SemaphoreSlim _processingLock = new(1, 1);
    private readonly Lock _taskLock = new();
    private readonly ILogger<WorkspaceFileWatcher> _logger;
    private Task _processingTask = Task.CompletedTask;
    private bool _disposed;
    private const int DebounceMs = 300;

    public WorkspaceFileWatcher(
        IReadOnlyList<string> watchedFiles,
        Func<IReadOnlyList<string>, Task> onChanges,
        ILoggerFactory loggerFactory)
    {
        _onChanges = onChanges;
        _watchedFiles = new HashSet<string>(watchedFiles, StringComparer.OrdinalIgnoreCase);
        _logger = loggerFactory.CreateLogger<WorkspaceFileWatcher>();
        _debounceTimer = new Timer(OnDebounceElapsed, null, Timeout.Infinite, Timeout.Infinite);

        _watchers = _watchedFiles
            .Select(Path.GetDirectoryName)
            .OfType<string>()
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(dir =>
            {
                var watcher = new FileSystemWatcher(dir, "*.cs")
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                    IncludeSubdirectories = true,
                    EnableRaisingEvents = true
                };
                watcher.Changed += OnFileChanged;
                return watcher;
            })
            .ToArray();

        _logger.LogInformation(
            "File watcher started: {Count} director(ies) for {FileCount} tracked file(s)",
            _watchers.Length,
            _watchedFiles.Count);
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (_disposed || !_watchedFiles.Contains(e.FullPath))
            return;

        _pendingChanges.Add(e.FullPath);
        _debounceTimer.Change(DebounceMs, Timeout.Infinite);
        _logger.LogDebug("File change detected: {Path}", e.FullPath);
    }

    private void OnDebounceElapsed(object? state)
    {
        if (_disposed) return;

        lock (_taskLock)
        {
            if (_disposed) return;
            _processingTask = ProcessPendingChangesAsync();
        }
    }

    private async Task ProcessPendingChangesAsync()
    {
        await _processingLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;

            var changes = new List<string>();
            while (_pendingChanges.TryTake(out var path))
                changes.Add(path);

            if (changes.Count == 0) return;

            var distinct = changes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            _logger.LogInformation("Processing {Count} file change(s)", distinct.Count);

            try
            {
                await _onChanges(distinct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing file changes");
            }
        }
        finally
        {
            _processingLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var watcher in _watchers)
            watcher.EnableRaisingEvents = false;

        _disposed = true;
        _debounceTimer.Change(Timeout.Infinite, Timeout.Infinite);
        _debounceTimer.Dispose();

        Task processingTask;
        lock (_taskLock)
        {
            processingTask = _processingTask;
        }

        await processingTask.ConfigureAwait(false);

        foreach (var watcher in _watchers)
            watcher.Dispose();

        _processingLock.Dispose();
        _logger.LogInformation("File watcher stopped");
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test tests/Parlance.CSharp.Workspace.Tests/Parlance.CSharp.Workspace.Tests.csproj --filter "FileWatcherTests"
```

Expected: All 5 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Parlance.CSharp.Workspace/Internal/WorkspaceFileWatcher.cs \
        tests/Parlance.CSharp.Workspace.Tests/Integration/FileWatcherTests.cs
git commit -m "Add WorkspaceFileWatcher with debounced .cs file monitoring"
```

---

### Task 12: Wire file watcher and RefreshAsync into CSharpWorkspaceSession

**Files:**
- Modify: `src/Parlance.CSharp.Workspace/CSharpWorkspaceSession.cs`
- Create: `tests/Parlance.CSharp.Workspace.Tests/Integration/RefreshTests.cs`

This task adds `RefreshAsync`, the file watcher integration, and the `OnFileChanges` callback to the session. The watcher only tracks text changes for already-loaded documents; structural changes remain out of scope and require reopening the session.

**Changes to CSharpWorkspaceSession.cs:**

1. Add `_loggerFactory` and `_watcher` fields
2. Store `loggerFactory` in constructor
3. Add `StartFileWatching` internal method
4. Add `OnFileChanges` private callback
5. Add `RefreshAsync` public method
6. Update `DisposeAsync` to dispose watcher
7. Update `LoadAsync` to call `StartFileWatching`

- [ ] **Step 1: Write integration tests for RefreshAsync**

Create `tests/Parlance.CSharp.Workspace.Tests/Integration/RefreshTests.cs`:

```csharp
using Parlance.CSharp.Workspace;

namespace Parlance.CSharp.Workspace.Tests.Integration;

public sealed class RefreshTests
{
    [Fact]
    public async Task RefreshAsync_ReportMode_ThrowsInvalidOperation()
    {
        var projectPath = Path.Combine(
            TestPaths.RepoRoot, "src", "Parlance.Abstractions", "Parlance.Abstractions.csproj");

        await using var session = await CSharpWorkspaceSession.OpenProjectAsync(projectPath);

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.RefreshAsync());
    }

    [Fact]
    public async Task RefreshAsync_ServerMode_NoChanges_VersionUnchanged()
    {
        var projectPath = Path.Combine(
            TestPaths.RepoRoot, "src", "Parlance.Abstractions", "Parlance.Abstractions.csproj");
        var options = new WorkspaceOpenOptions(
            Mode: WorkspaceMode.Server,
            EnableFileWatching: false);

        await using var session = await CSharpWorkspaceSession.OpenProjectAsync(projectPath, options);

        Assert.Equal(1, session.SnapshotVersion);
        await session.RefreshAsync();
        Assert.Equal(1, session.SnapshotVersion);
    }

    [Fact]
    public async Task RefreshAsync_DetectsSourceTextChange()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"parlance-refresh-{Guid.NewGuid():N}");
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
                EnableFileWatching: false);

            await using var session = await CSharpWorkspaceSession.OpenProjectAsync(csproj, options);
            Assert.Equal(1, session.SnapshotVersion);

            // Modify source on disk
            await File.WriteAllTextAsync(sourceFile,
                "namespace Test; public class Class1 { public int X { get; } }");

            await session.RefreshAsync();

            Assert.True(session.SnapshotVersion > 1,
                "SnapshotVersion should increment after detecting source text change");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task RefreshAsync_StructuralChange_NotDetected()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"parlance-refresh-{Guid.NewGuid():N}");
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
                EnableFileWatching: false);

            await using var session = await CSharpWorkspaceSession.OpenProjectAsync(csproj, options);

            // Add a new .cs file (structural change — NOT detected by RefreshAsync)
            var newFile = Path.Combine(tempDir, "Class2.cs");
            await File.WriteAllTextAsync(newFile, "namespace Test; public class Class2 { }");

            await session.RefreshAsync();

            // Version should NOT change — RefreshAsync only detects source text changes
            // to already-loaded documents, not new files
            Assert.Equal(1, session.SnapshotVersion);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/Parlance.CSharp.Workspace.Tests/Parlance.CSharp.Workspace.Tests.csproj --filter "RefreshTests"
```

Expected: Build failure — `RefreshAsync` doesn't exist on `CSharpWorkspaceSession`.

- [ ] **Step 3: Add fields and update constructor in CSharpWorkspaceSession**

In `src/Parlance.CSharp.Workspace/CSharpWorkspaceSession.cs`, add these fields after the existing ones:

```csharp
    private readonly ILoggerFactory _loggerFactory;
    private WorkspaceFileWatcher? _watcher;
```

Update the constructor to store `loggerFactory` — the only change from the Task 9 constructor is adding the `_loggerFactory = loggerFactory;` line (and the `ILoggerFactory loggerFactory` parameter which was already present):

```csharp
    private CSharpWorkspaceSession(
        string workspacePath,
        MSBuildWorkspace workspace,
        CSharpWorkspaceHealth health,
        IReadOnlyList<CSharpProjectInfo> projects,
        IProjectCompilationCache cache,
        WorkspaceMode mode,
        ILoggerFactory loggerFactory)
    {
        WorkspacePath = workspacePath;
        _workspace = workspace;
        Health = health;
        Projects = projects;
        _cache = cache;
        _mode = mode;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<CSharpWorkspaceSession>();
    }
```

- [ ] **Step 4: Add RefreshAsync method**

Add after `GetProjectByPath`:

```csharp
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        if (_mode is WorkspaceMode.Report)
            throw new InvalidOperationException("RefreshAsync is not supported in Report mode");

        var solution = _workspace.CurrentSolution;
        var affectedProjects = new HashSet<ProjectId>();
        var hasChanges = false;

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

                var newText = Microsoft.CodeAnalysis.Text.SourceText.From(
                    diskContent, currentText.Encoding);
                solution = solution.WithDocumentText(document.Id, newText);
                affectedProjects.Add(project.Id);
                hasChanges = true;
            }
        }

        if (!hasChanges)
        {
            _logger.LogDebug("RefreshAsync: no changes detected");
            return;
        }

        if (_workspace.TryApplyChanges(solution))
        {
            foreach (var projectId in affectedProjects)
                _cache.MarkDirty(projectId);

            Interlocked.Increment(ref _snapshotVersion);
            _logger.LogInformation(
                "RefreshAsync: {Count} project(s) updated, SnapshotVersion={Version}",
                affectedProjects.Count, SnapshotVersion);
        }
        else
        {
            _logger.LogWarning("RefreshAsync: failed to apply changes — concurrent modification");
        }
    }
```

- [ ] **Step 5: Add OnFileChanges callback and StartFileWatching**

Add after `RefreshAsync`:

```csharp
    internal void StartFileWatching(
        IReadOnlyList<string> documentPaths)
    {
        _watcher = new WorkspaceFileWatcher(
            documentPaths,
            OnFileChanges,
            _loggerFactory);
    }

    private async Task OnFileChanges(IReadOnlyList<string> changedPaths)
    {
        var solution = _workspace.CurrentSolution;
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
                : System.Text.Encoding.UTF8;
            var newText = Microsoft.CodeAnalysis.Text.SourceText.From(content, encoding);
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

        if (_workspace.TryApplyChanges(solution))
        {
            foreach (var projectId in affectedProjects)
                _cache.MarkDirty(projectId);

            Interlocked.Increment(ref _snapshotVersion);
            _logger.LogInformation(
                "File changes applied: {Count} file(s), SnapshotVersion={Version}",
                changedPaths.Count, SnapshotVersion);
        }
        else
        {
            _logger.LogWarning("Failed to apply file changes — concurrent modification");
        }
    }
```

- [ ] **Step 6: Update DisposeAsync to dispose watcher**

Replace the existing `DisposeAsync`:

```csharp
    public async ValueTask DisposeAsync()
    {
        if (_watcher is not null)
            await _watcher.DisposeAsync();

        // Cache is released with the session — no explicit clear needed
        _workspace.Dispose();
        _logger.LogInformation("Workspace session disposed: {Path}", WorkspacePath);
    }
```

- [ ] **Step 7: Update LoadAsync to start file watcher**

In `LoadAsync`, replace the final `return new CSharpWorkspaceSession(workspacePath, workspace, health, projects, cache, options.Mode, loggerFactory);` with:

```csharp
        var session = new CSharpWorkspaceSession(
            workspacePath, workspace, health, projects, cache, options.Mode, loggerFactory);

        if (options.FileWatchingEnabled)
        {
            var documentPaths = solution.Projects
                .SelectMany(p => p.Documents)
                .Select(d => d.FilePath)
                .OfType<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Watch the directories of loaded documents so linked files stay live too.
            session.StartFileWatching(documentPaths);
        }

        return session;
```

- [ ] **Step 8: Run RefreshAsync tests**

```bash
dotnet test tests/Parlance.CSharp.Workspace.Tests/Parlance.CSharp.Workspace.Tests.csproj --filter "RefreshTests"
```

Expected: All 4 tests pass.

- [ ] **Step 9: Run ALL tests**

```bash
dotnet test Parlance.sln
```

Expected: All tests pass (existing + all new workspace tests).

- [ ] **Step 10: Commit**

```bash
git add src/Parlance.CSharp.Workspace/CSharpWorkspaceSession.cs \
        tests/Parlance.CSharp.Workspace.Tests/Integration/RefreshTests.cs
git commit -m "Add RefreshAsync and file watcher integration to CSharpWorkspaceSession"
```

---

### Task 13: Final verification

- [ ] **Step 1: Run all tests**

```bash
dotnet test Parlance.sln
```

Expected: All tests pass — both the 120+ existing tests and all new workspace tests.

- [ ] **Step 2: Check formatting**

```bash
dotnet format Parlance.sln --verify-no-changes
```

Expected: No formatting violations. If any are found, fix them:

```bash
dotnet format Parlance.sln
```

Then re-run the verification and commit the formatting fixes.

- [ ] **Step 3: Build all projects**

```bash
dotnet build src/Parlance.CSharp.Workspace/Parlance.CSharp.Workspace.csproj
dotnet build src/Parlance.Cli/Parlance.Cli.csproj
```

Expected: Both build cleanly. The workspace project should not affect the CLI build.

- [ ] **Step 4: Final commit if formatting fixes were needed**

```bash
git add src/Parlance.CSharp.Workspace/ tests/Parlance.CSharp.Workspace.Tests/ src/Parlance.Abstractions/Location.cs Parlance.sln
git commit -m "Fix formatting"
```

Only commit if there were formatting changes.

- [ ] **Step 5: Review the implementation against acceptance criteria**

Verify against the spec's acceptance criteria:

| Criterion | How to verify |
|-----------|---------------|
| Project builds with MSBuild dependencies resolved | `dotnet build` succeeded in Step 3 |
| Core type compiles with designed API surface | All model types match spec signatures |
| API design self-evident from type signatures | Review public API in CSharpWorkspaceSession.cs |
| Existing tests continue to pass | `dotnet test Parlance.sln` in Step 1 |
| Loads Parlance.sln successfully | SolutionLoadingTests pass |
| Reports all projects with CSharpProjectInfo | SolutionLoadingTests.ContainsAbstractionsProject |
| Multi-targeted projects preserve declared TFMs plus the specific evaluated TFM for each loaded Roslyn project | SolutionLoadingTests.OpenSolutionAsync_ContainsMultiTargetedProjectPerFramework / ProjectLoadingTests.OpenProjectAsync_MultiTargetedProject_ReportsPerFrameworkActiveTargetFramework |
| Health report shows status, TFMs, language versions, and workspace diagnostics | SolutionLoadingTests.OpenSolutionAsync_ReportsHealthStatusAndDiagnostics |
| Workspace load diagnostics are surfaced on health without redefining completeness status | CSharpWorkspaceHealthTests.FromProjects_AllLoaded_WithWorkspaceWarning_StatusIsLoaded |
| Modify .cs file → workspace reflects change without reload | RefreshTests.RefreshAsync_DetectsSourceTextChange |
| SnapshotVersion increments on change | RefreshTests.RefreshAsync_DetectsSourceTextChange |
| Loading does not compile all projects upfront | Cache tests verify lazy compilation |
| File change marks right projects dirty (including dependents) | ServerCompilationCacheTests.MarkDirty_CascadesToTransitiveDependents |
| Concurrent queries to different projects don't block | ServerCompilationCacheTests.ConcurrentQueries_DifferentProjects_BothSucceed |
| File watcher observes already-loaded linked files outside the project directory | FileWatcherTests.DetectsTrackedFileChangeInLinkedDirectory |
| File watcher ignores non-.cs edits even when watched files exist in the same directory | FileWatcherTests.IgnoresNonCsFiles |
| File watcher ignores structural changes instead of observing-and-dropping them | FileWatcherTests.IgnoresUntrackedCsFiles |
| Loading produces structured log output | Logger.LogInformation calls throughout session loading |
