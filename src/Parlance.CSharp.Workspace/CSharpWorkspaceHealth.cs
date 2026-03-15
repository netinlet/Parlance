using System.Collections.Immutable;

namespace Parlance.CSharp.Workspace;

public sealed record CSharpWorkspaceHealth(
    WorkspaceLoadStatus Status,
    ImmutableList<CSharpProjectInfo> Projects,
    ImmutableList<WorkspaceDiagnostic> Diagnostics)
{
    public static CSharpWorkspaceHealth FromProjects(
        ImmutableList<CSharpProjectInfo> projects,
        ImmutableList<WorkspaceDiagnostic>? diagnostics = null)
    {
        diagnostics ??= [];
        return new(DeriveStatus(projects, diagnostics), projects, diagnostics);
    }

    private static WorkspaceLoadStatus DeriveStatus(
        ImmutableList<CSharpProjectInfo> projects,
        ImmutableList<WorkspaceDiagnostic> diagnostics)
    {
        var hasBlockingDiagnostics = diagnostics.Any(d =>
            d.Severity is WorkspaceDiagnosticSeverity.Error or WorkspaceDiagnosticSeverity.Warning);

        return projects switch
        {
            { Count: 0 } => WorkspaceLoadStatus.Failed,
            _ when projects.All(p => p.Status is ProjectLoadStatus.Failed) => WorkspaceLoadStatus.Failed,
            _ when hasBlockingDiagnostics => WorkspaceLoadStatus.Degraded,
            _ when projects.All(p => p.Status is ProjectLoadStatus.Loaded) => WorkspaceLoadStatus.Loaded,
            _ => WorkspaceLoadStatus.Degraded
        };
    }
}
