using System.Collections.Immutable;
using Microsoft.Extensions.Logging.Abstractions;
using Parlance.Analysis.Curation;
using Parlance.CSharp.Workspace;
using Parlance.CSharp.Workspace.Tests.Integration;

namespace Parlance.Analysis.Tests;

public sealed class AnalysisServiceTests : IAsyncLifetime
{
    private WorkspaceSessionHolder _holder = null!;
    private WorkspaceQueryService _query = null!;
    private CSharpWorkspaceSession _session = null!;
    private AnalysisService _service = null!;

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
    public async Task AnalyzeFiles_KnownFile_ReturnsDiagnostics()
    {
        var abstractionsDir = Path.GetDirectoryName(TestPaths.FindSolutionPath())!;
        var filePath = Path.Combine(abstractionsDir, "src", "Parlance.Abstractions", "Diagnostic.cs");

        var result = await _service.AnalyzeFilesAsync([filePath]);

        Assert.NotNull(result);
        Assert.Equal("default", result.CurationSet);
        Assert.NotNull(result.Summary);
        Assert.True(result.Summary.IdiomaticScore >= 0);
        Assert.True(result.Summary.IdiomaticScore <= 100);
    }

    [Fact]
    public async Task AnalyzeFiles_FileNotInWorkspace_ReturnsEmptyDiagnostics()
    {
        var result = await _service.AnalyzeFilesAsync(["/nonexistent/file.cs"]);

        Assert.NotNull(result);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task AnalyzeFiles_MaxDiagnostics_CapsOutput()
    {
        var abstractionsDir = Path.GetDirectoryName(TestPaths.FindSolutionPath())!;
        var filePath = Path.Combine(abstractionsDir, "src", "Parlance.Abstractions", "Diagnostic.cs");

        var result = await _service.AnalyzeFilesAsync(
            [filePath],
            new AnalyzeOptions(MaxDiagnostics: 1));

        Assert.True(result.Diagnostics.Count <= 1);
    }

    [Fact]
    public async Task AnalyzeFiles_UnknownCurationSet_ReturnsError()
    {
        var abstractionsDir = Path.GetDirectoryName(TestPaths.FindSolutionPath())!;
        var filePath = Path.Combine(abstractionsDir, "src", "Parlance.Abstractions", "Diagnostic.cs");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.AnalyzeFilesAsync(
                [filePath],
                new AnalyzeOptions(CurationSetName: "nonexistent")));
    }
}
