using Microsoft.Extensions.Logging.Abstractions;
using Parlance.CSharp.Workspace;
using Parlance.CSharp.Workspace.Tests.Integration;
using Parlance.Mcp.Tools;

namespace Parlance.Mcp.Tests.Tools;

public sealed class GetTypeAtToolTests : IAsyncLifetime
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
    public async Task GetTypeAt_PointingAtClassName_ResolvesType()
    {
        var filePath = Path.Combine(TestPaths.RepoRoot,
            "src", "Parlance.CSharp.Workspace", "CSharpWorkspaceSession.cs");
        var lines = await File.ReadAllLinesAsync(filePath);
        var classLine = Array.FindIndex(lines, l => l.Contains("class CSharpWorkspaceSession"));
        Assert.True(classLine >= 0, "Could not find class declaration");

        var classCol = lines[classLine].IndexOf("CSharpWorkspaceSession", StringComparison.Ordinal);
        Assert.True(classCol >= 0, $"Could not find 'CSharpWorkspaceSession' on line: {lines[classLine]}");

        // Tool takes 1-based line/column
        var result = await GetTypeAtTool.GetTypeAt(
            _holder, _query,
            filePath, classLine + 1, classCol + 1, CancellationToken.None);

        Assert.Equal("found", result.Status);
        Assert.Equal("CSharpWorkspaceSession", result.TypeName);
        Assert.Contains("CSharpWorkspaceSession", result.FullyQualifiedName);
        Assert.False(result.IsInferred);
    }

    [Fact]
    public async Task GetTypeAt_PointingAtVarDeclaration_ResolvesInferredType()
    {
        // WorkspaceQueryService.cs has var declarations we can target
        var filePath = Path.Combine(TestPaths.RepoRoot,
            "src", "Parlance.CSharp.Workspace", "WorkspaceQueryService.cs");
        var lines = await File.ReadAllLinesAsync(filePath);

        // Find a line like: var results = new List<ResolvedSymbol>();
        var varLine = Array.FindIndex(lines, l => l.TrimStart().StartsWith("var results = new List", StringComparison.Ordinal));
        Assert.True(varLine >= 0, "Could not find 'var results' declaration");

        var varCol = lines[varLine].IndexOf("var", StringComparison.Ordinal);

        var result = await GetTypeAtTool.GetTypeAt(
            _holder, _query,
            filePath, varLine + 1, varCol + 1, CancellationToken.None);

        Assert.Equal("found", result.Status);
        Assert.True(result.IsInferred);
        Assert.NotNull(result.TypeName);
        Assert.NotNull(result.FullyQualifiedName);
    }

    [Fact]
    public async Task GetTypeAt_UnknownFile_ReturnsNotFound()
    {
        var result = await GetTypeAtTool.GetTypeAt(
            _holder, _query,
            "/nonexistent/file.cs", 1, 1, CancellationToken.None);

        Assert.Equal("not_found", result.Status);
        Assert.NotNull(result.Message);
        Assert.Contains("not found", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetTypeAt_NotLoaded_ReturnsNotLoaded()
    {
        var holder = new WorkspaceSessionHolder();
        var query = new WorkspaceQueryService(holder, NullLogger<WorkspaceQueryService>.Instance);

        var result = GetTypeAtTool.GetTypeAt(
            holder, query,
            "/some/file.cs", 1, 1, CancellationToken.None).Result;

        Assert.Equal("not_loaded", result.Status);
    }

    [Fact]
    public void GetTypeAt_LoadFailed_ReturnsLoadFailed()
    {
        var holder = new WorkspaceSessionHolder();
        holder.SetLoadFailure(new WorkspaceLoadFailure("boom", "/path.sln"));
        var query = new WorkspaceQueryService(holder, NullLogger<WorkspaceQueryService>.Instance);

        var result = GetTypeAtTool.GetTypeAt(
            holder, query,
            "/some/file.cs", 1, 1, CancellationToken.None).Result;

        Assert.Equal("load_failed", result.Status);
        Assert.Equal("boom", result.Message);
    }
}
