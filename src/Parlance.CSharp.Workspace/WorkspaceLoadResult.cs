namespace Parlance.CSharp.Workspace;

/// <summary>
/// The outcome of attempting to open a workspace, as a value rather than a
/// thrown exception. <see cref="CSharpWorkspaceSession.TryOpenSolutionAsync"/>
/// and <see cref="CSharpWorkspaceSession.TryOpenProjectAsync"/> return this so
/// load coordination pattern-matches the outcome instead of catching.
/// </summary>
public abstract record WorkspaceLoadResult
{
    private WorkspaceLoadResult()
    {
    }

    public sealed record Success(CSharpWorkspaceSession Session) : WorkspaceLoadResult;

    public sealed record Failure(WorkspaceLoadFailure Reason) : WorkspaceLoadResult;

    /// <summary>
    /// Runs the matching side-effecting branch. Both handlers are required, so the
    /// single unreachable lives here and adding a case is a compile error at call sites.
    /// </summary>
    public void Switch(Action<CSharpWorkspaceSession> onSuccess, Action<WorkspaceLoadFailure> onFailure)
    {
        switch (this)
        {
            case Success success:
                onSuccess(success.Session);
                break;
            case Failure failure:
                onFailure(failure.Reason);
                break;
            default:
                throw new InvalidOperationException("Unreachable");
        }
    }
}
