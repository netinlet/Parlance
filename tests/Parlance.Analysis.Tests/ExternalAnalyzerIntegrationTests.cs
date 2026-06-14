using System.Collections.Immutable;
using Microsoft.Extensions.Logging.Abstractions;
using Parlance.Analysis.Curation;
using Parlance.Analyzers.Upstream;
using Parlance.CSharp.Workspace;

namespace Parlance.Analysis.Tests;

/// <summary>
/// End-to-end integration tests for the external analyzer trust gate.
/// Each test creates a minimal temp project so the workspace is isolated and disposable.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ExternalAnalyzerIntegrationTests
{
    private static string TestAnalyzerDll =>
        Path.Combine(AppContext.BaseDirectory, "test-analyzer-dlls", "Parlance.TestAnalyzer.dll");

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Creates a temp directory containing a minimal SDK project and a C# source file
    /// that will trigger PARLTEST01 (a local variable whose name starts with "testTrigger").
    /// </summary>
    private static async Task<(string TempDir, string CsprojPath, string SourceFilePath)> CreateTempProjectAsync()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"parlance-ext-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var csproj = Path.Combine(tempDir, "TestProject.csproj");
        await File.WriteAllTextAsync(csproj, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """);

        var sourceFile = Path.Combine(tempDir, "Trigger.cs");
        await File.WriteAllTextAsync(sourceFile, """
            using System;
            class C { void M() { var testTriggerFoo = 1; } }
            """);

        return (tempDir, csproj, sourceFile);
    }

    private static string LocalAnalyzerDir(string tempDir) =>
        Path.Combine(tempDir, ".parlance", "analyzers", "local");

    /// <summary>
    /// Copies TestAnalyzer.dll into the temp repo's local analyzer dir and returns the dest path.
    /// </summary>
    private static string CopyTestAnalyzerDll(string tempDir)
    {
        var localDir = LocalAnalyzerDir(tempDir);
        Directory.CreateDirectory(localDir);
        var dest = Path.Combine(localDir, "Parlance.TestAnalyzer.dll");
        File.Copy(TestAnalyzerDll, dest);
        return dest;
    }

    private static AnalysisService BuildService(WorkspaceSessionHolder holder, AnalyzerProvider provider) =>
        new(holder,
            new WorkspaceQueryService(holder, NullLogger<WorkspaceQueryService>.Instance),
            new CurationSetProvider(NullLogger<CurationSetProvider>.Instance),
            provider,
            NullLogger<AnalysisService>.Instance);

    // ---------------------------------------------------------------------------
    // Tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ExternalAnalyzer_TrustedDll_SurfacesDiagnostic()
    {
        Assert.True(File.Exists(TestAnalyzerDll),
            $"TestAnalyzer DLL not found at: {TestAnalyzerDll}");

        var (tempDir, csproj, sourceFile) = await CreateTempProjectAsync();
        try
        {
            // Copy DLL and trust it
            var dllDest = CopyTestAnalyzerDll(tempDir);
            var trustFile = new AnalyzerTrustFile(AnalyzerTrustFile.ProjectPath(tempDir));
            trustFile.Trust(dllDest);

            await using var session = Assert.IsType<WorkspaceLoadResult.Success>(
                await CSharpWorkspaceSession.TryOpenProjectAsync(csproj)).Session;

            var holder = new WorkspaceSessionHolder();
            holder.SetSession(session);

            var provider = new AnalyzerProvider([new LocalDirectoryAnalyzerSource()]);
            var service = BuildService(holder, provider);

            var result = await service.AnalyzeFilesAsync([sourceFile]);

            Assert.Contains(result.Diagnostics, d => d.RuleId == "PARLTEST01");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task ExternalAnalyzer_UntrustedDll_ProducesLoadFailureNotDiagnostic()
    {
        Assert.True(File.Exists(TestAnalyzerDll),
            $"TestAnalyzer DLL not found at: {TestAnalyzerDll}");

        var (tempDir, csproj, sourceFile) = await CreateTempProjectAsync();
        try
        {
            // Copy DLL but do NOT trust it
            CopyTestAnalyzerDll(tempDir);

            await using var session = Assert.IsType<WorkspaceLoadResult.Success>(
                await CSharpWorkspaceSession.TryOpenProjectAsync(csproj)).Session;

            var holder = new WorkspaceSessionHolder();
            holder.SetSession(session);

            var provider = new AnalyzerProvider([new LocalDirectoryAnalyzerSource()]);
            var service = BuildService(holder, provider);

            var result = await service.AnalyzeFilesAsync([sourceFile]);

            // No PARLTEST01 diagnostic — untrusted DLL must not run
            Assert.DoesNotContain(result.Diagnostics, d => d.RuleId == "PARLTEST01");

            // Load failure surfaced by the provider
            var providerResult = provider.GetComponents("net10.0", Path.GetFullPath(tempDir));
            Assert.Contains(providerResult.Failures,
                f => f.DllPath.Contains("Parlance.TestAnalyzer.dll") && f.Reason.Contains("Not trusted"));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task ExternalAnalyzer_HashMismatch_ProducesLoadFailure()
    {
        Assert.True(File.Exists(TestAnalyzerDll),
            $"TestAnalyzer DLL not found at: {TestAnalyzerDll}");

        var (tempDir, csproj, sourceFile) = await CreateTempProjectAsync();
        try
        {
            // Copy DLL and trust it, then mutate the DLL so the hash no longer matches
            var dllDest = CopyTestAnalyzerDll(tempDir);
            var trustFile = new AnalyzerTrustFile(AnalyzerTrustFile.ProjectPath(tempDir));
            trustFile.Trust(dllDest);

            // Flip the last byte — content changes but file still exists
            var bytes = File.ReadAllBytes(dllDest);
            bytes[^1] ^= 0xFF;
            File.WriteAllBytes(dllDest, bytes);

            await using var session = Assert.IsType<WorkspaceLoadResult.Success>(
                await CSharpWorkspaceSession.TryOpenProjectAsync(csproj)).Session;

            var holder = new WorkspaceSessionHolder();
            holder.SetSession(session);

            var provider = new AnalyzerProvider([new LocalDirectoryAnalyzerSource()]);
            var service = BuildService(holder, provider);

            var result = await service.AnalyzeFilesAsync([sourceFile]);

            // No PARLTEST01 — mutated DLL should be rejected
            Assert.DoesNotContain(result.Diagnostics, d => d.RuleId == "PARLTEST01");

            // Load failure with checksum mismatch
            var providerResult = provider.GetComponents("net10.0", Path.GetFullPath(tempDir));
            Assert.Contains(providerResult.Failures,
                f => f.Reason.Contains("Checksum mismatch"));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
