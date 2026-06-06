using Parlance.CSharp.Workspace;

namespace Parlance.CSharp.Workspace.Tests;

public sealed class WorkspaceStateTests
{
    [Fact]
    public void NotLoaded_is_the_initial_singleton_state()
    {
        Assert.Same(WorkspaceState.NotLoaded.Instance, WorkspaceState.NotLoaded.Instance);
    }

    [Fact]
    public void LoadFailed_carries_the_failure()
    {
        var failure = new WorkspaceLoadFailure("nope", "/x.sln");
        var state = new WorkspaceState.LoadFailed(failure);

        Assert.Equal(failure, state.Failure);
    }

    [Fact]
    public void Match_dispatches_to_the_active_case()
    {
        WorkspaceState state = WorkspaceState.NotLoaded.Instance;

        var result = state.Match(
            notLoaded: () => "nl",
            loaded: _ => "ld",
            loadFailed: _ => "lf",
            disposed: () => "ds");

        Assert.Equal("nl", result);
    }
}
