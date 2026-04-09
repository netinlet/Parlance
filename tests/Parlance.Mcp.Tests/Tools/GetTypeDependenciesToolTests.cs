using Microsoft.Extensions.Logging.Abstractions;
using Parlance.CSharp.Workspace;
using Parlance.CSharp.Workspace.Tests.Integration;
using Parlance.Mcp.Tools;

namespace Parlance.Mcp.Tests.Tools;

public sealed class GetTypeDependenciesToolTests : IAsyncLifetime
{
    private WorkspaceSessionHolder _holder = null!;
    private WorkspaceQueryService _query = null!;
    private CSharpWorkspaceSession _session = null!;

    public async Task InitializeAsync()
    {
        var solutionPath = TestPaths.FindSolutionPath();
        _session = await CSharpWorkspaceSession.OpenSolutionAsync(solutionPath);
        _holder = new WorkspaceSessionHolder();
        _holder.SetSession(_session);
        _query = new WorkspaceQueryService(_holder, NullLogger<WorkspaceQueryService>.Instance);
    }

    public async Task DisposeAsync() => await _session.DisposeAsync();

    [Fact]
    public async Task GetTypeDependencies_KnownType_ReturnsDependencies()
    {
        // WorkspaceQueryService has dependencies on WorkspaceSessionHolder and ILogger
        var result = await GetTypeDependenciesTool.GetTypeDependencies(
            _holder, _query, TestAnalytics.Instance,
            "WorkspaceQueryService", CancellationToken.None);

        Assert.Equal("found", result.Status);
        Assert.Equal("Parlance.CSharp.Workspace.WorkspaceQueryService", result.TypeName);
        // WorkspaceQueryService has a parameter of type WorkspaceSessionHolder
        Assert.Contains(result.Dependencies, d => d.Name == "WorkspaceSessionHolder");
    }

    [Fact]
    public async Task GetTypeDependencies_KnownType_ReturnsDependents()
    {
        // WorkspaceQueryService is referenced by many tools via DI
        var result = await GetTypeDependenciesTool.GetTypeDependencies(
            _holder, _query, TestAnalytics.Instance,
            "WorkspaceQueryService", CancellationToken.None);

        Assert.Equal("found", result.Status);
        Assert.NotEmpty(result.Dependents);
    }

    [Fact]
    public async Task GetTypeDependencies_DependenciesScopedToSolution()
    {
        // No framework types (like System.Object, System.String) should appear as dependencies
        var result = await GetTypeDependenciesTool.GetTypeDependencies(
            _holder, _query, TestAnalytics.Instance,
            "WorkspaceQueryService", CancellationToken.None);

        Assert.Equal("found", result.Status);
        Assert.All(result.Dependencies, d =>
            Assert.DoesNotContain("System.", d.FullyQualifiedName));
    }

    [Fact]
    public async Task GetTypeDependencies_NotFound_ReturnsNotFound()
    {
        var result = await GetTypeDependenciesTool.GetTypeDependencies(
            _holder, _query, TestAnalytics.Instance,
            "ThisTypeDoesNotExistAnywhere", CancellationToken.None);

        Assert.Equal("not_found", result.Status);
        Assert.Empty(result.Dependencies);
        Assert.Empty(result.Dependents);
    }

    [Fact]
    public void GetTypeDependencies_NotLoaded_ReturnsNotLoaded()
    {
        var holder = new WorkspaceSessionHolder();
        var query = new WorkspaceQueryService(holder, NullLogger<WorkspaceQueryService>.Instance);

        var result = GetTypeDependenciesTool.GetTypeDependencies(
            holder, query, TestAnalytics.Instance,
            "Anything", CancellationToken.None).Result;

        Assert.Equal("not_loaded", result.Status);
        Assert.Empty(result.Dependencies);
        Assert.Empty(result.Dependents);
    }

    [Fact]
    public void GetTypeDependencies_LoadFailed_ReturnsFailure()
    {
        var holder = new WorkspaceSessionHolder();
        holder.SetLoadFailure(new WorkspaceLoadFailure("boom", "/path.sln"));
        var query = new WorkspaceQueryService(holder, NullLogger<WorkspaceQueryService>.Instance);

        var result = GetTypeDependenciesTool.GetTypeDependencies(
            holder, query, TestAnalytics.Instance,
            "Anything", CancellationToken.None).Result;

        Assert.Equal("load_failed", result.Status);
        Assert.Equal("boom", result.Message);
    }
}
