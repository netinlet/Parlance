namespace Parlance.CSharp.Workspace;

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
