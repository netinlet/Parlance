using Parlance.CSharp.Workspace;
using Parlance.CSharp.Workspace.Tests.Integration;

namespace Parlance.CSharp.Workspace.Tests;

public sealed class CSharpWorkspaceSessionRepoPathTests
{
    [Fact]
    public async Task RepoPath_IsDirectoryOwningTheSolution()
    {
        var solutionPath = TestPaths.FindSolutionPath();
        var result = await CSharpWorkspaceSession.TryOpenSolutionAsync(solutionPath);
        var session = Assert.IsType<WorkspaceLoadResult.Success>(result).Session;
        await using (session)
            Assert.Equal(Path.GetDirectoryName(solutionPath), session.RepoPath);
    }
}
