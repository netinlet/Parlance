using System.Diagnostics;
using System.IO.Compression;

namespace Parlance.Package.Tests;

public sealed class AnalyzerPackageIntegrationTests : IAsyncLifetime
{
    private string _repoRoot = null!;
    private string _artifactsDir = null!;
    private string _tempDir = null!;
    private string _nugetConfigPath = null!;

    public async Task InitializeAsync()
    {
        // Find repo root by walking up from the base directory
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Parlance.sln")))
        {
            dir = dir.Parent;
        }

        _repoRoot = dir?.FullName ?? throw new InvalidOperationException("Could not find repo root containing Parlance.sln");
        _artifactsDir = Path.Combine(_repoRoot, "artifacts", "test-packages");
        _tempDir = Path.Combine(Path.GetTempPath(), $"parlance-pkg-tests-{Guid.NewGuid():N}");

        Directory.CreateDirectory(_artifactsDir);
        Directory.CreateDirectory(_tempDir);

        // Create a NuGet.config pointing to local feed + nuget.org
        // Must exist before packing the bundle, which needs to restore Parlance.CSharp.Analyzers from the local feed
        _nugetConfigPath = Path.Combine(_tempDir, "NuGet.config");
        var nugetConfig = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="LocalFeed" value="{_artifactsDir}" />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
            </configuration>
            """;
        await File.WriteAllTextAsync(_nugetConfigPath, nugetConfig);

        // Pack both packages into the local feed
        var analyzersCsproj = Path.Combine(_repoRoot, "src", "Parlance.CSharp.Analyzers", "Parlance.CSharp.Analyzers.csproj");
        var bundleCsproj = Path.Combine(_repoRoot, "src", "Parlance.CSharp.Package", "Parlance.CSharp.Package.csproj");

        await RunDotnet($"pack \"{analyzersCsproj}\" -c Release --output \"{_artifactsDir}\"");
        await RunDotnet($"pack \"{bundleCsproj}\" -c Release --output \"{_artifactsDir}\" --configfile \"{_nugetConfigPath}\"");
    }

    public Task DisposeAsync()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task AnalyzerPackage_ReportsParl0004_WhenIsFollowedByCast()
    {
        var projectDir = CreateTestProject("AnalyzerTest", "Parlance.CSharp.Analyzers", "0.1.0");
        WriteViolationCode(projectDir);

        var output = await RunDotnet(
            $"build \"{Path.Combine(projectDir, "Test.csproj")}\" --no-restore",
            allowFailure: true,
            restoreFirst: projectDir);

        Assert.Contains("PARL0004", output);
    }

    [Fact]
    public async Task BundlePackage_RestoresUpstreamDependencies()
    {
        var projectDir = CreateTestProject("BundleTest", "Parlance.CSharp", "0.1.0");
        WriteViolationCode(projectDir);

        var output = await RunDotnet(
            $"build \"{Path.Combine(projectDir, "Test.csproj")}\" --no-restore",
            allowFailure: true,
            restoreFirst: projectDir);

        Assert.Contains("PARL0004", output);
    }

    [Fact]
    public void AnalyzerPackage_HasNoLibFolder()
    {
        var nupkgPath = Directory.GetFiles(_artifactsDir, "Parlance.CSharp.Analyzers.*.nupkg").FirstOrDefault();
        Assert.NotNull(nupkgPath);

        using var zip = ZipFile.OpenRead(nupkgPath);
        var entries = zip.Entries.Select(e => e.FullName).ToList();

        Assert.DoesNotContain(entries, e => e.StartsWith("lib/", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(entries, e => e.StartsWith("analyzers/dotnet/cs/", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BundlePackage_ContainsBuildProps()
    {
        var nupkgPath = Directory.GetFiles(_artifactsDir, "Parlance.CSharp.0.*.nupkg")
            .FirstOrDefault(f => !Path.GetFileName(f).Contains("Analyzers"));
        Assert.NotNull(nupkgPath);

        using var zip = ZipFile.OpenRead(nupkgPath);
        var entries = zip.Entries.Select(e => e.FullName).ToList();

        Assert.Contains(entries, e => e == "build/Parlance.CSharp.props");
        Assert.Contains(entries, e => e == "build/Parlance.CSharp.targets");
        Assert.Contains(entries, e => e == "buildTransitive/Parlance.CSharp.props");
        Assert.Contains(entries, e => e == "buildTransitive/Parlance.CSharp.targets");
        Assert.Contains(entries, e => e.StartsWith("content/", StringComparison.Ordinal) && e.EndsWith(".editorconfig", StringComparison.Ordinal));
    }

    private string CreateTestProject(string name, string packageId, string version)
    {
        var projectDir = Path.Combine(_tempDir, name);
        Directory.CreateDirectory(projectDir);

        var csproj = $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="{packageId}" Version="{version}" />
              </ItemGroup>
            </Project>
            """;
        File.WriteAllText(Path.Combine(projectDir, "Test.csproj"), csproj);

        return projectDir;
    }

    private static void WriteViolationCode(string projectDir)
    {
        // Code that triggers PARL0004: is-followed-by-cast pattern
        var code = """
            namespace TestApp;

            public class Example
            {
                public string Describe(object obj)
                {
                    if (obj is string)
                    {
                        var s = (string)obj;
                        return s;
                    }
                    return string.Empty;
                }
            }
            """;
        File.WriteAllText(Path.Combine(projectDir, "Example.cs"), code);
    }

    private async Task<string> RunDotnet(string arguments, bool allowFailure = false, string? restoreFirst = null)
    {
        if (restoreFirst is not null)
        {
            await RunDotnet($"restore \"{restoreFirst}\" --configfile \"{_nugetConfigPath}\"");
        }

        var psi = new ProcessStartInfo("dotnet", arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi)!;
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        var combined = stdout + Environment.NewLine + stderr;

        if (!allowFailure && process.ExitCode != 0)
            throw new InvalidOperationException($"dotnet {arguments} failed with exit code {process.ExitCode}:\n{combined}");

        return combined;
    }
}
