using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;

namespace Parlance.CSharp.Workspace.Internal;

// Concurrent reads to the same dirty project may both recompile. This is correct
// (both produce equivalent compilations from the same project snapshot) but wasteful.
// Acceptable trade-off: avoids lock contention for the common case (different projects).
internal sealed class ServerCompilationCache(Func<Solution> solutionProvider) : IProjectCompilationCache
{
    private readonly ConcurrentDictionary<ProjectId, ProjectCompilationState> _cache = new();
    private readonly ConcurrentDictionary<ProjectId, byte> _dirty = new();

    public async Task<ProjectCompilationState> GetAsync(Project project, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(project.Id, out var state) && !_dirty.ContainsKey(project.Id))
            return state;

        var compilation = await project.GetCompilationAsync(ct)
            ?? throw new InvalidOperationException($"Compilation returned null for project '{project.Name}'");

        var newState = new ProjectCompilationState(compilation);
        _cache[project.Id] = newState;
        _dirty.TryRemove(project.Id, out _);
        return newState;
    }

    public void MarkDirty(ProjectId projectId)
    {
        _dirty[projectId] = 0;

        var solution = solutionProvider();
        var graph = solution.GetProjectDependencyGraph();
        foreach (var dependent in graph.GetProjectsThatTransitivelyDependOnThisProject(projectId))
            _dirty[dependent] = 0;
    }

    public void MarkAllDirty()
    {
        var solution = solutionProvider();
        foreach (var projectId in solution.ProjectIds)
            _dirty[projectId] = 0;
    }
}
