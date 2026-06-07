using Microsoft.Extensions.Logging.Abstractions;
using Parlance.CSharp.Workspace;
using Parlance.CSharp.Workspace.Tests.Integration;
using Parlance.Mcp.Tools;

namespace Parlance.Mcp.Tests.Tools;

[Trait("Category", "Integration")]
public sealed class SafeToDeleteToolTests(WorkspaceFixture fixture) : IClassFixture<WorkspaceFixture>
{
    private readonly WorkspaceSessionHolder _holder = fixture.Holder;
    private readonly WorkspaceQueryService _query = fixture.Query;

    [Fact]
    public async Task CheckSafeToDelete_ReferencedType_ReturnsSafeIsFalse()
    {
        // CSharpWorkspaceSession is referenced in many files across the solution
        var result = await SafeToDeleteTool.CheckSafeToDelete(
            _holder, _query,
            "CSharpWorkspaceSession", CancellationToken.None);

        Assert.Equal("found", result.Status);
        Assert.NotNull(result.SymbolName);
        Assert.Contains("CSharpWorkspaceSession", result.SymbolName);
        Assert.False(result.Safe);
        Assert.True(result.ReferenceCount > 0);
    }

    [Fact]
    public async Task CheckSafeToDelete_ReferencedType_ReturnsSampleLocations()
    {
        var result = await SafeToDeleteTool.CheckSafeToDelete(
            _holder, _query,
            "CSharpWorkspaceSession", CancellationToken.None);

        Assert.Equal("found", result.Status);
        Assert.NotEmpty(result.SampleLocations);
        Assert.True(result.SampleLocations.Count <= 5);
        Assert.All(result.SampleLocations, loc =>
        {
            Assert.NotNull(loc.FilePath);
            Assert.True(loc.Line > 0);
        });
    }

    [Fact]
    public async Task CheckSafeToDelete_NotFound_ReturnsNotFound()
    {
        var result = await SafeToDeleteTool.CheckSafeToDelete(
            _holder, _query,
            "ThisSymbolDoesNotExistAnywhere", CancellationToken.None);

        Assert.Equal("not_found", result.Status);
        Assert.False(result.Safe);
        Assert.Equal(0, result.ReferenceCount);
        Assert.Empty(result.SampleLocations);
    }

    [Fact]
    public void CheckSafeToDelete_NotLoaded_ReturnsNotLoaded()
    {
        var holder = new WorkspaceSessionHolder();
        var query = new WorkspaceQueryService(holder, NullLogger<WorkspaceQueryService>.Instance);

        var result = SafeToDeleteTool.CheckSafeToDelete(
            holder, query,
            "Anything", CancellationToken.None).Result;

        Assert.Equal("not_loaded", result.Status);
        Assert.False(result.Safe);
        Assert.Equal(0, result.ReferenceCount);
        Assert.Empty(result.SampleLocations);
    }

    [Fact]
    public async Task CheckSafeToDelete_SafeMatchesZeroReferenceCount()
    {
        // Verifies the Safe field correctly reflects ReferenceCount == 0 on a real workspace result
        var result = await SafeToDeleteTool.CheckSafeToDelete(
            _holder, _query,
            "CSharpWorkspaceSession", CancellationToken.None);

        Assert.Equal("found", result.Status);
        Assert.Equal(result.ReferenceCount == 0, result.Safe);
    }

    [Fact]
    public void CheckSafeToDelete_LoadFailed_ReturnsFailure()
    {
        var holder = new WorkspaceSessionHolder();
        holder.SetLoadFailure(new WorkspaceLoadFailure("boom", "/path.sln"));
        var query = new WorkspaceQueryService(holder, NullLogger<WorkspaceQueryService>.Instance);

        var result = SafeToDeleteTool.CheckSafeToDelete(
            holder, query,
            "Anything", CancellationToken.None).Result;

        Assert.Equal("load_failed", result.Status);
        Assert.Equal("boom", result.Message);
    }
}
