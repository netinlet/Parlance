using Parlance.CSharp.Workspace;

namespace Parlance.CSharp.Workspace.Tests.Integration;

public sealed class RefreshTests
{
    [Fact]
    public async Task RefreshAsync_ReportMode_ThrowsInvalidOperation()
    {
        var projectPath = Path.Combine(
            TestPaths.RepoRoot, "src", "Parlance.Abstractions", "Parlance.Abstractions.csproj");

        await using var session = await CSharpWorkspaceSession.OpenProjectAsync(projectPath);

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.RefreshAsync());
    }

    [Fact]
    public async Task RefreshAsync_ServerMode_NoChanges_VersionUnchanged()
    {
        var projectPath = Path.Combine(
            TestPaths.RepoRoot, "src", "Parlance.Abstractions", "Parlance.Abstractions.csproj");
        var options = new WorkspaceOpenOptions(
            Mode: WorkspaceMode.Server,
            EnableFileWatching: false);

        await using var session = await CSharpWorkspaceSession.OpenProjectAsync(projectPath, options);

        Assert.Equal(1, session.SnapshotVersion);
        await session.RefreshAsync();
        Assert.Equal(1, session.SnapshotVersion);
    }

    [Fact]
    public async Task RefreshAsync_DetectsSourceTextChange()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"parlance-refresh-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var csproj = Path.Combine(tempDir, "TestProject.csproj");
            await File.WriteAllTextAsync(csproj, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);

            var sourceFile = Path.Combine(tempDir, "Class1.cs");
            await File.WriteAllTextAsync(sourceFile, "namespace Test; public class Class1 { }");

            var options = new WorkspaceOpenOptions(
                Mode: WorkspaceMode.Server,
                EnableFileWatching: false);

            await using var session = await CSharpWorkspaceSession.OpenProjectAsync(csproj, options);
            Assert.Equal(1, session.SnapshotVersion);

            // Modify source on disk
            await File.WriteAllTextAsync(sourceFile,
                "namespace Test; public class Class1 { public int X { get; } }");

            await session.RefreshAsync();

            Assert.True(session.SnapshotVersion > 1,
                "SnapshotVersion should increment after detecting source text change");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task RefreshAsync_StructuralChange_NotDetected()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"parlance-refresh-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var csproj = Path.Combine(tempDir, "TestProject.csproj");
            await File.WriteAllTextAsync(csproj, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);

            var sourceFile = Path.Combine(tempDir, "Class1.cs");
            await File.WriteAllTextAsync(sourceFile, "namespace Test; public class Class1 { }");

            var options = new WorkspaceOpenOptions(
                Mode: WorkspaceMode.Server,
                EnableFileWatching: false);

            await using var session = await CSharpWorkspaceSession.OpenProjectAsync(csproj, options);

            // Add a new .cs file (structural change — NOT detected by RefreshAsync)
            var newFile = Path.Combine(tempDir, "Class2.cs");
            await File.WriteAllTextAsync(newFile, "namespace Test; public class Class2 { }");

            await session.RefreshAsync();

            // Version should NOT change — RefreshAsync only detects source text changes
            // to already-loaded documents, not new files
            Assert.Equal(1, session.SnapshotVersion);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
