using System.Collections.Immutable;
using Microsoft.Extensions.Logging.Abstractions;
using Parlance.Analysis.Curation;
using Parlance.CSharp.Workspace;
using Parlance.CSharp.Workspace.Tests.Integration;

namespace Parlance.Analysis.Tests;

[Trait("Category", "Integration")]
public sealed class AnalysisServiceTests(WorkspaceFixture fixture) : IClassFixture<WorkspaceFixture>
{
    private readonly AnalysisService _service = new(
        fixture.Holder, fixture.Query,
        new CurationSetProvider(NullLogger<CurationSetProvider>.Instance),
        AnalyzerProviderTestFactory.CreateWithBundled(),
        NullLogger<AnalysisService>.Instance);

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
