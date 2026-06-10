using Microsoft.Extensions.Logging.Abstractions;
using Parlance.CSharp.Workspace;
using Parlance.CSharp.Workspace.Tests.Integration;
using Parlance.Mcp.Tools;

namespace Parlance.Mcp.Tests.Tools;

[Trait("Category", "Integration")]
public sealed class OutlineFileToolTests(WorkspaceFixture fixture) : IClassFixture<WorkspaceFixture>
{
    private readonly WorkspaceSessionHolder _holder = fixture.Holder;
    private readonly WorkspaceQueryService _query = fixture.Query;

    [Fact]
    public async Task OutlineFile_KnownFile_ReturnsFoundWithTypes()
    {
        var filePath = Path.Combine(TestPaths.RepoRoot, "src", "Parlance.CSharp.Workspace", "CSharpWorkspaceSession.cs");

        var result = await OutlineFileTool.OutlineFile(
            _holder, _query, filePath, CancellationToken.None);

        Assert.Equal("found", result.Status);
        Assert.Null(result.FilePath);
        Assert.Contains(result.Types, t => t.Name == "CSharpWorkspaceSession");
    }

    [Fact]
    public async Task OutlineFile_KnownFile_ReturnsMethodAndPropertyMembers()
    {
        var filePath = Path.Combine(TestPaths.RepoRoot, "src", "Parlance.CSharp.Workspace", "CSharpWorkspaceSession.cs");

        var result = await OutlineFileTool.OutlineFile(
            _holder, _query, filePath, CancellationToken.None);

        Assert.Equal("found", result.Status);
        var sessionType = Assert.Single(result.Types, t => t.Name == "CSharpWorkspaceSession");
        Assert.NotEmpty(sessionType.Members);
        Assert.Contains(sessionType.Members, m => m.Kind == "Method" || m.Kind == "Property");
    }

    [Fact]
    public async Task OutlineFile_UnknownFile_ReturnsNotFound()
    {
        var result = await OutlineFileTool.OutlineFile(
            _holder, _query,
            "/does/not/exist/Fake.cs", CancellationToken.None);

        Assert.Equal("not_found", result.Status);
        Assert.NotNull(result.Message);
    }

    [Fact]
    public void OutlineFile_NotLoaded_ReturnsNotLoaded()
    {
        var holder = new WorkspaceSessionHolder();
        var query = new WorkspaceQueryService(holder, NullLogger<WorkspaceQueryService>.Instance);

        var result = OutlineFileTool.OutlineFile(
            holder, query,
            "/any/path.cs", CancellationToken.None).Result;

        Assert.Equal("not_loaded", result.Status);
    }

    [Fact]
    public async Task OutlineFile_MultipleTypes_ReturnsAllTypes()
    {
        var filePath = Path.Combine(TestPaths.RepoRoot,
            "src", "Parlance.Mcp", "Tools", "WorkspaceStatusResult.cs");
        var result = await OutlineFileTool.OutlineFile(
            _holder, _query, filePath, CancellationToken.None);

        Assert.Equal("found", result.Status);
        Assert.True(result.Types.Count >= 2);
    }

    [Fact]
    public void OutlineFile_LoadFailed_ReturnsLoadFailed()
    {
        var holder = new WorkspaceSessionHolder();
        holder.SetLoadFailure(new WorkspaceLoadFailure("boom", "/path.sln"));
        var query = new WorkspaceQueryService(holder, NullLogger<WorkspaceQueryService>.Instance);

        var result = OutlineFileTool.OutlineFile(
            holder, query,
            "/any/path.cs", CancellationToken.None).Result;

        Assert.Equal("load_failed", result.Status);
    }
}
