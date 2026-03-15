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
    public async Task OpenProjectAsync_ReportsTargetFramework()
    {
        var projectPath = Path.Combine(
            TestPaths.RepoRoot, "src", "Parlance.Abstractions", "Parlance.Abstractions.csproj");

        await using var session = await CSharpWorkspaceSession.OpenProjectAsync(projectPath);

        var project = session.Projects[0];
        Assert.Contains("net10.0", project.TargetFrameworks);
        Assert.Equal("net10.0", project.ActiveTargetFramework);
    }
}
