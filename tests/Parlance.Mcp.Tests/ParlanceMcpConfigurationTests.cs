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
        var bogusPath = Path.Combine(Path.GetTempPath(), "nonexistent", "path.sln");
        var config = ParlanceMcpConfiguration.FromArgs(["--solution-path", bogusPath]);

        Assert.Equal(Path.GetFullPath(bogusPath), config.SolutionPath);
    }

    [Fact]
    public void FromArgs_InvalidLogLevel_ThrowsDescriptiveError()
    {
        var solutionPath = GetSolutionPath();
        var ex = Assert.Throws<InvalidOperationException>(
            () => ParlanceMcpConfiguration.FromArgs(["--solution-path", solutionPath, "--log-level", "Deubg"]));

        Assert.Contains("Invalid log level", ex.Message);
        Assert.Contains("Deubg", ex.Message);
    }

    [Fact]
    public void FromArgs_ResolvesToAbsolutePath()
    {
        var solutionPath = GetSolutionPath();
        var config = ParlanceMcpConfiguration.FromArgs(["--solution-path", solutionPath]);

        Assert.True(Path.IsPathRooted(config.SolutionPath));
    }

    [Fact]
    public void FromArgs_SolutionPathFlagWithoutValue_ThrowsDescriptiveError()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => ParlanceMcpConfiguration.FromArgs(["--solution-path"]));

        Assert.Contains("--solution-path requires a value", ex.Message);
    }

    [Fact]
    public void FromArgs_LogLevelFlagWithoutValue_ThrowsDescriptiveError()
    {
        var solutionPath = GetSolutionPath();
        var ex = Assert.Throws<InvalidOperationException>(
            () => ParlanceMcpConfiguration.FromArgs(["--solution-path", solutionPath, "--log-level"]));

        Assert.Contains("--log-level requires a value", ex.Message);
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
