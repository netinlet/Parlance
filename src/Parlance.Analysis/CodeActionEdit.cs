using System.Collections.Immutable;

namespace Parlance.Analysis;

/// <summary>
/// The complete, machine-applyable result of a code action — an LSP <c>WorkspaceEdit</c> in spirit.
/// Where <see cref="CodeActionPreview"/> shows only the text changed in existing documents (enough to
/// <em>display</em> a diff), this carries everything an agent needs to <em>apply</em> the action without
/// re-deriving anything: per-document ordered text edits plus the resource operations (create / delete /
/// rename) that a code action can additionally produce. Parlance computes and returns it; the agent
/// persists it with its own write tools — Parlance never writes to disk.
/// </summary>
public sealed record CodeActionEdit(
    string ActionId,
    string Title,
    ImmutableList<DocumentEdit> DocumentEdits,
    ImmutableList<ResourceOperation> ResourceOperations,
    long SnapshotVersion = 0,
    bool IsExpired = false,
    string? ErrorMessage = null,
    ImmutableList<RenameHint>? RenameHints = null)
{
    /// <summary>
    /// Identifiers the action introduced with a placeholder name and a "rename me" marker (Roslyn's
    /// <c>CodeAction_Rename</c> annotation) — e.g. Extract Method's <c>NewMethod</c>. An IDE would drop the
    /// user straight into an inline rename here; an agent can't, so the placeholder ships as-is unless it acts.
    /// Surfacing the placeholder lets the agent substitute a real name in the same write — no second round-trip.
    /// </summary>
    public ImmutableList<RenameHint> RenameHints { get; init; } = RenameHints ?? [];

    /// <summary>The cached action was computed against a superseded snapshot; the edit is no longer valid.</summary>
    public static CodeActionEdit Expired(string actionId, string title) =>
        new(actionId, title, [], [], IsExpired: true);

    /// <summary>The action could not produce an applyable edit (e.g. no text changes, or it threw).</summary>
    public static CodeActionEdit Failed(string actionId, string title, string errorMessage) =>
        new(actionId, title, [], [], ErrorMessage: errorMessage);
}

/// <summary>
/// A placeholder identifier a code action wants renamed. <see cref="FilePath"/> is the document's path,
/// <see cref="PlaceholderName"/> the generated name (e.g. <c>NewMethod</c>), and <see cref="Range"/> its
/// location in the post-edit text so an agent can rewrite it to a meaningful name as it applies the edit.
/// </summary>
public sealed record RenameHint(string FilePath, string PlaceholderName, TextRange Range);

/// <summary>
/// Ordered text edits against one existing document. <see cref="FilePath"/> is the document's pre-change
/// path; a move is carried separately as a <see cref="ResourceOperation.RenameFile"/>. Edits are
/// non-overlapping and ordered <em>descending</em> by position (bottom-to-top): apply them in the given
/// order and each edit's range stays valid against the original text, because no earlier-applied edit
/// shifts the offsets of a later one. <see cref="Newline"/>/<see cref="Encoding"/> describe the existing
/// file so the agent's write does not churn line endings — each edit's replacement text has already been
/// normalised to <see cref="Newline"/>.
/// </summary>
public sealed record DocumentEdit(
    string FilePath,
    long? DocumentVersion,
    string Newline,
    string Encoding,
    bool HasBom,
    ImmutableList<TextEdit> Edits);

/// <summary>A file-level operation a code action can produce alongside (or instead of) text edits.</summary>
public abstract record ResourceOperation
{
    /// <summary>Create a new file with the given full content.</summary>
    public sealed record CreateFile(
        string FilePath, string Content, string Newline, string Encoding, bool HasBom) : ResourceOperation;

    /// <summary>Delete an existing file.</summary>
    public sealed record DeleteFile(string FilePath) : ResourceOperation;

    /// <summary>Move/rename a file from one path to another.</summary>
    public sealed record RenameFile(string OldFilePath, string NewFilePath) : ResourceOperation;
}
