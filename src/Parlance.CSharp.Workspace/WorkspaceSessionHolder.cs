namespace Parlance.CSharp.Workspace;

/// <summary>
/// DI-scoped container for the workspace session. Public setters are intentional —
/// the CLI command or MCP server sets the session once at startup. The holder's lifetime
/// is tied to the DI container, which disposes it (and the session) on shutdown.
/// </summary>
public sealed class WorkspaceSessionHolder : IAsyncDisposable
{
    private volatile CSharpWorkspaceSession? _session;
    private volatile WorkspaceLoadFailure? _loadFailure;

    public CSharpWorkspaceSession Session =>
        _session ?? throw new InvalidOperationException("Workspace session is not yet loaded");

    public bool IsLoaded => _session is not null;
    public WorkspaceLoadFailure? LoadFailure => _loadFailure;

    public void SetSession(CSharpWorkspaceSession session) => _session = session;
    public void SetLoadFailure(WorkspaceLoadFailure failure) => _loadFailure = failure;

    public async ValueTask DisposeAsync()
    {
        if (_session is not null)
            await _session.DisposeAsync();
    }
}
