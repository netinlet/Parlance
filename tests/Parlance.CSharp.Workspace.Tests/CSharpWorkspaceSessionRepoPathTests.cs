using Parlance.CSharp.Workspace.Tests.Integration;

namespace Parlance.CSharp.Workspace.Tests;

[Trait("Category", "Integration")]
public sealed class CSharpWorkspaceSessionRepoPathTests(WorkspaceFixture fixture) : IClassFixture<WorkspaceFixture>
{
    [Fact]
    public void RepoPath_IsDirectoryOwningTheSolution()
    {
        var solutionPath = TestPaths.FindSolutionPath();
        Assert.Equal(Path.GetDirectoryName(solutionPath), fixture.Session.RepoPath);
    }
}
