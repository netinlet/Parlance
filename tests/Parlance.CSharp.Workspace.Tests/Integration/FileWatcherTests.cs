using Microsoft.Extensions.Logging.Abstractions;
using Parlance.CSharp.Workspace;
using Parlance.CSharp.Workspace.Internal;

namespace Parlance.CSharp.Workspace.Tests.Integration;

public sealed class FileWatcherTests
{
    [Fact]
    public async Task DetectsTrackedFileChange()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"parlance-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "Test.cs");
            await File.WriteAllTextAsync(filePath, "// original");

            var tcs = new TaskCompletionSource<IReadOnlyList<string>>();
            await using var watcher = new WorkspaceFileWatcher(
                [dir],
                [filePath],
                changes => { tcs.TrySetResult(changes); return Task.CompletedTask; },
                NullLoggerFactory.Instance);

            await Task.Delay(200); // Let watcher start
            await File.WriteAllTextAsync(filePath, "// modified");

            var result = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Contains(result, p => p.EndsWith("Test.cs"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task IgnoresUntrackedCsFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"parlance-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var trackedFile = Path.Combine(dir, "Tracked.cs");
            await File.WriteAllTextAsync(trackedFile, "// tracked");

            var untrackedFile = Path.Combine(dir, "Untracked.cs");
            await File.WriteAllTextAsync(untrackedFile, "// original");

            var callbackFired = false;
            await using var watcher = new WorkspaceFileWatcher(
                [dir],
                [trackedFile],
                _ => { callbackFired = true; return Task.CompletedTask; },
                NullLoggerFactory.Instance);

            await Task.Delay(200);
            await File.WriteAllTextAsync(untrackedFile, "// modified");

            await Task.Delay(1000);
            Assert.False(callbackFired);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task IgnoresNonCsFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"parlance-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var txtFile = Path.Combine(dir, "readme.txt");
            await File.WriteAllTextAsync(txtFile, "original");

            var callbackFired = false;
            await using var watcher = new WorkspaceFileWatcher(
                [dir],
                [],
                _ => { callbackFired = true; return Task.CompletedTask; },
                NullLoggerFactory.Instance);

            await Task.Delay(200);
            await File.WriteAllTextAsync(txtFile, "modified");

            await Task.Delay(1000); // Wait longer than debounce window
            Assert.False(callbackFired);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task IgnoresCsFilesInObjSubdirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"parlance-test-{Guid.NewGuid():N}");
        var objDir = Path.Combine(dir, "obj", "Debug", "net10.0");
        Directory.CreateDirectory(objDir);
        try
        {
            var generatedFile = Path.Combine(objDir, "AssemblyInfo.cs");
            await File.WriteAllTextAsync(generatedFile, "// generated");

            var callbackFired = false;
            await using var watcher = new WorkspaceFileWatcher(
                [dir],
                [generatedFile], // Explicitly in watched files — watcher should still reject
                _ => { callbackFired = true; return Task.CompletedTask; },
                NullLoggerFactory.Instance);

            await Task.Delay(200);
            await File.WriteAllTextAsync(generatedFile, "// regenerated");

            await Task.Delay(1000);
            Assert.False(callbackFired);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task IgnoresCsFilesInBinSubdirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"parlance-test-{Guid.NewGuid():N}");
        var binDir = Path.Combine(dir, "bin", "Debug", "net10.0");
        Directory.CreateDirectory(binDir);
        try
        {
            var generatedFile = Path.Combine(binDir, "SomeGenerated.cs");
            await File.WriteAllTextAsync(generatedFile, "// generated");

            var callbackFired = false;
            await using var watcher = new WorkspaceFileWatcher(
                [dir],
                [generatedFile],
                _ => { callbackFired = true; return Task.CompletedTask; },
                NullLoggerFactory.Instance);

            await Task.Delay(200);
            await File.WriteAllTextAsync(generatedFile, "// regenerated");

            await Task.Delay(1000);
            Assert.False(callbackFired);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task DisposeStopsWatching()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"parlance-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "Test.cs");
            await File.WriteAllTextAsync(filePath, "// original");

            var callbackCount = 0;
            var watcher = new WorkspaceFileWatcher(
                [dir],
                [filePath],
                _ => { Interlocked.Increment(ref callbackCount); return Task.CompletedTask; },
                NullLoggerFactory.Instance);

            await watcher.DisposeAsync();

            await File.WriteAllTextAsync(filePath, "// modified after dispose");
            await Task.Delay(1000);
            Assert.Equal(0, callbackCount);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}

public sealed class FileWatcherSessionTests
{
    [Fact]
    public async Task FileChange_DoesNotCreateFeedbackLoop()
    {
        // Regression: OnFileChanges called TryApplyChanges which wrote back to disk,
        // re-triggering the watcher in an infinite loop.
        var tempDir = Path.Combine(Path.GetTempPath(), $"parlance-loop-{Guid.NewGuid():N}");
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
                EnableFileWatching: true);

            await using var session = await CSharpWorkspaceSession.OpenProjectAsync(csproj, options);

            // Edit the file — watcher should fire once then stop
            await File.WriteAllTextAsync(sourceFile,
                "namespace Test; public class Class1 { public int X { get; } }");

            // Wait for the first watcher cycle (debounce = 300ms + processing)
            await Task.Delay(TimeSpan.FromSeconds(2));

            var versionAfterFirstChange = session.SnapshotVersion;
            Assert.True(versionAfterFirstChange > 1,
                "SnapshotVersion should have incremented at least once after file edit");

            // Wait another full debounce window — version must NOT keep climbing
            await Task.Delay(TimeSpan.FromSeconds(2));

            Assert.Equal(versionAfterFirstChange, session.SnapshotVersion);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
