using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;

namespace Parlance.CSharp.Workspace.Internal;

internal sealed class ReportCompilationCache : IProjectCompilationCache
{
    private readonly ConcurrentDictionary<ProjectId, ProjectCompilationState> _cache = new();

    public async Task<ProjectCompilationState> GetAsync(Project project, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(project.Id, out var state))
            return state;

        var compilation = await project.GetCompilationAsync(ct)
            ?? throw new InvalidOperationException($"Compilation returned null for project '{project.Name}'");

        var newState = new ProjectCompilationState(compilation);
        return _cache.GetOrAdd(project.Id, newState);
    }

    public void MarkDirty(ProjectId projectId) { }

    public void MarkAllDirty() { }
}
