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

        // Walk up from test output to find Parlance.slnx
        var dir = testDir;
        while (dir is not null)
        {
            var sln = Directory.GetFiles(dir, "Parlance.slnx").FirstOrDefault();
            if (sln is not null) { _solutionPath = sln; return; }
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException("Could not find Parlance.slnx");
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
        Assert.Contains(".sln, .slnx, or .csproj", stderr);
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
    public async Task Rules_JsonFormat_IncludesDescriptorFields()
    {
        var (exitCode, stdout, _) = await RunCliAsync("rules", "--format", "json");
        Assert.Equal(0, exitCode);
        var first = JsonDocument.Parse(stdout).RootElement.EnumerateArray().First();
        Assert.True(first.TryGetProperty("severityRaw", out var severityRaw));
        Assert.Contains(severityRaw.GetString(), new[] { "Hidden", "Info", "Warning", "Error" });
        Assert.True(first.TryGetProperty("messageFormat", out _));
        Assert.True(first.TryGetProperty("isEnabledByDefault", out var enabled));
        Assert.Contains(enabled.ValueKind, new[] { JsonValueKind.True, JsonValueKind.False });
        Assert.True(first.TryGetProperty("helpLinkUri", out _));
        Assert.True(first.TryGetProperty("customTags", out var tags));
        Assert.Equal(JsonValueKind.Array, tags.ValueKind);
    }

    [Fact]
    public async Task Rules_AnalyzerPath_EnumeratesOnlyThatAssembly()
    {
        var analyzerDll = Path.Combine(
            Path.GetDirectoryName(_cliDll)!, "analyzer-dlls", "net10.0", "Parlance.CSharp.Analyzers.dll");
        Assert.True(File.Exists(analyzerDll), $"Expected analyzer DLL at {analyzerDll}");

        var (exitCode, stdout, _) = await RunCliAsync("rules", "--analyzer", analyzerDll, "--format", "json");
        Assert.Equal(0, exitCode);
        var ids = JsonDocument.Parse(stdout).RootElement.EnumerateArray()
            .Select(r => r.GetProperty("id").GetString()!).ToList();
        Assert.NotEmpty(ids);
        Assert.All(ids, id => Assert.StartsWith("PARL", id));
    }

    [Fact]
    public async Task Rules_AnalyzerPathNotFound_ReturnsNonZeroExit()
    {
        var (exitCode, _, stderr) = await RunCliAsync("rules", "--analyzer", "/no/such/analyzer.dll");
        Assert.NotEqual(0, exitCode);
        Assert.Contains("not found", stderr);
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

    [Fact]
    public async Task Mcp_MissingSolutionPath_ExitCode2()
    {
        var tempDir = Directory.CreateTempSubdirectory("parlance-cli-mcp-");
        try
        {
            var previous = Environment.CurrentDirectory;
            Environment.CurrentDirectory = tempDir.FullName;
            try
            {
                var (exitCode, _, stderr) = await RunCliAsync("mcp");
                Assert.Equal(2, exitCode);
                Assert.Contains("Solution path is required", stderr);
            }
            finally
            {
                Environment.CurrentDirectory = previous;
            }
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }
}
