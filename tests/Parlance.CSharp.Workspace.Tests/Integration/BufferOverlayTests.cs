using Parlance.CSharp.Workspace;

namespace Parlance.CSharp.Workspace.Tests.Integration;

public sealed class BufferOverlayTests
{
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

    [Fact]
    public async Task SyncBuffer_OverlaysText_WithoutTouchingDisk()
    {
        await using var session = await LoadServerSessionAsync();
        var path = AnyDocumentPath(session);
        var diskBefore = await File.ReadAllTextAsync(path);

        var version = await session.SyncBufferAsync(path, "// overlaid\n" + diskBefore);

        var docId = session.CurrentSolution.GetDocumentIdsWithFilePath(path)[0];
        var overlaidText = (await session.CurrentSolution.GetDocument(docId)!.GetTextAsync()).ToString();
        Assert.StartsWith("// overlaid", overlaidText);
        Assert.Equal(diskBefore, await File.ReadAllTextAsync(path)); // disk untouched
        Assert.Equal(1, version);
        Assert.True(session.IsBufferOpen(path));
    }

    [Fact]
    public async Task SyncBuffer_BumpsPerDocumentVersionAndSnapshot()
    {
        await using var session = await LoadServerSessionAsync();
        var path = AnyDocumentPath(session);
        var snap0 = session.SnapshotVersion;

        var v1 = await session.SyncBufferAsync(path, "// a");
        var v2 = await session.SyncBufferAsync(path, "// b");

        Assert.Equal(1, v1);
        Assert.Equal(2, v2);
        Assert.True(session.SnapshotVersion > snap0);
    }

    [Fact]
    public async Task SyncBuffer_IdenticalText_DoesNotBumpSnapshot()
    {
        await using var session = await LoadServerSessionAsync();
        var path = AnyDocumentPath(session);

        var v1 = await session.SyncBufferAsync(path, "// same text");
        var snapAfterFirst = session.SnapshotVersion;

        var v2 = await session.SyncBufferAsync(path, "// same text"); // identical re-sync

        Assert.Equal(v1, v2);                               // no new document version
        Assert.Equal(snapAfterFirst, session.SnapshotVersion); // no snapshot bump / recompile
        Assert.True(session.IsBufferOpen(path));
    }

    [Fact]
    public async Task CloseBuffer_RevertsToDisk()
    {
        await using var session = await LoadServerSessionAsync();
        var path = AnyDocumentPath(session);
        var disk = await File.ReadAllTextAsync(path);

        await session.SyncBufferAsync(path, "// overlaid");
        await session.CloseBufferAsync(path);

        var docId = session.CurrentSolution.GetDocumentIdsWithFilePath(path)[0];
        Assert.Equal(disk, (await session.CurrentSolution.GetDocument(docId)!.GetTextAsync()).ToString());
        Assert.False(session.IsBufferOpen(path));
    }

    [Fact]
    public async Task SyncBuffer_UnknownPath_ReturnsZero()
    {
        await using var session = await LoadServerSessionAsync();
        var version = await session.SyncBufferAsync("/not/in/solution.cs", "x");
        Assert.Equal(0, version);
    }

    [Fact]
    public async Task BufferVersion_TracksOpenOverlay_AndIsNullOtherwise()
    {
        await using var session = await LoadServerSessionAsync();
        var path = AnyDocumentPath(session);

        Assert.Null(session.BufferVersion(path));          // no overlay yet
        Assert.Null(session.BufferVersion("/not/in/solution.cs")); // not a document

        var v1 = await session.SyncBufferAsync(path, "// a");
        Assert.Equal(v1, session.BufferVersion(path));     // overlay version surfaced

        var v2 = await session.SyncBufferAsync(path, "// b");
        Assert.Equal(v2, session.BufferVersion(path));     // advances with the overlay

        await session.CloseBufferAsync(path);
        Assert.Null(session.BufferVersion(path));          // null again after close
    }

    [Fact]
    public async Task CloseBuffer_NotOpen_IsNoOp()
    {
        await using var session = await LoadServerSessionAsync();
        var path = AnyDocumentPath(session);
        var snap0 = session.SnapshotVersion;

        await session.CloseBufferAsync(path); // never opened

        Assert.False(session.IsBufferOpen(path));
        Assert.Equal(snap0, session.SnapshotVersion); // no-op: no snapshot bump
    }

    [Fact]
    public async Task RefreshAsync_DoesNotClobberOpenOverlay()
    {
        await using var session = await LoadServerSessionAsync();
        var path = AnyDocumentPath(session);

        await session.SyncBufferAsync(path, "// overlaid-marker");
        await session.RefreshAsync();   // must NOT revert the overlaid document

        var docId = session.CurrentSolution.GetDocumentIdsWithFilePath(path)[0];
        var text = (await session.CurrentSolution.GetDocument(docId)!.GetTextAsync()).ToString();
        Assert.Equal("// overlaid-marker", text);
        Assert.True(session.IsBufferOpen(path));
    }
}
