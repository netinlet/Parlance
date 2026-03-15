using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Parlance.CSharp.Workspace.Internal;

namespace Parlance.CSharp.Workspace.Tests.Internal;

public sealed class ServerCompilationCacheTests
{
    private static (AdhocWorkspace Workspace, Project Project) CreateSingleProject()
    {
        var workspace = new AdhocWorkspace();
        var projectInfo = ProjectInfo.Create(
            ProjectId.CreateNewId(), VersionStamp.Default, "Test", "Test", LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .WithParseOptions(new CSharpParseOptions(LanguageVersion.Latest));

        var project = workspace.AddProject(projectInfo);
        workspace.AddDocument(project.Id, "Class1.cs",
            SourceText.From("namespace Test; public class Class1 { }"));

        return (workspace, workspace.CurrentSolution.GetProject(project.Id)!);
    }

    private static (AdhocWorkspace Workspace, Project ProjectA, Project ProjectB) CreateDependentProjects()
    {
        var workspace = new AdhocWorkspace();

        var projectAInfo = ProjectInfo.Create(
            ProjectId.CreateNewId(), VersionStamp.Default, "A", "A", LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .WithParseOptions(new CSharpParseOptions(LanguageVersion.Latest));

        var projectA = workspace.AddProject(projectAInfo);
        workspace.AddDocument(projectA.Id, "A.cs",
            SourceText.From("namespace A; public class ClassA { }"));

        var projectBInfo = ProjectInfo.Create(
            ProjectId.CreateNewId(), VersionStamp.Default, "B", "B", LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .WithParseOptions(new CSharpParseOptions(LanguageVersion.Latest))
            .WithProjectReferences([new ProjectReference(projectA.Id)]);

        var projectB = workspace.AddProject(projectBInfo);
        workspace.AddDocument(projectB.Id, "B.cs",
            SourceText.From("namespace B; public class ClassB : A.ClassA { }"));

        var solution = workspace.CurrentSolution;
        return (workspace, solution.GetProject(projectA.Id)!, solution.GetProject(projectB.Id)!);
    }

    [Fact]
    public async Task GetAsync_FirstCall_ReturnsCompilation()
    {
        var (workspace, project) = CreateSingleProject();
        using var _ = workspace;

        var cache = new ServerCompilationCache(() => workspace.CurrentSolution);
        var state = await cache.GetAsync(project);

        Assert.NotNull(state.Compilation);
    }

    [Fact]
    public async Task GetAsync_SecondCall_ReturnsSameInstance()
    {
        var (workspace, project) = CreateSingleProject();
        using var _ = workspace;

        var cache = new ServerCompilationCache(() => workspace.CurrentSolution);
        var state1 = await cache.GetAsync(project);
        var state2 = await cache.GetAsync(project);

        Assert.Same(state1, state2);
    }

    [Fact]
    public async Task MarkDirty_CausesRecompilation()
    {
        var (workspace, project) = CreateSingleProject();
        using var _ = workspace;

        var cache = new ServerCompilationCache(() => workspace.CurrentSolution);
        var state1 = await cache.GetAsync(project);

        cache.MarkDirty(project.Id);

        var state2 = await cache.GetAsync(project);
        Assert.NotSame(state1, state2);
    }

    [Fact]
    public async Task MarkDirty_CascadesToTransitiveDependents()
    {
        var (workspace, projectA, projectB) = CreateDependentProjects();
        using var _ = workspace;

        var cache = new ServerCompilationCache(() => workspace.CurrentSolution);

        await cache.GetAsync(projectA);
        var stateB1 = await cache.GetAsync(projectB);

        // Marking A dirty should cascade to B (B depends on A)
        cache.MarkDirty(projectA.Id);

        var stateB2 = await cache.GetAsync(projectB);
        Assert.NotSame(stateB1, stateB2);
    }

    [Fact]
    public async Task MarkDirty_DoesNotCascadeUpstream()
    {
        var (workspace, projectA, projectB) = CreateDependentProjects();
        using var _ = workspace;

        var cache = new ServerCompilationCache(() => workspace.CurrentSolution);

        var stateA1 = await cache.GetAsync(projectA);
        await cache.GetAsync(projectB);

        // Marking B dirty should NOT cascade to A (A does not depend on B)
        cache.MarkDirty(projectB.Id);

        var stateA2 = await cache.GetAsync(projectA);
        Assert.Same(stateA1, stateA2);
    }

    [Fact]
    public async Task MarkAllDirty_MarksAllProjects()
    {
        var (workspace, projectA, projectB) = CreateDependentProjects();
        using var _ = workspace;

        var cache = new ServerCompilationCache(() => workspace.CurrentSolution);
        var stateA1 = await cache.GetAsync(projectA);
        var stateB1 = await cache.GetAsync(projectB);

        cache.MarkAllDirty();

        var stateA2 = await cache.GetAsync(projectA);
        var stateB2 = await cache.GetAsync(projectB);
        Assert.NotSame(stateA1, stateA2);
        Assert.NotSame(stateB1, stateB2);
    }

    [Fact]
    public async Task ConcurrentQueries_DifferentProjects_BothSucceed()
    {
        var (workspace, projectA, projectB) = CreateDependentProjects();
        using var _ = workspace;

        var cache = new ServerCompilationCache(() => workspace.CurrentSolution);

        var task1 = cache.GetAsync(projectA);
        var task2 = cache.GetAsync(projectB);

        var results = await Task.WhenAll(task1, task2);
        Assert.NotNull(results[0].Compilation);
        Assert.NotNull(results[1].Compilation);
    }
}
