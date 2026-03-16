using Parlance.CSharp.Workspace;

namespace Parlance.Mcp;

public sealed class WorkspaceSessionHolder : IAsyncDisposable
{
    private CSharpWorkspaceSession? _session;

    public CSharpWorkspaceSession Session =>
        _session ?? throw new InvalidOperationException("Workspace session is not yet loaded");

    public bool IsLoaded => _session is not null;

    public WorkspaceLoadFailure? LoadFailure { get; private set; }

    internal void SetSession(CSharpWorkspaceSession session) => _session = session;

    internal void SetLoadFailure(WorkspaceLoadFailure failure) => LoadFailure = failure;

    public async ValueTask DisposeAsync()
    {
        if (_session is not null)
            await _session.DisposeAsync();
    }
}
