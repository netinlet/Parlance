using Parlance.CSharp.Workspace;

namespace Parlance.CSharp.Workspace.Tests.Integration;

public sealed class ProjectLoadingTests
{
    [Fact]
    public async Task OpenProjectAsync_LoadsSingleProject()
    {
        var projectPath = Path.Combine(
            TestPaths.RepoRoot, "src", "Parlance.Abstractions", "Parlance.Abstractions.csproj");

        await using var session = Assert.IsType<WorkspaceLoadResult.Success>(await CSharpWorkspaceSession.TryOpenProjectAsync(projectPath)).Session;

        Assert.Equal(projectPath, session.WorkspacePath);
        Assert.Single(session.Projects);
        Assert.Equal("Parlance.Abstractions", session.Projects[0].Name);
        Assert.Equal(ProjectLoadStatus.Loaded, session.Projects[0].Status);
        Assert.Equal(WorkspaceLoadStatus.Loaded, session.Health.Status);
        Assert.Empty(session.Health.Diagnostics);
    }

    [Fact]
    public async Task TryOpenProjectAsync_NotFound_ReturnsFailure()
    {
        var outcome = await CSharpWorkspaceSession.TryOpenProjectAsync("/nonexistent/project.csproj");

        var failure = Assert.IsType<WorkspaceLoadResult.Failure>(outcome);
        Assert.Equal("/nonexistent/project.csproj", failure.Reason.SolutionPath);
    }

    [Fact]
    public async Task OpenProjectAsync_ReportsTargetFramework()
    {
        var projectPath = Path.Combine(
            TestPaths.RepoRoot, "src", "Parlance.Abstractions", "Parlance.Abstractions.csproj");

        await using var session = Assert.IsType<WorkspaceLoadResult.Success>(await CSharpWorkspaceSession.TryOpenProjectAsync(projectPath)).Session;

        var project = session.Projects[0];
        Assert.Contains("net10.0", project.TargetFrameworks);
        Assert.Equal("net10.0", project.ActiveTargetFramework);
    }
}
