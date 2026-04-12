using Microsoft.Extensions.Logging.Abstractions;
using Parlance.CSharp.Workspace;
using Parlance.CSharp.Workspace.Tests.Integration;
using Parlance.Mcp.Tools;

namespace Parlance.Mcp.Tests.Tools;

public sealed class DecompileTypeToolTests : IAsyncLifetime
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
    public async Task DecompileType_ExternalType_ReturnsDecompiledSource()
    {
        var result = await DecompileTypeTool.DecompileType(
            _holder, _query, NullLogger<DecompileTypeTool>.Instance,
            "Microsoft.CodeAnalysis.Project", CancellationToken.None);

        Assert.Equal("found", result.Status);
        Assert.NotNull(result.DecompiledSource);
        Assert.NotEmpty(result.DecompiledSource);
        Assert.NotNull(result.AssemblyName);
        Assert.NotNull(result.AssemblyPath);
    }

    [Fact]
    public async Task DecompileType_NonexistentType_ReturnsNotFound()
    {
        var result = await DecompileTypeTool.DecompileType(
            _holder, _query, NullLogger<DecompileTypeTool>.Instance,
            "This.Type.DoesNotExist", CancellationToken.None);

        Assert.Equal("not_found", result.Status);
    }

    [Fact]
    public void DecompileType_NotLoaded_ReturnsNotLoaded()
    {
        var holder = new WorkspaceSessionHolder();
        var query = new WorkspaceQueryService(holder, NullLogger<WorkspaceQueryService>.Instance);

        var result = DecompileTypeTool.DecompileType(
            holder, query, NullLogger<DecompileTypeTool>.Instance,
            "Anything", CancellationToken.None).Result;

        Assert.Equal("not_loaded", result.Status);
    }

    [Fact]
    public void DecompileType_LoadFailed_ReturnsLoadFailed()
    {
        var holder = new WorkspaceSessionHolder();
        holder.SetLoadFailure(new WorkspaceLoadFailure("boom", "/path.sln"));
        var query = new WorkspaceQueryService(holder, NullLogger<WorkspaceQueryService>.Instance);

        var result = DecompileTypeTool.DecompileType(
            holder, query, NullLogger<DecompileTypeTool>.Instance,
            "Anything", CancellationToken.None).Result;

        Assert.Equal("load_failed", result.Status);
    }
}
