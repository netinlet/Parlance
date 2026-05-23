using Parlance.CSharp.Workspace;

namespace Parlance.CSharp.Workspace.Tests;

public sealed class WorkspaceSessionHolderStateTests
{
    [Fact]
    public void Default_state_is_NotLoaded()
    {
        using var holder = new WorkspaceSessionHolder();
        Assert.IsType<WorkspaceState.NotLoaded>(holder.State);
    }

    [Fact]
    public void SetLoadFailure_transitions_to_LoadFailed()
    {
        using var holder = new WorkspaceSessionHolder();
        var failure = new WorkspaceLoadFailure("bad", "/x.sln");

        holder.SetLoadFailure(failure);

        var failed = Assert.IsType<WorkspaceState.LoadFailed>(holder.State);
        Assert.Equal(failure, failed.Failure);
    }

    [Fact]
    public void Dispose_transitions_to_Disposed()
    {
        var holder = new WorkspaceSessionHolder();
        holder.Dispose();
        Assert.IsType<WorkspaceState.Disposed>(holder.State);
    }

    [Fact]
    public void RequireSession_throws_when_state_is_not_Loaded()
    {
        using var holder = new WorkspaceSessionHolder();

        var ex = Assert.Throws<InvalidOperationException>(() => holder.RequireSession());

        Assert.Contains("not loaded", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
