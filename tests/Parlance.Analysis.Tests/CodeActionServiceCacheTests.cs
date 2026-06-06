using Microsoft.Extensions.Logging.Abstractions;
using Parlance.CSharp.Workspace;

namespace Parlance.Analysis.Tests;

public sealed class CodeActionServiceCacheTests
{
    [Fact]
    public void EvictStaleEntries_removes_entries_from_superseded_snapshots()
    {
        using var holder = new WorkspaceSessionHolder();
        var service = new CodeActionService(holder, NullLogger<CodeActionService>.Instance);

        service.AddToCacheForTest("fix-1", snapshotVersion: 1);
        service.AddToCacheForTest("fix-2", snapshotVersion: 1);
        service.AddToCacheForTest("refactor-1", snapshotVersion: 2);
        Assert.Equal(3, service.CacheCount);

        service.EvictStaleEntries(currentVersion: 2);

        // Only the current-snapshot entry survives; the two from v1 are gone.
        Assert.Equal(1, service.CacheCount);
    }

    [Fact]
    public void EvictStaleEntries_keeps_current_snapshot_entries()
    {
        using var holder = new WorkspaceSessionHolder();
        var service = new CodeActionService(holder, NullLogger<CodeActionService>.Instance);

        service.AddToCacheForTest("fix-1", snapshotVersion: 5);
        service.AddToCacheForTest("fix-2", snapshotVersion: 5);

        service.EvictStaleEntries(currentVersion: 5);

        Assert.Equal(2, service.CacheCount);
    }
}
