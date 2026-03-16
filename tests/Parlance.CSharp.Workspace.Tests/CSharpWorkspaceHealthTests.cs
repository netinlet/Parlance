using System.Collections.Immutable;
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
            Diagnostics: [],
            ProjectReferences: []);

    [Fact]
    public void FromProjects_AllLoaded_NoDiagnostics_StatusIsLoaded()
    {
        ImmutableList<CSharpProjectInfo> projects =
        [
            MakeProject(ProjectLoadStatus.Loaded, "A"),
            MakeProject(ProjectLoadStatus.Loaded, "B")
        ];

        var health = CSharpWorkspaceHealth.FromProjects(projects);

        Assert.Equal(WorkspaceLoadStatus.Loaded, health.Status);
        Assert.Equal(2, health.Projects.Count);
        Assert.Empty(health.Diagnostics);
    }

    [Fact]
    public void FromProjects_AllFailed_StatusIsFailed()
    {
        ImmutableList<CSharpProjectInfo> projects =
        [
            MakeProject(ProjectLoadStatus.Failed, "A"),
            MakeProject(ProjectLoadStatus.Failed, "B")
        ];

        var health = CSharpWorkspaceHealth.FromProjects(projects);

        Assert.Equal(WorkspaceLoadStatus.Failed, health.Status);
    }

    [Fact]
    public void FromProjects_AllLoaded_WithWorkspaceWarning_StatusIsDegraded()
    {
        ImmutableList<CSharpProjectInfo> projects =
        [
            MakeProject(ProjectLoadStatus.Loaded, "A"),
            MakeProject(ProjectLoadStatus.Loaded, "B")
        ];
        ImmutableList<WorkspaceDiagnostic> diagnostics =
        [
            new("MSB3277", "Found conflicts", WorkspaceDiagnosticSeverity.Warning)
        ];

        var health = CSharpWorkspaceHealth.FromProjects(projects, diagnostics);

        Assert.Equal(WorkspaceLoadStatus.Degraded, health.Status);
        Assert.Single(health.Diagnostics);
    }

    [Fact]
    public void FromProjects_Mixed_StatusIsDegraded()
    {
        ImmutableList<CSharpProjectInfo> projects =
        [
            MakeProject(ProjectLoadStatus.Loaded, "A"),
            MakeProject(ProjectLoadStatus.Failed, "B")
        ];

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
        ImmutableList<WorkspaceDiagnostic> diags =
        [
            new("W001", "warn", WorkspaceDiagnosticSeverity.Warning)
        ];

        var info = new CSharpProjectInfo(
            Key: key,
            Name: "MyProject",
            ProjectPath: "/path/to/MyProject.csproj",
            TargetFrameworks: ["net8.0", "net10.0"],
            ActiveTargetFramework: "net10.0",
            LangVersion: "13.0",
            Status: ProjectLoadStatus.Loaded,
            Diagnostics: diags,
            ProjectReferences: ["SomeOtherProject"]);

        Assert.Equal(key, info.Key);
        Assert.Equal("MyProject", info.Name);
        Assert.Equal("/path/to/MyProject.csproj", info.ProjectPath);
        Assert.Equal(["net8.0", "net10.0"], info.TargetFrameworks);
        Assert.Equal("net10.0", info.ActiveTargetFramework);
        Assert.Equal("13.0", info.LangVersion);
        Assert.Equal(ProjectLoadStatus.Loaded, info.Status);
        Assert.Single(info.Diagnostics);
        Assert.Equal(["SomeOtherProject"], info.ProjectReferences);
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
            Diagnostics: [new WorkspaceDiagnostic("ERR", "Load failed", WorkspaceDiagnosticSeverity.Error)],
            ProjectReferences: []);

        Assert.Null(info.ActiveTargetFramework);
        Assert.Null(info.LangVersion);
        Assert.Empty(info.TargetFrameworks);
        Assert.Equal(ProjectLoadStatus.Failed, info.Status);
    }
}
