using Microsoft.Extensions.Logging.Abstractions;
using Parlance.Analysis;
using Parlance.Analysis.Curation;
using Parlance.CSharp.Workspace;
using Parlance.CSharp.Workspace.Tests.Integration;
using Parlance.Mcp.Tools;

namespace Parlance.Mcp.Tests.Tools;

public sealed class AnalyzeDiffToolTests : IAsyncLifetime
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

        var result = AnalyzeDiffTool.AnalyzeDiff(
            holder, service, "HEAD", null, 1, CancellationToken.None).Result;

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

        var result = AnalyzeDiffTool.AnalyzeDiff(
            holder, service, "HEAD", null, 1, CancellationToken.None).Result;

        Assert.Equal("load_failed", result.Status);
        Assert.Equal("boom", result.Error);
    }

    [Fact]
    public async Task AnalyzeDiff_HeadBase_ReturnsSuccessWithoutCallerFilePaths()
    {
        var head = await Git("rev-parse", "HEAD");

        var result = await AnalyzeDiffTool.AnalyzeDiff(
            _holder, _service, head.Trim(), null, 1, CancellationToken.None);

        Assert.Equal("success", result.Status);
        Assert.True(result.Summary.HasValue);
        Assert.NotNull(result.Diagnostics);
        Assert.Equal(0, result.Summary.Value.Total);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task AnalyzeDiff_InvalidBaseRef_ReturnsError()
    {
        var result = await AnalyzeDiffTool.AnalyzeDiff(
            _holder, _service, "refs/heads/does-not-exist-for-test", null, 1, CancellationToken.None);

        Assert.Equal("error", result.Status);
        Assert.Contains("git diff failed", result.Error);
    }

    private static async Task<string> Git(params string[] args)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo("git")
        {
            WorkingDirectory = TestPaths.RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);

        using var process = System.Diagnostics.Process.Start(startInfo)!;
        var stdout = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return stdout;
    }
}
