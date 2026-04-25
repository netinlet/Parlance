using Microsoft.Extensions.Logging.Abstractions;
using Parlance.CSharp.Workspace;
using Parlance.CSharp.Workspace.Tests.Integration;
using Parlance.Mcp.Tools;

namespace Parlance.Mcp.Tests.Tools;

public sealed class CallHierarchyToolTests : IAsyncLifetime
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
    public async Task GetCallHierarchy_KnownMethod_HasCallers()
    {
        // RefreshAsync is called from multiple tests in RefreshTests.cs
        var result = await CallHierarchyTool.GetCallHierarchy(
            _holder, _query,
            "RefreshAsync", CancellationToken.None);

        Assert.Equal("found", result.Status);
        Assert.NotNull(result.TargetMethod);
        Assert.Contains("RefreshAsync", result.TargetMethod);
        Assert.NotEmpty(result.Callers);
    }

    [Fact]
    public async Task GetCallHierarchy_KnownMethod_HasCallees()
    {
        // RefreshAsync calls other methods internally (GetDocumentsAsync, etc.)
        var result = await CallHierarchyTool.GetCallHierarchy(
            _holder, _query,
            "RefreshAsync", CancellationToken.None);

        Assert.Equal("found", result.Status);
        Assert.NotNull(result.TargetMethod);
        Assert.Contains("RefreshAsync", result.TargetMethod);
        Assert.NotEmpty(result.Callees);
    }

    [Fact]
    public async Task GetCallHierarchy_NotFound_ReturnsNotFound()
    {
        var result = await CallHierarchyTool.GetCallHierarchy(
            _holder, _query,
            "ThisMethodDoesNotExistAnywhere", CancellationToken.None);

        Assert.Equal("not_found", result.Status);
        Assert.Empty(result.Callers);
        Assert.Empty(result.Callees);
    }

    [Fact]
    public void GetCallHierarchy_NotLoaded_ReturnsNotLoaded()
    {
        var holder = new WorkspaceSessionHolder();
        var query = new WorkspaceQueryService(holder, NullLogger<WorkspaceQueryService>.Instance);

        var result = CallHierarchyTool.GetCallHierarchy(
            holder, query,
            "Anything", CancellationToken.None).Result;

        Assert.Equal("not_loaded", result.Status);
        Assert.Empty(result.Callers);
        Assert.Empty(result.Callees);
    }

    [Fact]
    public void GetCallHierarchy_LoadFailed_ReturnsLoadFailed()
    {
        var holder = new WorkspaceSessionHolder();
        holder.SetLoadFailure(new WorkspaceLoadFailure("boom", "/path.sln"));
        var query = new WorkspaceQueryService(holder, NullLogger<WorkspaceQueryService>.Instance);

        var result = CallHierarchyTool.GetCallHierarchy(
            holder, query,
            "Anything", CancellationToken.None).Result;

        Assert.Equal("load_failed", result.Status);
        Assert.Equal("boom", result.Message);
        Assert.Empty(result.Callers);
        Assert.Empty(result.Callees);
    }
}
