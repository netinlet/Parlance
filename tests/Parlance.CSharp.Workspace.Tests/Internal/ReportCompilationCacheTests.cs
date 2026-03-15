using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Parlance.CSharp.Workspace.Internal;

namespace Parlance.CSharp.Workspace.Tests.Internal;

public sealed class ReportCompilationCacheTests
{
    private static (AdhocWorkspace Workspace, Project Project) CreateTestProject()
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

    [Fact]
    public async Task GetAsync_FirstCall_ReturnsCompilation()
    {
        var (workspace, project) = CreateTestProject();
        using var _ = workspace;

        var cache = new ReportCompilationCache();
        var state = await cache.GetAsync(project);

        Assert.NotNull(state.Compilation);
    }

    [Fact]
    public async Task GetAsync_SecondCall_ReturnsSameInstance()
    {
        var (workspace, project) = CreateTestProject();
        using var _ = workspace;

        var cache = new ReportCompilationCache();
        var state1 = await cache.GetAsync(project);
        var state2 = await cache.GetAsync(project);

        Assert.Same(state1, state2);
    }

    [Fact]
    public async Task MarkDirty_IsNoOp_StillReturnsCached()
    {
        var (workspace, project) = CreateTestProject();
        using var _ = workspace;

        var cache = new ReportCompilationCache();
        var state1 = await cache.GetAsync(project);

        cache.MarkDirty(project.Id);

        var state2 = await cache.GetAsync(project);
        Assert.Same(state1, state2);
    }

    [Fact]
    public async Task MarkAllDirty_IsNoOp_StillReturnsCached()
    {
        var (workspace, project) = CreateTestProject();
        using var _ = workspace;

        var cache = new ReportCompilationCache();
        var state1 = await cache.GetAsync(project);

        cache.MarkAllDirty();

        var state2 = await cache.GetAsync(project);
        Assert.Same(state1, state2);
    }
}
