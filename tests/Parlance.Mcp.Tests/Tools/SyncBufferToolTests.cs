using Parlance.CSharp.Workspace;
using Parlance.CSharp.Workspace.Tests.Integration;
using Parlance.Mcp.Tools;

namespace Parlance.Mcp.Tests.Tools;

public sealed class SyncBufferToolTests
{
    [Fact]
    public void NotLoaded_ReturnsNotLoadedStatus()
    {
        var holder = new WorkspaceSessionHolder();
        var result = SyncBufferTool.SyncBuffer(holder, "/x.cs", "text").GetAwaiter().GetResult();
        Assert.Equal("not_loaded", result.Status);
    }

    [Fact]
    public void Disposed_ReturnsNotLoadedStatus()
    {
        var holder = new WorkspaceSessionHolder();
        holder.Dispose();
        var result = SyncBufferTool.SyncBuffer(holder, "/x.cs", "text").GetAwaiter().GetResult();
        Assert.Equal("not_loaded", result.Status);
    }

    [Fact]
    public void LoadFailed_ReturnsLoadFailedStatus()
    {
        var holder = new WorkspaceSessionHolder();
        holder.SetLoadFailure(new WorkspaceLoadFailure("boom", "/x.sln"));
        var result = SyncBufferTool.SyncBuffer(holder, "/x.cs", "text").GetAwaiter().GetResult();
        Assert.Equal("load_failed", result.Status);
        Assert.Equal("boom", result.Message);
    }

    [Fact]
    public void CloseBuffer_NotLoaded_ReturnsNotLoadedStatus()
    {
        var holder = new WorkspaceSessionHolder();
        var result = SyncBufferTool.CloseBuffer(holder, "/x.cs").GetAwaiter().GetResult();
        Assert.Equal("not_loaded", result.Status);
    }

    [Fact]
    public void CloseBuffer_LoadFailed_ReturnsLoadFailedStatus()
    {
        var holder = new WorkspaceSessionHolder();
        holder.SetLoadFailure(new WorkspaceLoadFailure("boom", "/x.sln"));
        var result = SyncBufferTool.CloseBuffer(holder, "/x.cs").GetAwaiter().GetResult();
        Assert.Equal("load_failed", result.Status);
        Assert.Equal("boom", result.Message);
    }

    // Loaded-path snapshot-stamp coverage for the buffer tool. EveryToolStampsLiveSnapshotTests can't
    // exercise it — its loaded path mutates a Server-mode session and throws in that test's read-only
    // shared fixture — so the live-snapshot stamping is asserted here against an isolated Server session.
    [Fact]
    public async Task SyncBuffer_Loaded_StampsLiveSnapshot()
    {
        await using var session = await LoadServerSessionAsync();
        var holder = new WorkspaceSessionHolder();
        holder.SetSession(session);
        var path = AnyDocumentPath(session);

        var result = await SyncBufferTool.SyncBuffer(holder, path, "// overlaid for stamp test");

        Assert.Equal("synced", result.Status);
        Assert.NotEqual(0, result.SnapshotVersion);
        Assert.Equal(session.SnapshotVersion, result.SnapshotVersion);
    }

    [Fact]
    public async Task CloseBuffer_Loaded_StampsLiveSnapshot()
    {
        await using var session = await LoadServerSessionAsync();
        var holder = new WorkspaceSessionHolder();
        holder.SetSession(session);
        var path = AnyDocumentPath(session);

        // Dropping an absent overlay is idempotent (NotOpen -> closed); the assertion is on the stamp.
        var result = await SyncBufferTool.CloseBuffer(holder, path);

        Assert.Equal("closed", result.Status);
        Assert.NotEqual(0, result.SnapshotVersion);
        Assert.Equal(session.SnapshotVersion, result.SnapshotVersion);
    }

    private static async Task<CSharpWorkspaceSession> LoadServerSessionAsync()
    {
        var result = await CSharpWorkspaceSession.TryOpenSolutionAsync(
            TestPaths.FindSolutionPath(),
            new WorkspaceOpenOptions(Mode: WorkspaceMode.Server, EnableFileWatching: false));
        return Assert.IsType<WorkspaceLoadResult.Success>(result).Session;
    }

    private static string AnyDocumentPath(CSharpWorkspaceSession session) =>
        session.CurrentSolution.Projects.SelectMany(p => p.Documents)
            .Select(d => d.FilePath).First(p => p is not null)!;
}
