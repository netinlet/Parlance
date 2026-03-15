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
