using Microsoft.Extensions.Logging.Abstractions;
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
