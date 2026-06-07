namespace Parlance.CSharp.Workspace;

/// <summary>
/// DI-scoped container for the workspace lifecycle. The CLI command or MCP server
/// drives transitions; tools read State and pattern-match on its cases.
/// </summary>
// TODO: dogfooding test case for rename tool — rename to WorkspaceSessionHandle
public sealed class WorkspaceSessionHolder : IDisposable, IAsyncDisposable
{
    private WorkspaceState _state = WorkspaceState.NotLoaded.Instance;

    public WorkspaceState State => Volatile.Read(ref _state);

    public void SetSession(CSharpWorkspaceSession session)
    {
        var previous = Interlocked.Exchange(ref _state, new WorkspaceState.Loaded(session));
        if (previous is WorkspaceState.Loaded loaded)
            loaded.Session.Dispose();
    }

    public void SetLoadFailure(WorkspaceLoadFailure failure)
    {
        var previous = Interlocked.Exchange(ref _state, new WorkspaceState.LoadFailed(failure));
        if (previous is WorkspaceState.Loaded loaded)
            loaded.Session.Dispose();
    }

    /// <summary>
    /// For internal services that are only invoked after callers have pattern-matched
    /// <see cref="State"/> to <c>Loaded</c>. Throws if the precondition is violated.
    /// </summary>
    public CSharpWorkspaceSession LoadedSession => State switch
    {
        WorkspaceState.Loaded loaded => loaded.Session,
        _ => throw new InvalidOperationException("Workspace is not loaded. Callers must pattern-match on State before invoking services.")
    };

    /// <summary>The current snapshot version, or 0 when no session is loaded. For stamping results.</summary>
    public long CurrentSnapshotVersion() =>
        State is WorkspaceState.Loaded loaded ? loaded.Session.SnapshotVersion : 0;

    public void Dispose()
    {
        var previous = Interlocked.Exchange(ref _state, WorkspaceState.Disposed.Instance);
        if (previous is WorkspaceState.Loaded loaded)
            loaded.Session.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        var previous = Interlocked.Exchange(ref _state, WorkspaceState.Disposed.Instance);
        if (previous is WorkspaceState.Loaded loaded)
            await loaded.Session.DisposeAsync();
    }
}
