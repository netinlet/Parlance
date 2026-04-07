using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Parlance.CSharp.Workspace.Internal;

internal sealed class WorkspaceFileWatcher : IDisposable, IAsyncDisposable
{
    private readonly FileSystemWatcher[] _watchers;
    private readonly Func<IReadOnlyList<string>, Task> _onChanges;
    private readonly Timer _debounceTimer;
    private readonly ConcurrentBag<string> _pendingChanges = new();
    private readonly HashSet<string> _watchedFiles;
    private readonly SemaphoreSlim _processingLock = new(1, 1);
    private readonly Lock _taskLock = new();
    private readonly ILogger<WorkspaceFileWatcher> _logger;
    private Task _processingTask = Task.CompletedTask;
    private bool _disposed;
    private const int DebounceMs = 300;

    public WorkspaceFileWatcher(
        IReadOnlyList<string> directories,
        IReadOnlyList<string> watchedFiles,
        Func<IReadOnlyList<string>, Task> onChanges,
        ILoggerFactory loggerFactory)
    {
        _onChanges = onChanges;
        _watchedFiles = new HashSet<string>(watchedFiles, StringComparer.OrdinalIgnoreCase);
        _logger = loggerFactory.CreateLogger<WorkspaceFileWatcher>();
        _debounceTimer = new Timer(OnDebounceElapsed, null, Timeout.Infinite, Timeout.Infinite);

        _watchers = directories
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(dir =>
            {
                var watcher = new FileSystemWatcher(dir, "*.cs")
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                    IncludeSubdirectories = true,
                    EnableRaisingEvents = true
                };
                watcher.Changed += OnFileChanged;
                return watcher;
            })
            .ToArray();

        _logger.LogInformation(
            "File watcher started: {Count} director(ies)", _watchers.Length);
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (_disposed || IsBuildOutputPath(e.FullPath) || !_watchedFiles.Contains(e.FullPath))
            return;

        _pendingChanges.Add(e.FullPath);
        _debounceTimer.Change(DebounceMs, Timeout.Infinite);
        _logger.LogDebug("File change detected: {Path}", e.FullPath);
    }

    private void OnDebounceElapsed(object? state)
    {
        if (_disposed) return;

        lock (_taskLock)
        {
            if (_disposed) return;
            _processingTask = ProcessPendingChangesAsync();
        }
    }

    private async Task ProcessPendingChangesAsync()
    {
        await _processingLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;

            var changes = new List<string>();
            while (_pendingChanges.TryTake(out var path))
                changes.Add(path);

            if (changes.Count == 0) return;

            var distinct = changes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            _logger.LogInformation("Processing {Count} file change(s)", distinct.Count);

            try
            {
                await _onChanges(distinct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing file changes");
            }
        }
        finally
        {
            _processingLock.Release();
        }
    }

    internal static bool IsBuildOutputPath(string path)
    {
        var sep = Path.DirectorySeparatorChar;
        return path.Contains($"{sep}bin{sep}", StringComparison.OrdinalIgnoreCase)
            || path.Contains($"{sep}obj{sep}", StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        DisposeCore();
        _logger.LogInformation("File watcher stopped (sync)");
    }

    public async ValueTask DisposeAsync()
    {
        var processingTask = DisposeCore();
        await processingTask.ConfigureAwait(false);
        _logger.LogInformation("File watcher stopped");
    }

    private Task DisposeCore()
    {
        foreach (var watcher in _watchers)
            watcher.EnableRaisingEvents = false;

        _disposed = true;
        _debounceTimer.Change(Timeout.Infinite, Timeout.Infinite);
        _debounceTimer.Dispose();

        Task processingTask;
        lock (_taskLock)
        {
            processingTask = _processingTask;
        }

        foreach (var watcher in _watchers)
            watcher.Dispose();

        _processingLock.Dispose();
        return processingTask;
    }
}
