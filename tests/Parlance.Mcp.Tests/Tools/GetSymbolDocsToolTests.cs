using Microsoft.Extensions.Logging.Abstractions;
using Parlance.CSharp.Workspace;
using Parlance.CSharp.Workspace.Tests.Integration;
using Parlance.Mcp.Tools;

namespace Parlance.Mcp.Tests.Tools;

public sealed class GetSymbolDocsToolTests : IAsyncLifetime
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
    public async Task GetSymbolDocs_TypeWithDocs_ReturnsSummary()
    {
        // ResolvedSymbol has XML doc comments in Parlance.CSharp.Workspace
        var result = await GetSymbolDocsTool.GetSymbolDocs(
            _holder, _query, NullLogger<GetSymbolDocsTool>.Instance,
            "ResolvedSymbol", CancellationToken.None);

        Assert.Equal("found", result.Status);
        Assert.Contains("ResolvedSymbol", result.SymbolName);
        Assert.NotNull(result.Summary);
        Assert.NotEmpty(result.Summary);
    }

    [Fact]
    public async Task GetSymbolDocs_AmbiguousMethod_ReturnsAmbiguous()
    {
        // DisposeAsync exists on CSharpWorkspaceSession, WorkspaceSessionHolder, and WorkspaceFileWatcher,
        // so unqualified lookup should surface the ambiguity rather than silently picking one.
        var result = await GetSymbolDocsTool.GetSymbolDocs(
            _holder, _query, NullLogger<GetSymbolDocsTool>.Instance,
            "DisposeAsync", CancellationToken.None);

        Assert.Equal("ambiguous", result.Status);
        Assert.NotEmpty(result.Candidates);
    }

    [Fact]
    public async Task GetSymbolDocs_TypeWithoutDocs_ReturnsNoDocs()
    {
        // WorkspaceLoadFailure has no XML doc comments
        var result = await GetSymbolDocsTool.GetSymbolDocs(
            _holder, _query, NullLogger<GetSymbolDocsTool>.Instance,
            "WorkspaceLoadFailure", CancellationToken.None);

        Assert.Equal("no_docs", result.Status);
        Assert.Equal("WorkspaceLoadFailure", result.SymbolName);
        Assert.Null(result.Summary);
    }

    [Fact]
    public async Task GetSymbolDocs_NotFound_ReturnsNotFound()
    {
        var result = await GetSymbolDocsTool.GetSymbolDocs(
            _holder, _query, NullLogger<GetSymbolDocsTool>.Instance,
            "ThisSymbolDoesNotExistAnywhere", CancellationToken.None);

        Assert.Equal("not_found", result.Status);
        Assert.Contains("ThisSymbolDoesNotExistAnywhere", result.Message);
    }

    [Fact]
    public void GetSymbolDocs_NotLoaded_ReturnsNotLoaded()
    {
        var holder = new WorkspaceSessionHolder();
        var query = new WorkspaceQueryService(holder, NullLogger<WorkspaceQueryService>.Instance);

        var result = GetSymbolDocsTool.GetSymbolDocs(
            holder, query, NullLogger<GetSymbolDocsTool>.Instance,
            "Anything", CancellationToken.None).Result;

        Assert.Equal("not_loaded", result.Status);
    }

    [Fact]
    public void GetSymbolDocs_LoadFailed_ReturnsLoadFailed()
    {
        var holder = new WorkspaceSessionHolder();
        holder.SetLoadFailure(new WorkspaceLoadFailure("boom", "/path.sln"));
        var query = new WorkspaceQueryService(holder, NullLogger<WorkspaceQueryService>.Instance);

        var result = GetSymbolDocsTool.GetSymbolDocs(
            holder, query, NullLogger<GetSymbolDocsTool>.Instance,
            "Anything", CancellationToken.None).Result;

        Assert.Equal("load_failed", result.Status);
        Assert.Equal("boom", result.Message);
    }
}
