using System.Diagnostics;
using System.Text.Json;

namespace Parlance.Cli.Tests.Integration;

public sealed class CliIntegrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _cliDll;

    public CliIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"parlance-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        // Find the CLI project's built DLL relative to test output
        var testDir = AppContext.BaseDirectory;
        _cliDll = Path.GetFullPath(Path.Combine(testDir, "..", "..", "..", "..", "..",
            "src", "Parlance.Cli", "bin", "Debug", "net10.0", "Parlance.Cli.dll"));

        // If DLL doesn't exist at the relative path, try building it
        if (!File.Exists(_cliDll))
        {
            var cliProject = Path.GetFullPath(Path.Combine(testDir, "..", "..", "..", "..", "..",
                "src", "Parlance.Cli", "Parlance.Cli.csproj"));
            var buildProcess = Process.Start(new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"build \"{cliProject}\" --no-restore -v quiet",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            buildProcess?.WaitForExit();
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private async Task<(int ExitCode, string StdOut, string StdErr)> RunCliAsync(params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        // Use dotnet exec for the built DLL - much faster than dotnet run
        if (File.Exists(_cliDll))
        {
            psi.Arguments = $"exec \"{_cliDll}\" {string.Join(' ', args)}";
        }
        else
        {
            // Fallback to dotnet run if DLL not found
            var cliProject = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                "src", "Parlance.Cli", "Parlance.Cli.csproj"));
            psi.Arguments = $"run --project \"{cliProject}\" --no-build -- {string.Join(' ', args)}";
        }

        using var process = Process.Start(psi)!;
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (process.ExitCode, stdout, stderr);
    }

    [Fact]
    public async Task Analyze_SingleFile_ShowsDiagnostics()
    {
        var file = Path.Combine(_tempDir, "Test.cs");
        File.WriteAllText(file, """
            class C
            {
                void M(object obj)
                {
                    if (obj is string)
                    {
                        var s = (string)obj;
                    }
                }
            }
            """);

        var (exitCode, stdout, _) = await RunCliAsync("analyze", file);

        Assert.Equal(0, exitCode);
        Assert.Contains("PARL0004", stdout);
        Assert.Contains("Idiomatic score:", stdout);
    }

    [Fact]
    public async Task Analyze_JsonFormat_ReturnsValidJson()
    {
        var file = Path.Combine(_tempDir, "Test.cs");
        File.WriteAllText(file, "class C { void M() { } }");

        var (exitCode, stdout, _) = await RunCliAsync("analyze", file, "--format", "json");

        Assert.Equal(0, exitCode);
        var doc = JsonDocument.Parse(stdout);
        Assert.True(doc.RootElement.TryGetProperty("summary", out _));
    }

    [Fact]
    public async Task Analyze_FailBelow_ExitCode1()
    {
        var file = Path.Combine(_tempDir, "Test.cs");
        File.WriteAllText(file, """
            class C
            {
                void M(object obj)
                {
                    if (obj is string) { var s = (string)obj; }
                }
            }
            """);

        var (exitCode, _, _) = await RunCliAsync("analyze", file, "--fail-below", "100");

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task Analyze_NoFiles_ExitCode2()
    {
        var (exitCode, _, stderr) = await RunCliAsync("analyze", Path.Combine(_tempDir, "nonexistent"));

        Assert.Equal(2, exitCode);
        Assert.Contains("No .cs files found", stderr);
    }

    [Fact]
    public async Task Fix_DryRun_DoesNotModify()
    {
        var file = Path.Combine(_tempDir, "Test.cs");
        var original = """
            using System;
            using System.IO;
            class C
            {
                void M()
                {
                    using (var stream = new MemoryStream())
                    {
                        stream.WriteByte(1);
                    }
                }
            }
            """;
        File.WriteAllText(file, original);

        var (exitCode, stdout, _) = await RunCliAsync("fix", file);

        Assert.Equal(0, exitCode);
        Assert.Contains("would be modified", stdout);
        Assert.Equal(original, File.ReadAllText(file));
    }

    [Fact]
    public async Task Fix_Apply_ModifiesFile()
    {
        var file = Path.Combine(_tempDir, "Test.cs");
        File.WriteAllText(file, """
            using System;
            using System.IO;
            class C
            {
                void M()
                {
                    using (var stream = new MemoryStream())
                    {
                        stream.WriteByte(1);
                    }
                }
            }
            """);

        var (exitCode, stdout, _) = await RunCliAsync("fix", file, "--apply");

        Assert.Equal(0, exitCode);
        Assert.Contains("Applied fixes", stdout);

        var modified = File.ReadAllText(file);
        Assert.Contains("using var stream", modified);
    }

    [Fact]
    public async Task Rules_ShowsAllRules()
    {
        var (exitCode, stdout, _) = await RunCliAsync("rules");

        Assert.Equal(0, exitCode);
        Assert.Contains("PARL0001", stdout);
        Assert.Contains("PARL0004", stdout);
        Assert.Contains("PARL9001", stdout);
    }

    [Fact]
    public async Task Rules_Fixable_ShowsOnlyFixableRules()
    {
        var (exitCode, stdout, _) = await RunCliAsync("rules", "--fixable");

        Assert.Equal(0, exitCode);
        Assert.Contains("PARL0004", stdout);
        Assert.Contains("PARL9001", stdout);
        Assert.DoesNotContain("PARL0001", stdout);
    }
}
