using Parlance.Mcp;

namespace Parlance.Mcp.Tests;

public sealed class ParlanceMcpConfigurationTests
{
    [Fact]
    public void FromArgs_WithSolutionPath_ParsesCorrectly()
    {
        var solutionPath = GetSolutionPath();
        var config = ParlanceMcpConfiguration.FromArgs(["--solution-path", solutionPath]);

        Assert.Equal(Path.GetFullPath(solutionPath), config.SolutionPath);
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Information, config.MinimumLogLevel);
    }

    [Fact]
    public void FromArgs_WithLogLevel_ParsesCorrectly()
    {
        var solutionPath = GetSolutionPath();
        var config = ParlanceMcpConfiguration.FromArgs(
            ["--solution-path", solutionPath, "--log-level", "Debug"]);

        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Debug, config.MinimumLogLevel);
    }

    [Fact]
    public void FromArgs_MissingSolutionPath_ThrowsDescriptiveError()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => ParlanceMcpConfiguration.FromArgs([]));

        Assert.Contains("--solution-path", ex.Message);
        Assert.Contains("PARLANCE_SOLUTION_PATH", ex.Message);
    }

    [Fact]
    public void FromArgs_NonexistentFile_AcceptsPath()
    {
        var config = ParlanceMcpConfiguration.FromArgs(["--solution-path", "/nonexistent/path.sln"]);

        Assert.Equal("/nonexistent/path.sln", config.SolutionPath);
    }

    [Fact]
    public void FromArgs_ResolvesToAbsolutePath()
    {
        var solutionPath = GetSolutionPath();
        var config = ParlanceMcpConfiguration.FromArgs(["--solution-path", solutionPath]);

        Assert.True(Path.IsPathRooted(config.SolutionPath));
    }

    private static string GetSolutionPath()
    {
        var dir = Directory.GetCurrentDirectory();
        while (dir is not null)
        {
            var slnPath = Path.Combine(dir, "Parlance.sln");
            if (File.Exists(slnPath))
                return slnPath;
            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException("Cannot find Parlance.sln in parent directories");
    }
}
