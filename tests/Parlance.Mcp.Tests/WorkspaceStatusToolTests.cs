using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Parlance.Analysis;
using Parlance.Analyzers.Upstream;
using Parlance.CSharp.Workspace;
using Parlance.Mcp.Tools;

namespace Parlance.Mcp.Tests;

public sealed class WorkspaceStatusToolTests
{
    private static readonly ParlanceMcpConfiguration DefaultConfig =
        new("/path/to/Solution.sln", "/path/to/.parlance/logs", LogLevel.Information);

    private static AnalyzerProvider EmptyProvider => new([]);

    private static string BundledAnalyzerDll =>
        Path.Combine(AppContext.BaseDirectory, "analyzer-dlls", "net10.0", "Parlance.CSharp.Analyzers.dll");

    [Fact]
    public void GetStatus_LoadFailure_ReturnsFailedStatus()
    {
        var holder = new WorkspaceSessionHolder();
        holder.SetLoadFailure(new WorkspaceLoadFailure("Something went wrong", "/path/to/Solution.sln"));
        var logger = NullLogger<WorkspaceStatusTool>.Instance;

        var result = WorkspaceStatusTool.GetStatus(holder, DefaultConfig, EmptyProvider, logger);

        Assert.Equal("Failed", result.Status);
        Assert.Equal("/path/to/Solution.sln", result.SolutionPath.Absolute);
        Assert.Equal(0, result.SnapshotVersion);
        Assert.Single(result.Diagnostics);
        Assert.Equal("LoadFailure", result.Diagnostics[0].Code);
        Assert.Contains("Something went wrong", result.Diagnostics[0].Message);
    }

    [Fact]
    public void GetStatus_NotYetLoaded_ReturnsLoadingStatus()
    {
        var holder = new WorkspaceSessionHolder();
        var logger = NullLogger<WorkspaceStatusTool>.Instance;

        var result = WorkspaceStatusTool.GetStatus(holder, DefaultConfig, EmptyProvider, logger);

        Assert.Equal("Loading", result.Status);
        Assert.Equal("/path/to/Solution.sln", result.SolutionPath.Absolute);
        Assert.Equal(0, result.SnapshotVersion);
        Assert.Empty(result.Projects);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void GetStatus_LoadFailure_TakesPrecedenceOverNotLoaded()
    {
        var holder = new WorkspaceSessionHolder();
        holder.SetLoadFailure(new WorkspaceLoadFailure("Failed", "/path.sln"));
        var logger = NullLogger<WorkspaceStatusTool>.Instance;

        var result = WorkspaceStatusTool.GetStatus(holder, DefaultConfig, EmptyProvider, logger);

        Assert.Equal("Failed", result.Status);
    }

    [Fact]
    public void GetStatus_ExposesAbsoluteWorkspaceRoot_AnchoringTheRelativeSolutionPath()
    {
        var holder = new WorkspaceSessionHolder();
        var result = WorkspaceStatusTool.GetStatus(holder, DefaultConfig, EmptyProvider, NullLogger<WorkspaceStatusTool>.Instance);

        // SolutionPath serializes workspace-relative like every other path; WorkspaceRoot is the
        // absolute anchor a client needs to recover the repo location and resolve those relative
        // paths from a different cwd. It must be absolute and own the solution file.
        Assert.True(Path.IsPathRooted(result.WorkspaceRoot));
        Assert.StartsWith(result.WorkspaceRoot, result.SolutionPath.Absolute);
    }

    [Fact]
    public void ProjectStatusEntry_HasNoScalarTargetFramework() =>
        Assert.Null(typeof(ProjectStatusEntry).GetProperty("TargetFramework"));

    [Fact]
    public void GetStatus_WithUntrustedDll_IncludesTrustNoticeInResult()
    {
        Assert.True(File.Exists(BundledAnalyzerDll));

        var repo = Directory.CreateTempSubdirectory("parlance-trust-notice-").FullName;
        try
        {
            var localDir = Path.Combine(repo, ".parlance", "analyzers", "local");
            Directory.CreateDirectory(localDir);
            File.Copy(BundledAnalyzerDll, Path.Combine(localDir, Path.GetFileName(BundledAnalyzerDll)));

            // A fake solution file inside the repo so RepoPath.Containing resolves to the repo root
            var fakeSln = Path.Combine(repo, "Fake.sln");
            File.WriteAllText(fakeSln, "");

            var config = new ParlanceMcpConfiguration(fakeSln, Path.Combine(repo, ".parlance", "logs"));
            var provider = new AnalyzerProvider([new LocalDirectoryAnalyzerSource()]);
            var holder = new WorkspaceSessionHolder();

            var result = WorkspaceStatusTool.GetStatus(
                holder, config, provider, NullLogger<WorkspaceStatusTool>.Instance);

            Assert.Contains(result.Notices, n => n.StartsWith("Not trusted"));
        }
        finally { Directory.Delete(repo, recursive: true); }
    }
}
