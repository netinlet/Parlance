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

    /// <summary>
    /// True when an expected snapshot version is supplied and no longer matches the current snapshot.
    /// Best-effort staleness signal. Meaningful only when the workspace is loaded: it compares against
    /// <see cref="CurrentSnapshotVersion"/>, which returns 0 when not loaded — so callers must gate on
    /// the loaded state first (e.g. inside a <c>loaded:</c> match branch) before treating a true result
    /// as "stale" rather than "not loaded".
    /// <para>
    /// A value of <c>0</c> is treated as "no expectation" (identical to <c>null</c>): snapshot versions
    /// start at 1 and only increment, so a client that serializes a default <c>0</c> on the wire instead
    /// of omitting the field would otherwise always be reported stale on a fresh workspace.
    /// </para>
    /// </summary>
    public bool IsStale(long? expectedSnapshotVersion) =>
        expectedSnapshotVersion is { } expected && expected != 0 && expected != CurrentSnapshotVersion();

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
