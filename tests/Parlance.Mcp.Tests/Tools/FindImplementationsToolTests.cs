using Microsoft.Extensions.Logging.Abstractions;
using Parlance.CSharp.Workspace;
using Parlance.CSharp.Workspace.Tests.Integration;
using Parlance.Mcp.Tools;

namespace Parlance.Mcp.Tests.Tools;

public sealed class FindImplementationsToolTests(WorkspaceFixture fixture) : IClassFixture<WorkspaceFixture>
{
    private readonly WorkspaceSessionHolder _holder = fixture.Holder;
    private readonly WorkspaceQueryService _query = fixture.Query;

    [Fact]
    public async Task FindImplementations_FindsImplementorsOfKnownInterface()
    {
        var result = await FindImplementationsTool.FindImplementations(
            _holder, _query,
            "IOutputFormatter", CancellationToken.None);

        Assert.Equal("found", result.Status);
        Assert.NotNull(result.TargetType);
        Assert.Contains("IOutputFormatter", result.TargetType);
        Assert.NotEmpty(result.Implementations);
        Assert.All(result.Implementations, entry => Assert.NotNull(entry.FullyQualifiedName));
    }

    [Fact]
    public async Task FindImplementations_NotFound_ReturnsNotFound()
    {
        var result = await FindImplementationsTool.FindImplementations(
            _holder, _query,
            "IThisInterfaceDoesNotExist", CancellationToken.None);

        Assert.Equal("not_found", result.Status);
        Assert.Empty(result.Implementations);
    }

    [Fact]
    public void FindImplementations_NotLoaded_ReturnsNotLoaded()
    {
        var holder = new WorkspaceSessionHolder();
        var query = new WorkspaceQueryService(holder, NullLogger<WorkspaceQueryService>.Instance);

        var result = FindImplementationsTool.FindImplementations(
            holder, query,
            "Anything", CancellationToken.None).Result;

        Assert.Equal("not_loaded", result.Status);
        Assert.Empty(result.Implementations);
    }

    [Fact]
    public void FindImplementations_LoadFailed_ReturnsFailure()
    {
        var holder = new WorkspaceSessionHolder();
        holder.SetLoadFailure(new WorkspaceLoadFailure("boom", "/path.sln"));
        var query = new WorkspaceQueryService(holder, NullLogger<WorkspaceQueryService>.Instance);

        var result = FindImplementationsTool.FindImplementations(
            holder, query,
            "Anything", CancellationToken.None).Result;

        Assert.Equal("load_failed", result.Status);
        Assert.Equal("boom", result.Message);
    }
}
