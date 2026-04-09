using Microsoft.Extensions.Logging.Abstractions;
using Parlance.CSharp.Workspace;
using Parlance.CSharp.Workspace.Tests.Integration;
using Parlance.Mcp.Tools;

namespace Parlance.Mcp.Tests.Tools;

public sealed class DescribeTypeToolTests : IAsyncLifetime
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
    public async Task DescribeType_FindsKnownType()
    {
        var result = await DescribeTypeTool.DescribeType(
            _holder, _query, TestAnalytics.Instance,
            "CSharpWorkspaceSession", CancellationToken.None);

        Assert.Equal("found", result.Status);
        Assert.Equal("CSharpWorkspaceSession", result.Name);
        Assert.Equal("Parlance.CSharp.Workspace.CSharpWorkspaceSession", result.FullyQualifiedName);
        Assert.NotNull(result.FilePath);
        Assert.Contains("CSharpWorkspaceSession", result.FilePath);
        Assert.True(result.Line > 0, $"Expected 1-based line number > 0, got {result.Line}");
        Assert.NotEmpty(result.Members);
    }

    [Fact]
    public async Task DescribeType_FullyQualifiedName_ResolvesUnambiguously()
    {
        var result = await DescribeTypeTool.DescribeType(
            _holder, _query, TestAnalytics.Instance,
            "Parlance.CSharp.Workspace.CSharpWorkspaceSession", CancellationToken.None);

        Assert.Equal("found", result.Status);
        Assert.Equal("Parlance.CSharp.Workspace.CSharpWorkspaceSession", result.FullyQualifiedName);
    }

    [Fact]
    public async Task DescribeType_AmbiguousName_ReturnsAmbiguousWithCandidates()
    {
        // "Diagnostic" exists in both Parlance.Abstractions and Microsoft.CodeAnalysis
        var result = await DescribeTypeTool.DescribeType(
            _holder, _query, TestAnalytics.Instance,
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
            _holder, _query, TestAnalytics.Instance,
            "ThisTypeDoesNotExist", CancellationToken.None);

        Assert.Equal("not_found", result.Status);
    }

    [Fact]
    public void DescribeType_NotLoaded_ReturnsNotLoaded()
    {
        var holder = new WorkspaceSessionHolder();
        var query = new WorkspaceQueryService(holder, NullLogger<WorkspaceQueryService>.Instance);

        var result = DescribeTypeTool.DescribeType(
            holder, query, TestAnalytics.Instance,
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
            holder, query, TestAnalytics.Instance,
            "Anything", CancellationToken.None).Result;

        Assert.Equal("load_failed", result.Status);
    }
}
