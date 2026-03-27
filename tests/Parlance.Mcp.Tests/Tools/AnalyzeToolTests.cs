using System.Collections.Immutable;
using Microsoft.Extensions.Logging.Abstractions;
using Parlance.Analysis;
using Parlance.Analysis.Curation;
using Parlance.CSharp.Workspace;
using Parlance.CSharp.Workspace.Tests.Integration;
using Parlance.Mcp.Tools;

namespace Parlance.Mcp.Tests.Tools;

public sealed class AnalyzeToolTests : IAsyncLifetime
{
    private WorkspaceSessionHolder _holder = null!;
    private WorkspaceQueryService _query = null!;
    private AnalysisService _service = null!;
    private CSharpWorkspaceSession _session = null!;

    public async Task InitializeAsync()
    {
        var solutionPath = TestPaths.FindSolutionPath();
        _session = await CSharpWorkspaceSession.OpenSolutionAsync(solutionPath);
        _holder = new WorkspaceSessionHolder();
        _holder.SetSession(_session);
        _query = new WorkspaceQueryService(_holder, NullLogger<WorkspaceQueryService>.Instance);
        var curationProvider = new CurationSetProvider(NullLogger<CurationSetProvider>.Instance);
        _service = new AnalysisService(
            _holder, _query, curationProvider,
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

        var result = AnalyzeTool.Analyze(
            holder, service, NullLogger<AnalyzeTool>.Instance,
            ["test.cs"], null, null, CancellationToken.None).Result;

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

        var result = AnalyzeTool.Analyze(
            holder, service, NullLogger<AnalyzeTool>.Instance,
            ["test.cs"], null, null, CancellationToken.None).Result;

        Assert.Equal("load_failed", result.Status);
    }

    [Fact]
    public async Task Analyze_KnownFile_ReturnsResults()
    {
        var solutionDir = Path.GetDirectoryName(TestPaths.FindSolutionPath())!;
        var filePath = Path.Combine(solutionDir, "src", "Parlance.Abstractions", "Diagnostic.cs");

        var result = await AnalyzeTool.Analyze(
            _holder, _service, NullLogger<AnalyzeTool>.Instance,
            [filePath], null, null, CancellationToken.None);

        Assert.Equal("success", result.Status);
        Assert.NotNull(result.Summary);
        Assert.NotNull(result.Diagnostics);
    }
}
