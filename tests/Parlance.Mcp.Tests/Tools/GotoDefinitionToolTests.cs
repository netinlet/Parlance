using Microsoft.Extensions.Logging.Abstractions;
using Parlance.CSharp.Workspace;
using Parlance.CSharp.Workspace.Tests.Integration;
using Parlance.Mcp.Tools;

namespace Parlance.Mcp.Tests.Tools;

[Trait("Category", "Integration")]
public sealed class GotoDefinitionToolTests(WorkspaceFixture fixture) : IClassFixture<WorkspaceFixture>
{
    private readonly WorkspaceSessionHolder _holder = fixture.Holder;
    private readonly WorkspaceQueryService _query = fixture.Query;

    [Fact]
    public async Task GotoDefinition_ByName_FindsSourceDefinition()
    {
        var result = await GotoDefinitionTool.GotoDefinition(
            _holder, _query,
            symbolName: "CSharpWorkspaceSession",
            filePath: null, line: null, column: null,
            CancellationToken.None);

        Assert.Equal("found", result.Status);
        Assert.Equal("CSharpWorkspaceSession", result.SymbolName);
        Assert.False(result.IsMetadata);
        Assert.Null(result.AssemblyName);
        Assert.NotEmpty(result.Locations);
        Assert.All(result.Locations, loc =>
        {
            Assert.NotNull(loc.FilePath);
            Assert.NotEmpty(loc.FilePath.Value.Absolute);
            Assert.True(loc.Line > 0);
            Assert.NotNull(loc.Snippet);
        });
    }

    [Fact]
    public async Task GotoDefinition_ByPosition_FindsDefinition()
    {
        var filePath = Path.Combine(TestPaths.RepoRoot,
            "src", "Parlance.CSharp.Workspace", "WorkspaceQueryService.cs");
        var lines = await File.ReadAllLinesAsync(filePath);

        var refLine = Array.FindIndex(lines, l => l.Contains("holder.LoadedSession"));
        Assert.True(refLine >= 0, "Could not find 'holder.LoadedSession' reference");

        var refCol = lines[refLine].IndexOf("LoadedSession", StringComparison.Ordinal);

        var result = await GotoDefinitionTool.GotoDefinition(
            _holder, _query,
            symbolName: null,
            filePath: filePath, line: refLine + 1, column: refCol + 1,
            CancellationToken.None);

        Assert.Equal("found", result.Status);
        Assert.NotNull(result.SymbolName);
        Assert.NotEmpty(result.Locations);
    }

    [Fact]
    public async Task GotoDefinition_BothInputs_PositionTakesPrecedence()
    {
        var filePath = Path.Combine(TestPaths.RepoRoot,
            "src", "Parlance.CSharp.Workspace", "WorkspaceQueryService.cs");
        var lines = await File.ReadAllLinesAsync(filePath);

        var refLine = Array.FindIndex(lines, l => l.Contains("holder.LoadedSession"));
        Assert.True(refLine >= 0);
        var refCol = lines[refLine].IndexOf("LoadedSession", StringComparison.Ordinal);

        var result = await GotoDefinitionTool.GotoDefinition(
            _holder, _query,
            symbolName: "SymbolCandidate",
            filePath: filePath, line: refLine + 1, column: refCol + 1,
            CancellationToken.None);

        Assert.Equal("found", result.Status);
        Assert.DoesNotContain("SymbolCandidate", result.SymbolName);
    }

    [Fact]
    public async Task GotoDefinition_MetadataSymbol_ReturnsIsMetadata()
    {
        var result = await GotoDefinitionTool.GotoDefinition(
            _holder, _query,
            symbolName: "INamedTypeSymbol",
            filePath: null, line: null, column: null,
            CancellationToken.None);

        Assert.Equal("found", result.Status);
        Assert.True(result.IsMetadata);
        Assert.NotNull(result.AssemblyName);
        Assert.Empty(result.Locations);
    }

    [Fact]
    public async Task GotoDefinition_UnknownSymbol_ReturnsNotFound()
    {
        var result = await GotoDefinitionTool.GotoDefinition(
            _holder, _query,
            symbolName: "ThisSymbolDefinitelyDoesNotExist",
            filePath: null, line: null, column: null,
            CancellationToken.None);

        Assert.Equal("not_found", result.Status);
        Assert.Empty(result.Locations);
    }

    [Fact]
    public async Task GotoDefinition_AmbiguousName_ReturnsAmbiguous()
    {
        var result = await GotoDefinitionTool.GotoDefinition(
            _holder, _query,
            symbolName: "Diagnostic",
            filePath: null, line: null, column: null,
            CancellationToken.None);

        Assert.Equal("ambiguous", result.Status);
        Assert.NotEmpty(result.Candidates);
    }

    [Fact]
    public async Task GotoDefinition_NoInputs_ReturnsError()
    {
        var result = await GotoDefinitionTool.GotoDefinition(
            _holder, _query,
            symbolName: null,
            filePath: null, line: null, column: null,
            CancellationToken.None);

        Assert.Equal("error", result.Status);
        Assert.NotNull(result.Message);
    }

    [Fact]
    public async Task GotoDefinition_PartialPosition_ReturnsError()
    {
        var result = await GotoDefinitionTool.GotoDefinition(
            _holder, _query,
            symbolName: null,
            filePath: "/some/file.cs", line: 10, column: null,
            CancellationToken.None);

        Assert.Equal("error", result.Status);
        Assert.Contains("filePath, line, and column", result.Message);
    }

    [Fact]
    public void GotoDefinition_NotLoaded_ReturnsNotLoaded()
    {
        var holder = new WorkspaceSessionHolder();
        var query = new WorkspaceQueryService(holder, NullLogger<WorkspaceQueryService>.Instance);

        var result = GotoDefinitionTool.GotoDefinition(
            holder, query,
            symbolName: "Anything",
            filePath: null, line: null, column: null,
            CancellationToken.None).Result;

        Assert.Equal("not_loaded", result.Status);
    }

    [Fact]
    public void GotoDefinition_LoadFailed_ReturnsLoadFailed()
    {
        var holder = new WorkspaceSessionHolder();
        holder.SetLoadFailure(new WorkspaceLoadFailure("boom", "/path.sln"));
        var query = new WorkspaceQueryService(holder, NullLogger<WorkspaceQueryService>.Instance);

        var result = GotoDefinitionTool.GotoDefinition(
            holder, query,
            symbolName: "Anything",
            filePath: null, line: null, column: null,
            CancellationToken.None).Result;

        Assert.Equal("load_failed", result.Status);
        Assert.Equal("boom", result.Message);
    }

}
