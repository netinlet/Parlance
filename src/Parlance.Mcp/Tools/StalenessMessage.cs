namespace Parlance.Mcp.Tools;

/// <summary>
/// One wording for the "the workspace moved past the snapshot you expected" signal, shared by every tool
/// that reports <c>stale</c>. Kept in one place so the message stays consistent as more tools gain staleness
/// checks (see <c>apply-code-action</c>, <c>analyze</c>).
/// </summary>
internal static class StalenessMessage
{
    /// <summary>The caller passed an <c>expectedSnapshotVersion</c> the workspace has since advanced past.</summary>
    public static string ExpectedMismatch(long expected, long actual) =>
        $"Workspace moved past the expected snapshot (expected {expected}, now {actual}). Re-query.";

    /// <summary>A cached action was computed against a snapshot that has since been superseded.</summary>
    public static string ActionSuperseded(string actionId) =>
        $"Action '{actionId}' was computed against a superseded snapshot. Re-query fixes or refactorings.";
}
