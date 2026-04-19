using Parlance.CSharp.Workspace;
using Parlance.CSharp.Workspace.Tests.Integration;
using Parlance.Mcp.Tools;

namespace Parlance.Mcp.Tests.Tools;

public sealed class ListProjectFilesToolTests : IAsyncLifetime
{
    private WorkspaceSessionHolder _holder = null!;
    private CSharpWorkspaceSession _session = null!;

    public async Task InitializeAsync()
    {
        _session = await CSharpWorkspaceSession.OpenSolutionAsync(TestPaths.FindSolutionPath());
        _holder = new WorkspaceSessionHolder();
        _holder.SetSession(_session);
    }

    public async Task DisposeAsync() => await _session.DisposeAsync();

    [Fact]
    public void NotLoaded_ReturnsNotLoaded()
    {
        var result = ListProjectFilesTool.ListProjectFiles(
            new WorkspaceSessionHolder(), null, null, "relative", null);

        Assert.Equal("not_loaded", result.Status);
        Assert.Equal("Workspace is still loading", result.Error);
        Assert.Empty(result.Files);
    }

    [Fact]
    public void LoadFailed_ReturnsLoadFailed()
    {
        var holder = new WorkspaceSessionHolder();
        holder.SetLoadFailure(new WorkspaceLoadFailure("boom", "/path.sln"));

        var result = ListProjectFilesTool.ListProjectFiles(
            holder, null, null, "relative", null);

        Assert.Equal("load_failed", result.Status);
        Assert.Equal("boom", result.Error);
        Assert.Empty(result.Files);
    }

    [Fact]
    public void ListProjectFiles_PathPattern_ReturnsCompactRelativeFiles()
    {
        var result = ListProjectFilesTool.ListProjectFiles(
            _holder, null, "src/Parlance.Mcp/Tools/*Tool.cs", "relative", null);

        Assert.Equal("success", result.Status);
        Assert.Equal(_session.SnapshotVersion, result.SnapshotVersion);
        Assert.Equal("relative", result.PathStyle);
        Assert.Equal("src/Parlance.Mcp/Tools/*Tool.cs", result.PathPattern);
        Assert.False(result.Truncated);
        Assert.Equal(result.TotalMatched, result.Returned);
        Assert.Contains("src/Parlance.Mcp/Tools/AnalyzeTool.cs", result.Files);
        Assert.DoesNotContain(result.Files, f => f.Contains('\\'));
        Assert.Equal(result.Files.Order(StringComparer.OrdinalIgnoreCase), result.Files);
    }

    [Fact]
    public void ListProjectFiles_ProjectNameAndMaxFiles_LimitsDeterministically()
    {
        var result = ListProjectFilesTool.ListProjectFiles(
            _holder, "Parlance.Mcp", "src/Parlance.Mcp/**/*.cs", "relative", 2);

        Assert.Equal("success", result.Status);
        Assert.True(result.TotalMatched > 2);
        Assert.Equal(2, result.Returned);
        Assert.True(result.Truncated);
        Assert.Equal(2, result.Files.Count);
        Assert.All(result.Files, file => Assert.StartsWith("src/Parlance.Mcp/", file));
        Assert.Equal(result.Files.Order(StringComparer.OrdinalIgnoreCase), result.Files);
    }

    [Fact]
    public void ListProjectFiles_AbsolutePathStyle_ReturnsAbsolutePaths()
    {
        var result = ListProjectFilesTool.ListProjectFiles(
            _holder, "Parlance.Mcp", "src/Parlance.Mcp/Program.cs", "absolute", null);

        Assert.Equal("success", result.Status);
        var file = Assert.Single(result.Files);
        Assert.True(Path.IsPathRooted(file));
        Assert.EndsWith("src/Parlance.Mcp/Program.cs", file.Replace('\\', '/'));
    }
}
