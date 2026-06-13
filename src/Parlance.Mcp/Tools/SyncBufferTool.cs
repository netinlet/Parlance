using System.ComponentModel;
using ModelContextProtocol.Server;
using Parlance.CSharp.Workspace;

namespace Parlance.Mcp.Tools;

[McpServerToolType]
public sealed class SyncBufferTool
{
    [McpServerTool(Name = "sync-buffer")]
    [Description("SPECULATIVE in-memory buffer overlay (LSP didChange-style). Pushes full-text buffer " +
                 "content so analyze/navigation/code-action tools see an in-flight edit before it is saved. " +
                 "Does NOT write to disk and does NOT persist anything — the overlay lives only in memory and " +
                 "wins over disk until close-buffer reverts it. Returns the new per-document version and " +
                 "snapshot. To save an edit the agent writes with its own tools; to materialize a code action " +
                 "into an applyable edit use apply-code-action.")]
    public static Task<SyncBufferResult> SyncBuffer(
        WorkspaceSessionHolder holder,
        [Description("Absolute path of the file being edited")] string path,
        [Description("Full buffer text (full-text replacement)")] string text,
        CancellationToken ct = default) =>
        holder.State.Match(
            notLoaded: () => Task.FromResult(SyncBufferResult.NotLoaded()),
            loaded: session => ApplySyncAsync(session, path, text, ct),
            loadFailed: failure => Task.FromResult(SyncBufferResult.LoadFailed(failure.Message)),
            disposed: () => Task.FromResult(SyncBufferResult.NotLoaded()));

    [McpServerTool(Name = "close-buffer")]
    [Description("Drop the SPECULATIVE in-memory buffer overlay for a file, reverting it to the on-disk " +
                 "contents. Never writes to disk; it only discards the in-memory overlay sync-buffer created.")]
    public static Task<SyncBufferResult> CloseBuffer(
        WorkspaceSessionHolder holder,
        [Description("Absolute path whose overlay should be dropped")] string path,
        CancellationToken ct = default) =>
        holder.State.Match(
            notLoaded: () => Task.FromResult(SyncBufferResult.NotLoaded()),
            loaded: session => ApplyCloseAsync(session, path, ct),
            loadFailed: failure => Task.FromResult(SyncBufferResult.LoadFailed(failure.Message)),
            disposed: () => Task.FromResult(SyncBufferResult.NotLoaded()));

    private static async Task<SyncBufferResult> ApplySyncAsync(
        CSharpWorkspaceSession session, string path, string text, CancellationToken ct)
    {
        var version = await session.SyncBufferAsync(path, text, ct);
        return version == 0
            ? SyncBufferResult.NotInWorkspace(path, session.SnapshotVersion)
            : SyncBufferResult.Synced(version, session.SnapshotVersion);
    }

    private static async Task<SyncBufferResult> ApplyCloseAsync(
        CSharpWorkspaceSession session, string path, CancellationToken ct)
    {
        var outcome = await session.CloseBufferAsync(path, ct);
        return outcome switch
        {
            // NotOpen is reported as closed too: dropping an absent overlay is idempotent.
            CloseBufferOutcome.Closed or CloseBufferOutcome.NotOpen =>
                SyncBufferResult.Closed(session.SnapshotVersion),
            CloseBufferOutcome.RevertUnavailable =>
                SyncBufferResult.RevertUnavailable(path, session.SnapshotVersion),
            _ => SyncBufferResult.Closed(session.SnapshotVersion),
        };
    }
}

public sealed record SyncBufferResult(string Status, string? Message)
{
    public long SnapshotVersion { get; init; }
    public long DocumentVersion { get; init; }

    public static SyncBufferResult Synced(long documentVersion, long snapshotVersion) =>
        new("synced", null) { DocumentVersion = documentVersion, SnapshotVersion = snapshotVersion };

    public static SyncBufferResult Closed(long snapshotVersion) =>
        new("closed", null) { SnapshotVersion = snapshotVersion };

    public static SyncBufferResult RevertUnavailable(string path, long snapshotVersion) =>
        new("revert_unavailable",
            $"'{path}' is missing on disk; the buffer overlay was left open because it cannot be reverted.")
        { SnapshotVersion = snapshotVersion };

    public static SyncBufferResult NotInWorkspace(string path, long snapshotVersion) =>
        new("not_in_workspace", $"'{path}' is not a document in the loaded workspace")
        { SnapshotVersion = snapshotVersion };

    public static SyncBufferResult NotLoaded() => new("not_loaded", "Workspace is still loading");

    public static SyncBufferResult LoadFailed(string message) => new("load_failed", message);
}
