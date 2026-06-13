using Microsoft.Extensions.Logging.Abstractions;
using Parlance.Analysis;
using Parlance.CSharp.Workspace;
using Parlance.Mcp;
using Parlance.Mcp.Tools;

namespace Parlance.Mcp.Tests.Tools;

/// <summary>
/// Characterization tests for how MCP tools map non-loaded <see cref="WorkspaceState"/>
/// cases to their wire DTOs. Guards the Task 1.8 migration that routes every tool's
/// state dispatch through <see cref="WorkspaceState.Match{T}"/>. The exhaustiveness win
/// is compile-time; these lock the runtime behaviour, especially that <c>Disposed</c>
/// is treated as "not loaded" by gate tools and as "loading" by workspace-status.
/// </summary>
public sealed class ToolStateDispatchTests
{
    private static WorkspaceQueryService QueryFor(WorkspaceSessionHolder holder) =>
        new(holder, NullLogger<WorkspaceQueryService>.Instance);

    // --- gate-only tool: SearchSymbols ---

    [Fact]
    public async Task SearchSymbols_NotLoaded_ReturnsNotLoaded()
    {
        using var holder = new WorkspaceSessionHolder();

        var result = await SearchSymbolsTool.SearchSymbols(
            holder, QueryFor(holder), "Anything", null, 25, CancellationToken.None);

        Assert.Equal("not_loaded", result.Status);
    }

    [Fact]
    public async Task SearchSymbols_Disposed_ReturnsNotLoaded()
    {
        var holder = new WorkspaceSessionHolder();
        holder.Dispose();

        var result = await SearchSymbolsTool.SearchSymbols(
            holder, QueryFor(holder), "Anything", null, 25, CancellationToken.None);

        Assert.Equal("not_loaded", result.Status);
    }

    [Fact]
    public async Task SearchSymbols_LoadFailed_ReturnsLoadFailed()
    {
        using var holder = new WorkspaceSessionHolder();
        holder.SetLoadFailure(new WorkspaceLoadFailure("boom", "/x.sln"));

        var result = await SearchSymbolsTool.SearchSymbols(
            holder, QueryFor(holder), "Anything", null, 25, CancellationToken.None);

        Assert.Equal("load_failed", result.Status);
        Assert.Equal("boom", result.Message);
    }

    // --- session-needing tool: GetTypeDependencies ---

    [Fact]
    public async Task GetTypeDependencies_NotLoaded_ReturnsNotLoaded()
    {
        using var holder = new WorkspaceSessionHolder();

        var result = await GetTypeDependenciesTool.GetTypeDependencies(
            holder, QueryFor(holder), "Anything", CancellationToken.None);

        Assert.Equal("not_loaded", result.Status);
    }

    [Fact]
    public async Task GetTypeDependencies_Disposed_ReturnsNotLoaded()
    {
        var holder = new WorkspaceSessionHolder();
        holder.Dispose();

        var result = await GetTypeDependenciesTool.GetTypeDependencies(
            holder, QueryFor(holder), "Anything", CancellationToken.None);

        Assert.Equal("not_loaded", result.Status);
    }

    [Fact]
    public async Task GetTypeDependencies_LoadFailed_ReturnsLoadFailed()
    {
        using var holder = new WorkspaceSessionHolder();
        holder.SetLoadFailure(new WorkspaceLoadFailure("boom", "/x.sln"));

        var result = await GetTypeDependenciesTool.GetTypeDependencies(
            holder, QueryFor(holder), "Anything", CancellationToken.None);

        Assert.Equal("load_failed", result.Status);
        Assert.Equal("boom", result.Message);
    }

    // --- sync tool: WorkspaceStatus (Disposed/NotLoaded map to "Loading", not "not_loaded") ---

    private static ParlanceMcpConfiguration Config() => new("/x.sln", "/logs");

    private static AnalyzerProvider EmptyProvider => new([]);

    [Fact]
    public void WorkspaceStatus_NotLoaded_ReturnsLoading()
    {
        using var holder = new WorkspaceSessionHolder();

        var result = WorkspaceStatusTool.GetStatus(
            holder, Config(), EmptyProvider, NullLogger<WorkspaceStatusTool>.Instance);

        Assert.Equal("Loading", result.Status);
    }

    [Fact]
    public void WorkspaceStatus_Disposed_ReturnsLoading()
    {
        var holder = new WorkspaceSessionHolder();
        holder.Dispose();

        var result = WorkspaceStatusTool.GetStatus(
            holder, Config(), EmptyProvider, NullLogger<WorkspaceStatusTool>.Instance);

        Assert.Equal("Loading", result.Status);
    }

    [Fact]
    public void WorkspaceStatus_LoadFailed_ReturnsFailed()
    {
        using var holder = new WorkspaceSessionHolder();
        holder.SetLoadFailure(new WorkspaceLoadFailure("boom", "/x.sln"));

        var result = WorkspaceStatusTool.GetStatus(
            holder, Config(), EmptyProvider, NullLogger<WorkspaceStatusTool>.Instance);

        Assert.Equal("Failed", result.Status);
    }
}
