using Parlance.CSharp.Workspace;
using Parlance.CSharp.Workspace.Tests.Integration;

namespace Parlance.CSharp.Workspace.Tests;

public sealed class WorkspaceLoadResultTests
{
    [Fact]
    public void Failure_exposes_failure_record()
    {
        var failure = new WorkspaceLoadFailure("nope", "/x.sln");
        var outcome = new WorkspaceLoadResult.Failure(failure);

        Assert.Equal(failure, outcome.Reason);
    }

    [Fact]
    public async Task TryOpenSolutionAsync_returns_Failure_for_missing_file()
    {
        var outcome = await CSharpWorkspaceSession.TryOpenSolutionAsync("/does/not/exist.sln");

        var failure = Assert.IsType<WorkspaceLoadResult.Failure>(outcome);
        Assert.Equal("/does/not/exist.sln", failure.Reason.SolutionPath);
    }

    [Fact]
    public async Task TryOpenProjectAsync_returns_Failure_for_missing_file()
    {
        var outcome = await CSharpWorkspaceSession.TryOpenProjectAsync("/does/not/exist.csproj");

        var failure = Assert.IsType<WorkspaceLoadResult.Failure>(outcome);
        Assert.Equal("/does/not/exist.csproj", failure.Reason.SolutionPath);
    }

    [Fact]
    public async Task TryOpenSolutionAsync_returns_Success_for_real_solution()
    {
        var outcome = await CSharpWorkspaceSession.TryOpenSolutionAsync(TestPaths.FindSolutionPath());

        var success = Assert.IsType<WorkspaceLoadResult.Success>(outcome);
        await using var session = success.Session;
        Assert.NotNull(session);
    }
}
