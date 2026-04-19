using Microsoft.Extensions.Logging.Abstractions;
using Parlance.Analysis;
using Parlance.Analysis.Curation;
using Parlance.CSharp.Workspace;
using Parlance.CSharp.Workspace.Tests.Integration;
using Parlance.Mcp.Tools;

namespace Parlance.Mcp.Tests.Tools;

public sealed class AnalyzeProjectToolTests : IAsyncLifetime
{
    private WorkspaceSessionHolder _holder = null!;
    private AnalysisService _service = null!;
    private CSharpWorkspaceSession _session = null!;

    public async Task InitializeAsync()
    {
        _session = await CSharpWorkspaceSession.OpenSolutionAsync(TestPaths.FindSolutionPath());
        _holder = new WorkspaceSessionHolder();
        _holder.SetSession(_session);

        var query = new WorkspaceQueryService(_holder, NullLogger<WorkspaceQueryService>.Instance);
        var curationProvider = new CurationSetProvider(NullLogger<CurationSetProvider>.Instance);
        _service = new AnalysisService(
            _holder, query, curationProvider,
            NullLogger<AnalysisService>.Instance);
    }

    public async Task DisposeAsync() => await _session.DisposeAsync();

    [Fact]
    public void NotLoaded_ReturnsNotLoaded()
    {
        var holder = new WorkspaceSessionHolder();
        var query = new WorkspaceQueryService(holder, NullLogger<WorkspaceQueryService>.Instance);
        var curationProvider = new CurationSetProvider(NullLogger<CurationSetProvider>.Instance);
        var service = new AnalysisService(
            holder, query, curationProvider,
            NullLogger<AnalysisService>.Instance);

        var result = AnalyzeProjectTool.AnalyzeProject(
            holder, service, null, null, 1, CancellationToken.None).Result;

        Assert.Equal("not_loaded", result.Status);
    }

    [Fact]
    public void LoadFailed_ReturnsLoadFailed()
    {
        var holder = new WorkspaceSessionHolder();
        holder.SetLoadFailure(new WorkspaceLoadFailure("boom", "/path.sln"));
        var query = new WorkspaceQueryService(holder, NullLogger<WorkspaceQueryService>.Instance);
        var curationProvider = new CurationSetProvider(NullLogger<CurationSetProvider>.Instance);
        var service = new AnalysisService(
            holder, query, curationProvider,
            NullLogger<AnalysisService>.Instance);

        var result = AnalyzeProjectTool.AnalyzeProject(
            holder, service, null, null, 1, CancellationToken.None).Result;

        Assert.Equal("load_failed", result.Status);
        Assert.Equal("boom", result.Error);
    }

    [Fact]
    public async Task AnalyzeProject_KnownProject_ReturnsResultsWithoutCallerFilePaths()
    {
        var result = await AnalyzeProjectTool.AnalyzeProject(
            _holder, _service, "Parlance.Abstractions", null, 1, CancellationToken.None);

        Assert.Equal("success", result.Status);
        Assert.True(result.Summary.HasValue);
        Assert.NotNull(result.Diagnostics);
        Assert.True(result.Summary.Value.Score >= 0);
    }

    [Fact]
    public async Task AnalyzeProject_NoProjectName_AnalyzesSolutionDocuments()
    {
        var result = await AnalyzeProjectTool.AnalyzeProject(
            _holder, _service, null, null, 1, CancellationToken.None);

        Assert.Equal("success", result.Status);
        Assert.True(result.Summary.HasValue);
        Assert.NotNull(result.Diagnostics);
    }

    [Fact]
    public async Task AnalyzeProject_UnknownProject_ReturnsError()
    {
        var result = await AnalyzeProjectTool.AnalyzeProject(
            _holder, _service, "Does.Not.Exist", null, 1, CancellationToken.None);

        Assert.Equal("error", result.Status);
        Assert.Equal("Project 'Does.Not.Exist' was not found.", result.Error);
    }
}
