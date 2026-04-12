using Microsoft.Extensions.Logging.Abstractions;
using Parlance.CSharp.Workspace;
using Parlance.CSharp.Workspace.Tests.Integration;
using Parlance.Mcp.Tools;

namespace Parlance.Mcp.Tests.Tools;

public sealed class FindReferencesToolTests : IAsyncLifetime
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
    public async Task FindReferences_FindsReferencesOfKnownSymbol()
    {
        var result = await FindReferencesTool.FindReferences(
            _holder, _query,
            "CSharpWorkspaceSession", CancellationToken.None);

        Assert.Equal("found", result.Status);
        Assert.NotNull(result.SymbolName);
        Assert.Contains("CSharpWorkspaceSession", result.SymbolName);
        Assert.True(result.TotalCount > 0);
        Assert.NotEmpty(result.FileGroups);
        Assert.All(result.FileGroups, group =>
        {
            Assert.NotEmpty(group.FilePath);
            Assert.NotEmpty(group.Locations);
        });
    }

    [Fact]
    public async Task FindReferences_NotFound_ReturnsNotFound()
    {
        var result = await FindReferencesTool.FindReferences(
            _holder, _query,
            "ThisSymbolDoesNotExistAnywhere", CancellationToken.None);

        Assert.Equal("not_found", result.Status);
        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.FileGroups);
    }

    [Fact]
    public void FindReferences_NotLoaded_ReturnsNotLoaded()
    {
        var holder = new WorkspaceSessionHolder();
        var query = new WorkspaceQueryService(holder, NullLogger<WorkspaceQueryService>.Instance);

        var result = FindReferencesTool.FindReferences(
            holder, query,
            "Anything", CancellationToken.None).Result;

        Assert.Equal("not_loaded", result.Status);
        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.FileGroups);
    }

    [Fact]
    public void FindReferences_LoadFailed_ReturnsFailure()
    {
        var holder = new WorkspaceSessionHolder();
        holder.SetLoadFailure(new WorkspaceLoadFailure("boom", "/path.sln"));
        var query = new WorkspaceQueryService(holder, NullLogger<WorkspaceQueryService>.Instance);

        var result = FindReferencesTool.FindReferences(
            holder, query,
            "Anything", CancellationToken.None).Result;

        Assert.Equal("load_failed", result.Status);
        Assert.Equal("boom", result.Message);
    }
}
