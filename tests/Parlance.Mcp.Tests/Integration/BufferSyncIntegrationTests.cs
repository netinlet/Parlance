using Parlance.CSharp.Workspace;
using Parlance.CSharp.Workspace.Tests.Integration;

namespace Parlance.Mcp.Tests.Integration;

public sealed class BufferSyncIntegrationTests
{
    [Fact]
    public async Task OverlaidBuffer_IsVisibleToCompilation_ThenRevertsOnClose()
    {
        var result = await CSharpWorkspaceSession.TryOpenSolutionAsync(
            TestPaths.FindSolutionPath(),
            new WorkspaceOpenOptions(Mode: WorkspaceMode.Server, EnableFileWatching: false));
        await using var session = Assert.IsType<WorkspaceLoadResult.Success>(result).Session;

        var path = session.CurrentSolution.Projects.SelectMany(p => p.Documents)
            .Select(d => d.FilePath).First(p => p is not null)!;
        var disk = await File.ReadAllTextAsync(path);

        var overlay = disk + "\n// PARLANCE_OVERLAY_MARKER\n";
        await session.SyncBufferAsync(path, overlay);

        var docId = session.CurrentSolution.GetDocumentIdsWithFilePath(path)[0];
        var tree = await session.CurrentSolution.GetDocument(docId)!.GetSyntaxTreeAsync();
        Assert.Contains("PARLANCE_OVERLAY_MARKER", tree!.ToString());

        await session.CloseBufferAsync(path);
        var reverted = await session.CurrentSolution.GetDocument(docId)!.GetTextAsync();
        Assert.DoesNotContain("PARLANCE_OVERLAY_MARKER", reverted.ToString());
    }
}
