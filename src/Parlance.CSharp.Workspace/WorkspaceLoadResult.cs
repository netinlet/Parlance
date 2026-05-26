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
}
