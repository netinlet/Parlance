using Microsoft.CodeAnalysis;

namespace Parlance.CSharp.Workspace.Internal;

internal interface IProjectCompilationCache
{
    Task<ProjectCompilationState> GetAsync(Project project, CancellationToken ct = default);
    void MarkDirty(ProjectId projectId);
    void MarkAllDirty();
}
