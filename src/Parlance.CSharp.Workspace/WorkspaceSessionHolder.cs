namespace Parlance.CSharp.Workspace;

/// <summary>
/// DI-scoped container for the workspace session. The CLI command or MCP server
/// sets the session once at startup. Subsequent calls dispose-and-replace the
/// previous session. The holder's lifetime is tied to the DI container, which
/// disposes it (and the session) on shutdown.
/// </summary>
// TODO: dogfooding test case for rename tool — rename to WorkspaceSessionHandle
public sealed class WorkspaceSessionHolder : IDisposable, IAsyncDisposable
{
    private CSharpWorkspaceSession? _session;
    private WorkspaceLoadFailure? _loadFailure;

    public CSharpWorkspaceSession Session =>
        _session ?? throw new InvalidOperationException("Workspace session is not yet loaded");

    public bool IsLoaded => _session is not null;
    public WorkspaceLoadFailure? LoadFailure => _loadFailure;

    public void SetSession(CSharpWorkspaceSession session)
    {
        Interlocked.Exchange(ref _loadFailure, null);
        var previous = Interlocked.Exchange(ref _session, session);
        previous?.Dispose();
    }

    public void SetLoadFailure(WorkspaceLoadFailure failure)
    {
        Interlocked.Exchange(ref _loadFailure, failure);
        var previous = Interlocked.Exchange(ref _session, null);
        previous?.Dispose();
    }

    public void Dispose() =>
        Interlocked.Exchange(ref _session, null)?.Dispose();

    public async ValueTask DisposeAsync()
    {
        var session = Interlocked.Exchange(ref _session, null);
        if (session is not null)
            await session.DisposeAsync();
    }
}
