using System.Diagnostics;
using System.IO.Compression;

namespace Parlance.Package.Tests;

public sealed class PackageTestFixture : IAsyncLifetime
{
    public string RepoRoot { get; private set; } = null!;
    public string ArtifactsDir { get; private set; } = null!;
    public string TempDir { get; private set; } = null!;
    public string NugetConfigPath { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        // Sweep stale temp dirs left by interrupted prior runs
        foreach (var staleDir in Directory.GetDirectories(Path.GetTempPath(), "parlance-pkg-tests-*"))
        {
            try { Directory.Delete(staleDir, recursive: true); } catch { }
        }

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Parlance.slnx")))
            dir = dir.Parent;

        RepoRoot = dir?.FullName ?? throw new InvalidOperationException("Could not find repo root containing Parlance.slnx");
        ArtifactsDir = Path.Combine(RepoRoot, "artifacts", "test-packages");
        TempDir = Path.Combine(Path.GetTempPath(), $"parlance-pkg-tests-{Guid.NewGuid():N}");

        Directory.CreateDirectory(ArtifactsDir);
        Directory.CreateDirectory(TempDir);

        // NuGet.config is scoped to consumer restores only. The source packs below
        // intentionally omit --configfile so they restore under the ambient
        // ~/.nuget/packages and never write the temp path into src/**/obj.
        //
        // The isolated globalPackagesFolder is still needed on the consumer side:
        // NuGet keys its cache by id+version and won't re-extract the same version,
        // so without isolation a stale ~/.nuget/packages/parlance.csharp.analyzers/0.1.0
        // would silently shadow a freshly-packed nupkg and make diagnostic assertions
        // spuriously pass or fail.
        var packagesFolder = Path.Combine(TempDir, "packages");
        Directory.CreateDirectory(packagesFolder);
        NugetConfigPath = Path.Combine(TempDir, "NuGet.config");
        var nugetConfig = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <config>
                <add key="globalPackagesFolder" value="{packagesFolder}" />
              </config>
              <packageSources>
                <clear />
                <add key="LocalFeed" value="{ArtifactsDir}" />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
            </configuration>
            """;
        await File.WriteAllTextAsync(NugetConfigPath, nugetConfig);

        var analyzersCsproj = Path.Combine(RepoRoot, "src", "Parlance.CSharp.Analyzers", "Parlance.CSharp.Analyzers.csproj");
        var bundleCsproj = Path.Combine(RepoRoot, "src", "Parlance.CSharp.Package", "Parlance.CSharp.Package.csproj");

        // Pack source projects without --configfile: restore runs under the repo's
        // ambient NuGet config so src/**/obj is written with real ~/.nuget/packages
        // paths only, never the temp path that DisposeAsync will delete.
        await RunDotnet($"pack \"{analyzersCsproj}\" -c Release --output \"{ArtifactsDir}\"");
        await RunDotnet($"pack \"{bundleCsproj}\" -c Release --output \"{ArtifactsDir}\"");
    }

    public Task DisposeAsync()
    {
        try
        {
            if (Directory.Exists(TempDir))
                Directory.Delete(TempDir, recursive: true);
        }
        catch
        {
            // Best effort cleanup
        }

        return Task.CompletedTask;
    }

    public async Task<string> RunDotnet(string arguments, bool allowFailure = false, string? restoreFirst = null)
    {
        if (restoreFirst is not null)
        {
            await RunDotnet($"restore \"{restoreFirst}\" --configfile \"{NugetConfigPath}\"");
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

[Trait("Category", "Integration")]
public sealed class AnalyzerPackageIntegrationTests(PackageTestFixture fixture) : IClassFixture<PackageTestFixture>
{
    [Fact]
    public async Task AnalyzerPackage_ReportsParl9003_WhenDefaultExpressionUsed()
    {
        var projectDir = CreateTestProject("AnalyzerTest", "Parlance.CSharp.Analyzers", "0.1.0");
        WriteViolationCode(projectDir);

        var output = await fixture.RunDotnet(
            $"build \"{Path.Combine(projectDir, "Test.csproj")}\" --no-restore",
            allowFailure: true,
            restoreFirst: projectDir);

        Assert.Contains("PARL9003", output);
    }

    [Fact]
    public async Task BundlePackage_RestoresUpstreamDependencies()
    {
        var projectDir = CreateTestProject("BundleTest", "Parlance.CSharp", "0.1.0");
        WriteViolationCode(projectDir);

        var output = await fixture.RunDotnet(
            $"build \"{Path.Combine(projectDir, "Test.csproj")}\" --no-restore",
            allowFailure: true,
            restoreFirst: projectDir);

        Assert.Contains("PARL9003", output);
    }

    [Fact]
    public async Task AnalyzerPackage_ReportsParl3001_WhenCognitiveComplexityExceedsThreshold()
    {
        var projectDir = CreateTestProject("Parl3001AnalyzerTest", "Parlance.CSharp.Analyzers", "0.1.0");
        WriteComplexCode(projectDir);

        var output = await fixture.RunDotnet(
            $"build \"{Path.Combine(projectDir, "Test.csproj")}\" --no-restore",
            allowFailure: true,
            restoreFirst: projectDir);

        Assert.Contains("PARL3001", output);
    }

    [Fact]
    public async Task BundlePackage_ReportsParl3001_WhenCognitiveComplexityExceedsThreshold()
    {
        var projectDir = CreateTestProject("Parl3001BundleTest", "Parlance.CSharp", "0.1.0");
        WriteComplexCode(projectDir);

        var output = await fixture.RunDotnet(
            $"build \"{Path.Combine(projectDir, "Test.csproj")}\" --no-restore",
            allowFailure: true,
            restoreFirst: projectDir);

        Assert.Contains("PARL3001", output);
    }

    [Fact]
    public void AnalyzerPackage_HasNoLibFolder()
    {
        var nupkgPath = Directory.GetFiles(fixture.ArtifactsDir, "Parlance.CSharp.Analyzers.*.nupkg").FirstOrDefault();
        Assert.NotNull(nupkgPath);

        using var zip = ZipFile.OpenRead(nupkgPath);
        var entries = zip.Entries.Select(e => e.FullName).ToList();

        Assert.DoesNotContain(entries, e => e.StartsWith("lib/", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(entries, e => e.StartsWith("analyzers/dotnet/cs/", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BundlePackage_ContainsBuildProps()
    {
        var nupkgPath = Directory.GetFiles(fixture.ArtifactsDir, "Parlance.CSharp.0.*.nupkg")
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
        var projectDir = Path.Combine(fixture.TempDir, name);
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

        // Elevate PARL9003 to warning so it appears in dotnet build output
        // (default severity is Info, which dotnet build suppresses)
        var editorConfig = """
            is_global = true
            dotnet_diagnostic.PARL9003.severity = warning
            """;
        File.WriteAllText(Path.Combine(projectDir, ".editorconfig"), editorConfig);

        return projectDir;
    }

    private static void WriteViolationCode(string projectDir)
    {
        // Code that triggers PARL9003: default(T) instead of default literal
        var code = """
            namespace TestApp;

            public class Example
            {
                public int GetDefault()
                {
                    return default(int);
                }
            }
            """;
        File.WriteAllText(Path.Combine(projectDir, "Example.cs"), code);
    }

    private static void WriteComplexCode(string projectDir)
    {
        // Code that triggers PARL3001: cognitive complexity > 15.
        // 16 sequential `if` statements at nesting 0 produce a score of 16 (one
        // per `if`), which exceeds the default threshold of 15.
        var conditions = string.Join(
            Environment.NewLine,
            Enumerable.Range(1, 16).Select(i => $"        if (n == {i}) {{ }}"));

        var code = $$"""
            namespace TestApp;

            public class Example
            {
                public void Complex(int n)
                {
            {{conditions}}
                }
            }
            """;
        File.WriteAllText(Path.Combine(projectDir, "Complex.cs"), code);
    }
}
