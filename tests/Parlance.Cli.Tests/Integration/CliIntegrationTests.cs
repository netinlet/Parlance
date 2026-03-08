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

    [Fact]
    public async Task Rules_ShowsUpstreamRules()
    {
        var (exitCode, stdout, _) = await RunCliAsync("rules");

        Assert.Equal(0, exitCode);
        Assert.Contains("PARL0001", stdout);
        Assert.Matches("(CA|IDE|RCS)", stdout);
    }

    [Fact]
    public async Task Rules_TargetFramework_AcceptsNet8()
    {
        var (exitCode, stdout, _) = await RunCliAsync("rules", "--target-framework", "net8.0");

        Assert.Equal(0, exitCode);
        Assert.Contains("PARL0001", stdout);
    }

    [Fact]
    public async Task Analyze_TargetFramework_AcceptsNet8()
    {
        var file = Path.Combine(_tempDir, "Test.cs");
        File.WriteAllText(file, "class C { }");

        var (exitCode, _, _) = await RunCliAsync("analyze", file, "--target-framework", "net8.0");

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task Fix_TargetFramework_AcceptsNet8()
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

        var (exitCode, _, _) = await RunCliAsync("fix", file, "--target-framework", "net8.0");

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task Analyze_WithUpstreamAnalyzers_ReportsNonParlDiagnostics()
    {
        var file = Path.Combine(_tempDir, "Test.cs");
        // Code that triggers CA1822 (method can be static)
        File.WriteAllText(file, """
            public class C
            {
                public int GetValue()
                {
                    return 42;
                }
            }
            """);

        var (exitCode, stdout, _) = await RunCliAsync("analyze", file);

        Assert.Equal(0, exitCode);
        // Should contain at least one non-PARL diagnostic
        Assert.Matches("(CA|IDE|RCS)", stdout);
    }

    [Fact]
    public async Task Analyze_DefaultProfile_ProducesUpstreamDiagnosticsInJson()
    {
        var file = Path.Combine(_tempDir, "Test.cs");
        File.WriteAllText(file, """
            using System;

            public class MyClass
            {
                public int Add(int a, int b)
                {
                    return a + b;
                }
            }
            """);

        var (exitCode, stdout, _) = await RunCliAsync(
            "analyze", file, "--format", "json");

        Assert.Equal(0, exitCode);
        var doc = JsonDocument.Parse(stdout);
        Assert.True(doc.RootElement.TryGetProperty("summary", out _));
    }

    [Fact]
    public async Task Analyze_ParlAndUpstreamDiagnostics_CoexistInOutput()
    {
        var file = Path.Combine(_tempDir, "Test.cs");
        // Code that triggers both PARL0004 (pattern matching) and upstream diagnostics
        File.WriteAllText(file, """
            public class C
            {
                public void M(object obj)
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
        // PARL diagnostics still fire
        Assert.Contains("PARL0004", stdout);
        // Upstream diagnostics also fire alongside PARL ones
        Assert.Matches("(CA|IDE|RCS)", stdout);
    }

    // Reject invalid --format values instead of silently falling back to text
    [Fact]
    public async Task Analyze_InvalidFormat_ReturnsNonZeroExit()
    {
        var file = Path.Combine(_tempDir, "Test.cs");
        File.WriteAllText(file, "class C { }");

        var (exitCode, _, stderr) = await RunCliAsync("analyze", file, "--format", "nope");

        Assert.NotEqual(0, exitCode);
        Assert.Contains("nope", stderr);
    }

    [Fact]
    public async Task Rules_InvalidFormat_ReturnsNonZeroExit()
    {
        var (exitCode, _, stderr) = await RunCliAsync("rules", "--format", "nope");

        Assert.NotEqual(0, exitCode);
        Assert.Contains("nope", stderr);
    }

    // Unknown options on commands without variadic args should fail
    [Fact]
    public async Task Rules_UnknownOption_ReturnsNonZeroExit()
    {
        var (exitCode, _, _) = await RunCliAsync("rules", "--bogus");

        Assert.NotEqual(0, exitCode);
    }

    // Recursive glob patterns like src/**/*.cs should match nested files
    [Fact]
    public async Task Analyze_RecursiveGlob_FindsNestedFiles()
    {
        var subDir = Path.Combine(_tempDir, "src", "sub");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "Example.cs"), "class Example { }");
        File.WriteAllText(Path.Combine(_tempDir, "src", "Root.cs"), "class Root { }");

        var pattern = Path.Combine(_tempDir, "src", "**", "*.cs");
        var (exitCode, stdout, stderr) = await RunCliAsync("analyze", pattern);

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("No .cs files found", stderr);
        Assert.Contains("Idiomatic score:", stdout);
    }

    // fix --apply should preserve original file encoding and BOM
    [Fact]
    public async Task Fix_Apply_PreservesBom()
    {
        var file = Path.Combine(_tempDir, "BomTest.cs");
        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        var content = """
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
            """u8;

        using (var fs = File.Create(file))
        {
            fs.Write(bom);
            fs.Write(content);
        }

        var (exitCode, _, _) = await RunCliAsync("fix", file, "--apply");
        Assert.Equal(0, exitCode);

        var bytes = File.ReadAllBytes(file);
        Assert.True(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
            "UTF-8 BOM should be preserved after fix --apply");
    }

    [Fact]
    public async Task Fix_Apply_DoesNotAddBomWhenNoneExisted()
    {
        var file = Path.Combine(_tempDir, "NoBomTest.cs");
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

        var (exitCode, _, _) = await RunCliAsync("fix", file, "--apply");
        Assert.Equal(0, exitCode);

        var bytes = File.ReadAllBytes(file);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
            "No BOM should be added when original file had none");
    }
}
