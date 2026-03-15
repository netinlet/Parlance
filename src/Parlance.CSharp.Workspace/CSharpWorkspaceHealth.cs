namespace Parlance.CSharp.Workspace;

public sealed record CSharpWorkspaceHealth(
    WorkspaceLoadStatus Status,
    IReadOnlyList<CSharpProjectInfo> Projects,
    IReadOnlyList<WorkspaceDiagnostic> Diagnostics)
{
    public static CSharpWorkspaceHealth FromProjects(
        IReadOnlyList<CSharpProjectInfo> projects,
        IReadOnlyList<WorkspaceDiagnostic>? diagnostics = null)
    {
        diagnostics ??= [];
        return new(DeriveStatus(projects), projects, diagnostics);
    }

    private static WorkspaceLoadStatus DeriveStatus(IReadOnlyList<CSharpProjectInfo> projects)
    {
        return projects switch
        {
            { Count: 0 } => WorkspaceLoadStatus.Failed,
            _ when projects.All(p => p.Status is ProjectLoadStatus.Failed) => WorkspaceLoadStatus.Failed,
            _ when projects.All(p => p.Status is ProjectLoadStatus.Loaded) => WorkspaceLoadStatus.Loaded,
            _ => WorkspaceLoadStatus.Degraded
        };
    }
}
