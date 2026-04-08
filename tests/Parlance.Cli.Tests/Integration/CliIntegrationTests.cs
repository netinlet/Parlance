// tests/Parlance.Cli.Tests/Integration/CliIntegrationTests.cs
using System.Diagnostics;
using System.Text.Json;

namespace Parlance.Cli.Tests.Integration;

public sealed class CliIntegrationTests
{
    private readonly string _cliDll;
    private readonly string _solutionPath;

    public CliIntegrationTests()
    {
        var testDir = AppContext.BaseDirectory;
        _cliDll = Path.GetFullPath(Path.Combine(testDir, "..", "..", "..", "..", "..",
            "src", "Parlance.Cli", "bin", "Debug", "net10.0", "Parlance.Cli.dll"));

        if (!File.Exists(_cliDll))
        {
            var cliProject = Path.GetFullPath(Path.Combine(testDir, "..", "..", "..", "..", "..",
                "src", "Parlance.Cli", "Parlance.Cli.csproj"));
            Process.Start(new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"build \"{cliProject}\" --no-restore -v quiet",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            })?.WaitForExit();
        }

        // Walk up from test output to find Parlance.sln
        var dir = testDir;
        while (dir is not null)
        {
            var sln = Directory.GetFiles(dir, "Parlance.sln").FirstOrDefault();
            if (sln is not null) { _solutionPath = sln; return; }
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException("Could not find Parlance.sln");
    }

    private async Task<(int ExitCode, string StdOut, string StdErr)> RunCliAsync(params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        var quotedArgs = string.Join(' ', args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));

        psi.Arguments = File.Exists(_cliDll)
            ? $"exec \"{_cliDll}\" {quotedArgs}"
            : $"run --project \"{Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Parlance.Cli", "Parlance.Cli.csproj"))}\" --no-build -- {quotedArgs}";

        using var process = Process.Start(psi)!;
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, stdout, stderr);
    }

    [Fact]
    public async Task Analyze_Solution_ExitCode0AndProducesOutput()
    {
        var (exitCode, stdout, _) = await RunCliAsync("analyze", _solutionPath);
        Assert.Equal(0, exitCode);
        Assert.Contains("Idiomatic score:", stdout);
    }

    [Fact]
    public async Task Analyze_JsonFormat_ReturnsValidJsonWithExpectedShape()
    {
        var (exitCode, stdout, _) = await RunCliAsync("analyze", _solutionPath, "--format", "json");
        Assert.Equal(0, exitCode);
        var doc = JsonDocument.Parse(stdout);
        Assert.True(doc.RootElement.TryGetProperty("summary", out _));
        Assert.True(doc.RootElement.TryGetProperty("diagnostics", out _));
        Assert.True(doc.RootElement.TryGetProperty("curationSet", out _));
    }

    [Fact]
    public async Task Analyze_FileNotFound_ExitCode2()
    {
        var (exitCode, _, stderr) = await RunCliAsync("analyze", "/nonexistent/path.sln");
        Assert.Equal(2, exitCode);
        Assert.Contains("not found", stderr);
    }

    [Fact]
    public async Task Analyze_WrongExtension_ExitCode2()
    {
        var (exitCode, _, stderr) = await RunCliAsync("analyze", "somefile.cs");
        Assert.Equal(2, exitCode);
        Assert.Contains(".sln or .csproj", stderr);
    }

    [Fact]
    public async Task Analyze_InvalidFormat_ReturnsNonZeroExit()
    {
        var (exitCode, _, stderr) = await RunCliAsync("analyze", _solutionPath, "--format", "nope");
        Assert.NotEqual(0, exitCode);
        Assert.Contains("nope", stderr);
    }

    [Fact]
    public async Task Rules_ShowsUpstreamRules()
    {
        var (exitCode, stdout, _) = await RunCliAsync("rules");
        Assert.Equal(0, exitCode);
        Assert.Matches("(CA|IDE|RCS)", stdout);
    }

    [Fact]
    public async Task Rules_JsonFormat_ReturnsArray()
    {
        var (exitCode, stdout, _) = await RunCliAsync("rules", "--format", "json");
        Assert.Equal(0, exitCode);
        var doc = JsonDocument.Parse(stdout);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.NotEmpty(doc.RootElement.EnumerateArray());
    }

    [Fact]
    public async Task Rules_InvalidFormat_ReturnsNonZeroExit()
    {
        var (exitCode, _, _) = await RunCliAsync("rules", "--format", "nope");
        Assert.NotEqual(0, exitCode);
    }

    [Fact]
    public async Task Rules_UnknownOption_ReturnsNonZeroExit()
    {
        var (exitCode, _, _) = await RunCliAsync("rules", "--bogus");
        Assert.NotEqual(0, exitCode);
    }
}
