using System.Collections.Immutable;
using System.ComponentModel;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Server;
using Parlance.Analysis;
using Parlance.Analysis.Curation;
using Parlance.CSharp.Workspace;
using Parlance.CSharp.Workspace.Tests.Integration;
using Parlance.Mcp.Tools;

namespace Parlance.Mcp.Tests.Tools;

public sealed class AnalyzeFilesToolTests : IAsyncLifetime
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
    public void AnalyzeFiles_HasExplicitToolNameAndShellExamples()
    {
        var method = typeof(AnalyzeFilesTool).GetMethod(nameof(AnalyzeFilesTool.AnalyzeFiles))!;
        var tool = method.GetCustomAttribute<McpServerToolAttribute>()!;
        var description = method.GetCustomAttribute<DescriptionAttribute>()!.Description;

        Assert.Equal("analyze-files", tool.Name);
        Assert.Contains("workspace-relative", description);
        Assert.Contains("ls *.cs", description);
        Assert.Contains("git diff --name-only main...HEAD -- '*.cs'", description);
    }

    [Fact]
    public void NotLoaded_ReturnsNotLoaded()
    {
        var holder = new WorkspaceSessionHolder();
        var query = new WorkspaceQueryService(holder, NullLogger<WorkspaceQueryService>.Instance);
        var curationProvider = new CurationSetProvider(NullLogger<CurationSetProvider>.Instance);
        var service = new AnalysisService(
            holder, query, curationProvider,
            NullLogger<AnalysisService>.Instance);

        var result = AnalyzeFilesTool.AnalyzeFiles(
            holder, service,
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

        var result = AnalyzeFilesTool.AnalyzeFiles(
            holder, service,
            ["test.cs"], null, null, CancellationToken.None).Result;

        Assert.Equal("load_failed", result.Status);
    }

    [Fact]
    public async Task Analyze_KnownFile_ReturnsResults()
    {
        var solutionDir = Path.GetDirectoryName(TestPaths.FindSolutionPath())!;
        var filePath = Path.Combine(solutionDir, "src", "Parlance.Abstractions", "Diagnostic.cs");

        var result = await AnalyzeFilesTool.AnalyzeFiles(
            _holder, _service,
            [filePath], null, null, CancellationToken.None);

        Assert.Equal("success", result.Status);
        Assert.NotNull(result.Summary);
        Assert.NotNull(result.Diagnostics);
    }

    [Fact]
    public async Task Analyze_WorkspaceRelativeFile_ReturnsResults()
    {
        var result = await AnalyzeFilesTool.AnalyzeFiles(
            _holder, _service,
            ["src/Parlance.Mcp/Program.cs"], null, null, CancellationToken.None);

        Assert.Equal("success", result.Status);
        Assert.True(result.Summary.HasValue);
        Assert.True(result.Summary.Value.Total > 0);
        Assert.NotNull(result.Diagnostics);
    }
}
