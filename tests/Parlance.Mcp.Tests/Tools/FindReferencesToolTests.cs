using Microsoft.Extensions.Logging.Abstractions;
using Parlance.CSharp.Workspace;
using Parlance.CSharp.Workspace.Tests.Integration;
using Parlance.Mcp.Tools;

namespace Parlance.Mcp.Tests.Tools;

[Trait("Category", "Integration")]
public sealed class FindReferencesToolTests(WorkspaceFixture fixture) : IClassFixture<WorkspaceFixture>
{
    private readonly WorkspaceSessionHolder _holder = fixture.Holder;
    private readonly WorkspaceQueryService _query = fixture.Query;

    [Fact]
    public async Task FindReferences_FindsReferencesOfKnownSymbol()
    {
        var result = await FindReferencesTool.FindReferences(
            _holder, _query,
            "CSharpWorkspaceSession", ct: CancellationToken.None);

        Assert.Equal("found", result.Status);
        Assert.NotNull(result.SymbolName);
        Assert.Contains("CSharpWorkspaceSession", result.SymbolName);
        Assert.True(result.TotalCount > 0);
        Assert.NotEmpty(result.FileGroups);
        Assert.All(result.FileGroups, group =>
        {
            Assert.NotEmpty(group.FilePath.Absolute);
            Assert.NotEmpty(group.Locations);
        });
    }

    [Fact]
    public async Task FindReferences_WithSnippets_ReturnsNonNullSnippets()
    {
        var result = await FindReferencesTool.FindReferences(
            _holder, _query,
            "CSharpWorkspaceSession", includeSnippets: true, ct: CancellationToken.None);

        Assert.Equal("found", result.Status);
        Assert.NotNull(result.SymbolName);
        Assert.Contains("CSharpWorkspaceSession", result.SymbolName);
        Assert.True(result.TotalCount > 0);
        Assert.NotEmpty(result.FileGroups);

        var allReferences = result.FileGroups
            .SelectMany(group => group.Locations)
            .ToList();

        Assert.Contains(allReferences, r => r.Snippet is not null);
    }

    [Fact]
    public async Task FindReferences_NotFound_ReturnsNotFound()
    {
        var result = await FindReferencesTool.FindReferences(
            _holder, _query,
            "ThisSymbolDoesNotExistAnywhere", ct: CancellationToken.None);

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
            "Anything", ct: CancellationToken.None).Result;

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
            "Anything", ct: CancellationToken.None).Result;

        Assert.Equal("load_failed", result.Status);
        Assert.Equal("boom", result.Message);
    }
}
