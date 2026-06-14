using Microsoft.Extensions.Logging.Abstractions;
using Parlance.Analysis.Curation;
using Parlance.Analyzers.Upstream;
using Parlance.CSharp.Workspace;

namespace Parlance.Analysis.Tests;

/// <summary>
/// Integration tests verifying that external analyzer DLLs wire correctly into the
/// <see cref="CodeActionService"/> pipeline — specifically that:
/// <list type="bullet">
///   <item>A trusted external analyzer's diagnostics flow into <c>GetCodeFixesAsync</c> without crashing,
///         even when the external DLL ships no fix provider for that rule.</item>
///   <item>Refactoring providers sourced from multiple <see cref="IAnalyzerSource"/>s (including
///         <see cref="LocalDirectoryAnalyzerSource"/>) merge correctly and produce results.</item>
/// </list>
/// Each test creates an isolated temp project so workspace state is never shared.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ExternalCodeActionIntegrationTests
{
    private static string TestAnalyzerDll =>
        Path.Combine(AppContext.BaseDirectory, "test-analyzer-dlls", "Parlance.TestAnalyzer.dll");

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static async Task<(string TempDir, string CsprojPath, string SourceFilePath)> CreateTempProjectAsync()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"parlance-ca-{Guid.NewGuid():N}");
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

        // Source triggers PARLTEST01 (local variable starting with "testTrigger").
        // Two occurrences give code-fix context a multi-diagnostic document to work over.
        var sourceFile = Path.Combine(tempDir, "Trigger.cs");
        await File.WriteAllTextAsync(sourceFile, """
            class C
            {
                void M()
                {
                    var testTriggerAlpha = 1;
                    var testTriggerBeta  = 2;
                }
            }
            """);

        return (tempDir, csproj, sourceFile);
    }

    private static string CopyTestAnalyzerDll(string tempDir)
    {
        var localDir = Path.Combine(tempDir, ".parlance", "analyzers", "local");
        Directory.CreateDirectory(localDir);
        var dest = Path.Combine(localDir, "Parlance.TestAnalyzer.dll");
        File.Copy(TestAnalyzerDll, dest);
        return dest;
    }

    private static CodeActionService BuildCodeActionService(WorkspaceSessionHolder holder, AnalyzerProvider provider) =>
        new(holder, provider, NullLogger<CodeActionService>.Instance);

    // ---------------------------------------------------------------------------
    // Tests
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Verifies that a PARLTEST01 diagnostic produced by a trusted external DLL can flow
    /// through <see cref="CodeActionService.GetCodeFixesAsync"/> without an exception.
    /// The external DLL has no fix provider for PARLTEST01, so the result is an empty list —
    /// but the pipeline must not crash on encountering an unfixable external diagnostic.
    /// </summary>
    [Fact]
    public async Task GetCodeFixes_ExternalAnalyzerDiagnostic_PipelineHandlesNoExternalFix()
    {
        Assert.True(File.Exists(TestAnalyzerDll),
            $"TestAnalyzer DLL not found at: {TestAnalyzerDll}");

        var (tempDir, csproj, sourceFile) = await CreateTempProjectAsync();
        try
        {
            var dllDest = CopyTestAnalyzerDll(tempDir);
            var trustFile = new AnalyzerTrustFile(AnalyzerTrustFile.ProjectPath(tempDir));
            trustFile.Trust(dllDest);

            await using var session = Assert.IsType<WorkspaceLoadResult.Success>(
                await CSharpWorkspaceSession.TryOpenProjectAsync(csproj)).Session;

            var holder = new WorkspaceSessionHolder();
            holder.SetSession(session);

            // Include both the external source and RoslynFeatures (which supplies fix providers for
            // built-in rules). The external DLL contributes only an analyzer — no fix provider.
            var provider = new AnalyzerProvider([
                new LocalDirectoryAnalyzerSource(),
                new RoslynFeaturesAnalyzerSource()
            ]);

            var service = BuildCodeActionService(holder, provider);

            // Line 5 contains the first "testTriggerAlpha" — PARLTEST01 fires here.
            var fixes = await service.GetCodeFixesAsync(sourceFile, 5, "PARLTEST01", CancellationToken.None);

            // No fix provider covers PARLTEST01 (external DLL has none), so the list is empty.
            // The important invariant is that calling GetCodeFixesAsync does not throw.
            Assert.NotNull(fixes);
            Assert.Empty(fixes); // no fix provider covers PARLTEST01 in the external DLL
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that when <see cref="LocalDirectoryAnalyzerSource"/> is composed with
    /// <see cref="RoslynFeaturesAnalyzerSource"/>, the merged refactoring-provider set is
    /// non-empty and <see cref="CodeActionService.GetRefactoringsAsync"/> returns results
    /// from Roslyn's built-in providers. This confirms the multi-source merge path is wired
    /// end-to-end through <see cref="AnalyzerProvider"/> into the code-action service.
    /// </summary>
    [Fact]
    public async Task GetRefactorings_MergedExternalAndRoslynFeaturesSources_ReturnRefactorings()
    {
        Assert.True(File.Exists(TestAnalyzerDll),
            $"TestAnalyzer DLL not found at: {TestAnalyzerDll}");

        var (tempDir, csproj, sourceFile) = await CreateTempProjectAsync();
        try
        {
            // Trust the external DLL so LocalDirectoryAnalyzerSource loads it — its refactoring
            // providers (none in this DLL) will merge with those from RoslynFeaturesAnalyzerSource.
            var dllDest = CopyTestAnalyzerDll(tempDir);
            var trustFile = new AnalyzerTrustFile(AnalyzerTrustFile.ProjectPath(tempDir));
            trustFile.Trust(dllDest);

            await using var session = Assert.IsType<WorkspaceLoadResult.Success>(
                await CSharpWorkspaceSession.TryOpenProjectAsync(csproj)).Session;

            var holder = new WorkspaceSessionHolder();
            holder.SetSession(session);

            var provider = new AnalyzerProvider([
                new LocalDirectoryAnalyzerSource(),
                new RoslynFeaturesAnalyzerSource()
            ]);

            var service = BuildCodeActionService(holder, provider);

            // Position the cursor on the class name "C" (line 1, col 7) — a reliable refactoring
            // anchor for Roslyn built-ins (rename, generate constructor, etc.).
            var refactorings = await service.GetRefactoringsAsync(sourceFile, 1, 7, ct: CancellationToken.None);

            // RoslynFeaturesAnalyzerSource contributes refactoring providers, so at least one
            // refactoring must be available regardless of what the external DLL contributes.
            Assert.NotEmpty(refactorings);
            Assert.All(refactorings, r => Assert.False(string.IsNullOrWhiteSpace(r.Title)));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
