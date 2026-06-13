using Microsoft.Extensions.Logging.Abstractions;
using Parlance.CSharp.Workspace;
using Parlance.CSharp.Workspace.Tests.Integration;
using Parlance.Mcp.Tools;

namespace Parlance.Mcp.Tests.Tools;

[Trait("Category", "Integration")]
public sealed class DescribeTypeToolTests(WorkspaceFixture fixture) : IClassFixture<WorkspaceFixture>
{
    private readonly WorkspaceSessionHolder _holder = fixture.Holder;
    private readonly WorkspaceQueryService _query = fixture.Query;

    [Fact]
    public async Task DescribeType_FindsKnownType()
    {
        var result = await DescribeTypeTool.DescribeType(
            _holder, _query,
            "CSharpWorkspaceSession", CancellationToken.None);

        Assert.Equal("found", result.Status);
        Assert.Equal("Parlance.CSharp.Workspace.CSharpWorkspaceSession", result.FullyQualifiedName);
        Assert.NotNull(result.FilePath);
        Assert.Contains("CSharpWorkspaceSession", result.FilePath!.Value.Absolute);
        Assert.True(result.Line > 0, $"Expected 1-based line number > 0, got {result.Line}");
        Assert.NotEmpty(result.Members);
    }

    [Fact]
    public async Task DescribeType_FullyQualifiedName_ResolvesUnambiguously()
    {
        var result = await DescribeTypeTool.DescribeType(
            _holder, _query,
            "Parlance.CSharp.Workspace.CSharpWorkspaceSession", CancellationToken.None);

        Assert.Equal("found", result.Status);
        Assert.Equal("Parlance.CSharp.Workspace.CSharpWorkspaceSession", result.FullyQualifiedName);
    }

    [Fact]
    public async Task DescribeType_AmbiguousName_ReturnsAmbiguousWithCandidates()
    {
        // "Diagnostic" exists in both Parlance.Abstractions and Microsoft.CodeAnalysis
        var result = await DescribeTypeTool.DescribeType(
            _holder, _query,
            "Diagnostic", CancellationToken.None);

        Assert.True(result.Status is "found" or "ambiguous",
            $"Expected 'found' or 'ambiguous', got '{result.Status}'");
        if (result.Status == "found")
            Assert.Equal("Parlance.Abstractions.Diagnostic", result.FullyQualifiedName);
        if (result.Status == "ambiguous")
            Assert.NotEmpty(result.Candidates);
    }

    [Fact]
    public async Task DescribeType_NotFound_ReturnsNotFound()
    {
        var result = await DescribeTypeTool.DescribeType(
            _holder, _query,
            "ThisTypeDoesNotExist", CancellationToken.None);

        Assert.Equal("not_found", result.Status);
    }

    [Fact]
    public void DescribeType_NotLoaded_ReturnsNotLoaded()
    {
        var holder = new WorkspaceSessionHolder();
        var query = new WorkspaceQueryService(holder, NullLogger<WorkspaceQueryService>.Instance);

        var result = DescribeTypeTool.DescribeType(
            holder, query,
            "Anything", CancellationToken.None).Result;

        Assert.Equal("not_loaded", result.Status);
    }

    [Fact]
    public void DescribeType_LoadFailed_ReturnsFailure()
    {
        var holder = new WorkspaceSessionHolder();
        holder.SetLoadFailure(new WorkspaceLoadFailure("boom", "/path.sln"));
        var query = new WorkspaceQueryService(holder, NullLogger<WorkspaceQueryService>.Instance);

        var result = DescribeTypeTool.DescribeType(
            holder, query,
            "Anything", CancellationToken.None).Result;

        Assert.Equal("load_failed", result.Status);
    }
}
