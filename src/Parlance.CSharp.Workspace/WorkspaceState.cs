namespace Parlance.CSharp.Workspace;

public abstract record WorkspaceState
{
    private WorkspaceState()
    {
    }

    public sealed record NotLoaded : WorkspaceState
    {
        public static NotLoaded Instance { get; } = new();

        private NotLoaded()
        {
        }
    }

    public sealed record Loaded(CSharpWorkspaceSession Session) : WorkspaceState;

    public sealed record LoadFailed(WorkspaceLoadFailure Failure) : WorkspaceState;

    public sealed record Disposed : WorkspaceState
    {
        public static Disposed Instance { get; } = new();

        private Disposed()
        {
        }
    }

    public T Match<T>(
        Func<T> notLoaded,
        Func<CSharpWorkspaceSession, T> loaded,
        Func<WorkspaceLoadFailure, T> loadFailed,
        Func<T> disposed) =>
        this switch
        {
            NotLoaded => notLoaded(),
            Loaded loadedState => loaded(loadedState.Session),
            LoadFailed failedState => loadFailed(failedState.Failure),
            Disposed => disposed(),
            _ => throw new InvalidOperationException("Unreachable")
        };
}
