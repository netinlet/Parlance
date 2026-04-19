using Parlance.CSharp.Workspace;
using Parlance.CSharp.Workspace.Tests.Integration;

namespace Parlance.Analysis.Tests;

public sealed class AnalysisFileResolverTests : IAsyncLifetime
{
    private CSharpWorkspaceSession _session = null!;

    public async Task InitializeAsync() =>
        _session = await CSharpWorkspaceSession.OpenSolutionAsync(TestPaths.FindSolutionPath());

    public async Task DisposeAsync() => await _session.DisposeAsync();

    [Fact]
    public void ResolveTargets_RelativeFile_ResolvesFromBaseDirectory()
    {
        var repoRoot = Path.GetDirectoryName(TestPaths.FindSolutionPath())!;

        var files = AnalysisFileResolver.ResolveTargets(
            _session,
            ["src/Parlance.Mcp/Program.cs"],
            repoRoot);

        Assert.Equal(Path.Combine(repoRoot, "src", "Parlance.Mcp", "Program.cs"), Assert.Single(files));
    }

    [Fact]
    public void ResolveTargets_SolutionPath_ExpandsLoadedSolutionFiles()
    {
        var files = AnalysisFileResolver.ResolveTargets(_session, [TestPaths.FindSolutionPath()]);

        Assert.Contains(files, f => f.EndsWith(
            Path.Combine("src", "Parlance.Mcp", "Program.cs"), StringComparison.Ordinal));
    }

    [Fact]
    public void ResolveTargets_ProjectPath_ExpandsLoadedProjectFiles()
    {
        var repoRoot = Path.GetDirectoryName(TestPaths.FindSolutionPath())!;
        var projectPath = Path.Combine(repoRoot, "src", "Parlance.Mcp", "Parlance.Mcp.csproj");

        var files = AnalysisFileResolver.ResolveTargets(_session, [projectPath]);

        Assert.Contains(files, f => f.EndsWith(
            Path.Combine("src", "Parlance.Mcp", "Program.cs"), StringComparison.Ordinal));
        Assert.DoesNotContain(files, f => f.EndsWith(
            Path.Combine("src", "Parlance.Cli", "Program.cs"), StringComparison.Ordinal));
    }

    [Fact]
    public void ResolveTargets_UnloadedSolutionPath_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            AnalysisFileResolver.ResolveTargets(_session, [Path.Combine(Path.GetTempPath(), "Other.sln")]));

        Assert.Contains("not the loaded workspace", ex.Message);
    }

    [Fact]
    public void ResolveTargets_UnloadedProjectPath_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            AnalysisFileResolver.ResolveTargets(_session, [Path.Combine(Path.GetTempPath(), "Other.csproj")]));

        Assert.Contains("not loaded", ex.Message);
    }
}
