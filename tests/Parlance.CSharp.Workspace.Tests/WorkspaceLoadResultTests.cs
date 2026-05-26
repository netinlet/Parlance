using Parlance.CSharp.Workspace;

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
}
