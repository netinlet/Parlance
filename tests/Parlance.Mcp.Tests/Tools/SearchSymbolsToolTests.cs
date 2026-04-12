using Microsoft.Extensions.Logging.Abstractions;
using Parlance.CSharp.Workspace;
using Parlance.CSharp.Workspace.Tests.Integration;
using Parlance.Mcp.Tools;

namespace Parlance.Mcp.Tests.Tools;

public sealed class SearchSymbolsToolTests : IAsyncLifetime
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
    public async Task SearchSymbols_SubstringMatch_FindsResults()
    {
        var result = await SearchSymbolsTool.SearchSymbols(
            _holder, _query,
            searchQuery: "Workspace", kind: null, maxResults: 25,
            CancellationToken.None);

        Assert.Equal("found", result.Status);
        Assert.NotEmpty(result.Matches);
        Assert.True(result.TotalMatches > 0);
        Assert.All(result.Matches, m =>
        {
            Assert.NotEmpty(m.DisplayName);
            Assert.NotEmpty(m.FullyQualifiedName);
            Assert.NotEmpty(m.Kind);
            Assert.NotEmpty(m.ProjectName);
        });
    }

    [Fact]
    public async Task SearchSymbols_KindFilter_ReturnsOnlyMatchingKind()
    {
        var result = await SearchSymbolsTool.SearchSymbols(
            _holder, _query,
            searchQuery: "Workspace", kind: "class", maxResults: 25,
            CancellationToken.None);

        Assert.Equal("found", result.Status);
        Assert.NotEmpty(result.Matches);
        Assert.All(result.Matches, m => Assert.Contains("Type", m.Kind));
    }

    [Fact]
    public async Task SearchSymbols_MaxResults_CapsOutput()
    {
        var result = await SearchSymbolsTool.SearchSymbols(
            _holder, _query,
            searchQuery: "Get", kind: null, maxResults: 3,
            CancellationToken.None);

        Assert.Equal("found", result.Status);
        Assert.True(result.Matches.Count <= 3);
        Assert.True(result.TotalMatches >= result.Matches.Count);
    }

    [Fact]
    public async Task SearchSymbols_NoMatches_ReturnsNoMatches()
    {
        var result = await SearchSymbolsTool.SearchSymbols(
            _holder, _query,
            searchQuery: "XyzzyNonexistentSymbolName", kind: null, maxResults: 25,
            CancellationToken.None);

        Assert.Equal("no_matches", result.Status);
        Assert.Empty(result.Matches);
        Assert.Equal(0, result.TotalMatches);
    }

    [Fact]
    public async Task SearchSymbols_InvalidKind_ReturnsError()
    {
        var result = await SearchSymbolsTool.SearchSymbols(
            _holder, _query,
            searchQuery: "Workspace", kind: "delegate", maxResults: 25,
            CancellationToken.None);

        Assert.Equal("error", result.Status);
        Assert.Contains("Unknown kind", result.Message);
        Assert.Contains("delegate", result.Message);
    }

    [Fact]
    public void SearchSymbols_NotLoaded_ReturnsNotLoaded()
    {
        var holder = new WorkspaceSessionHolder();
        var query = new WorkspaceQueryService(holder, NullLogger<WorkspaceQueryService>.Instance);

        var result = SearchSymbolsTool.SearchSymbols(
            holder, query,
            searchQuery: "Anything", kind: null, maxResults: 25,
            CancellationToken.None).Result;

        Assert.Equal("not_loaded", result.Status);
    }

    [Fact]
    public void SearchSymbols_LoadFailed_ReturnsLoadFailed()
    {
        var holder = new WorkspaceSessionHolder();
        holder.SetLoadFailure(new WorkspaceLoadFailure("boom", "/path.sln"));
        var query = new WorkspaceQueryService(holder, NullLogger<WorkspaceQueryService>.Instance);

        var result = SearchSymbolsTool.SearchSymbols(
            holder, query,
            searchQuery: "Anything", kind: null, maxResults: 25,
            CancellationToken.None).Result;

        Assert.Equal("load_failed", result.Status);
        Assert.Equal("boom", result.Message);
    }
}
