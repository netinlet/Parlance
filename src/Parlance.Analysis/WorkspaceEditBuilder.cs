using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.Text;

namespace Parlance.Analysis;

/// <summary>
/// Turns the solution delta a code action produces (<c>current → changed</c>) into a complete
/// <see cref="CodeActionEdit"/>. Pure with respect to its two <see cref="Solution"/> arguments — it holds
/// no workspace state — so it can be exercised against hand-built <c>AdhocWorkspace</c> solutions covering
/// every resource-operation shape. This is the half the preview path leaves out: preview maps only changed
/// documents; apply additionally covers added / removed / renamed documents.
/// </summary>
internal static class WorkspaceEditBuilder
{
    public static async Task<CodeActionEdit> BuildAsync(
        string actionId,
        string title,
        Solution currentSolution,
        Solution changedSolution,
        Func<string, long?> bufferVersion,
        long snapshotVersion,
        CancellationToken ct)
    {
        var documentEdits = ImmutableList.CreateBuilder<DocumentEdit>();
        var resourceOps = ImmutableList.CreateBuilder<ResourceOperation>();
        var renameHints = ImmutableList.CreateBuilder<RenameHint>();

        foreach (var projectChange in changedSolution.GetChanges(currentSolution).GetProjectChanges())
        {
            // Added documents → create-file operations carrying the full new content.
            foreach (var docId in projectChange.GetAddedDocuments())
            {
                var newDoc = changedSolution.GetDocument(docId);
                if (newDoc?.FilePath is null) continue;
                await CollectRenameHintsAsync(newDoc, renameHints, ct);
                var text = await newDoc.GetTextAsync(ct);
                // Materialise the content once; detect the newline from the SourceText line table rather
                // than a second full ToString().
                resourceOps.Add(new ResourceOperation.CreateFile(
                    newDoc.FilePath, text.ToString(),
                    SourceTextFacts.DetectNewline(text),
                    SourceTextFacts.EncodingName(text),
                    SourceTextFacts.HasBom(text)));
            }

            // Removed documents → delete-file operations.
            foreach (var docId in projectChange.GetRemovedDocuments())
            {
                var oldDoc = currentSolution.GetDocument(docId);
                if (oldDoc?.FilePath is null) continue;
                resourceOps.Add(new ResourceOperation.DeleteFile(oldDoc.FilePath));
            }

            // Changed documents → a rename op when the path moved, and/or ordered text edits.
            foreach (var docId in projectChange.GetChangedDocuments())
            {
                var oldDoc = currentSolution.GetDocument(docId);
                var newDoc = changedSolution.GetDocument(docId);
                if (oldDoc is null || newDoc is null) continue;

                await CollectRenameHintsAsync(newDoc, renameHints, ct);

                var oldPath = oldDoc.FilePath;
                var newPath = newDoc.FilePath;
                if (oldPath is not null && newPath is not null &&
                    !string.Equals(oldPath, newPath, StringComparison.Ordinal))
                {
                    resourceOps.Add(new ResourceOperation.RenameFile(oldPath, newPath));
                }

                var oldText = await oldDoc.GetTextAsync(ct);

                // Respect the existing file's line ending so the agent's write does not churn EOLs.
                var newline = SourceTextFacts.DetectNewline(oldText);
                // Document-level diff: yields minimal text changes even when the changed document shares no
                // text-snapshot lineage with the original — which is the normal case, since a code fix hands
                // back a reparsed/reformatted document. SourceText.GetTextChanges would fall back to a single
                // whole-document replacement here, doubling the entire file over the wire.
                var changes = await newDoc.GetTextChangesAsync(oldDoc, ct);
                var edits = ToOrderedEdits(oldText, changes, newline);
                if (edits.IsEmpty) continue;

                // Edits target the pre-change (old) path; any move is the separate rename op above.
                documentEdits.Add(new DocumentEdit(
                    oldPath ?? newPath ?? "",
                    bufferVersion(oldPath ?? ""),
                    newline,
                    SourceTextFacts.EncodingName(oldText),
                    SourceTextFacts.HasBom(oldText),
                    edits));
            }
        }

        return new CodeActionEdit(
            actionId, title, documentEdits.ToImmutable(), resourceOps.ToImmutable(), snapshotVersion,
            RenameHints: renameHints.ToImmutable());
    }

    /// <summary>
    /// Finds identifiers the action tagged with Roslyn's <c>CodeAction_Rename</c> annotation in
    /// <paramref name="newDoc"/> — the marker that drives inline-rename in an IDE (Extract Method's
    /// <c>NewMethod</c> is the canonical case). Each becomes a <see cref="RenameHint"/> carrying the
    /// placeholder name and its 1-based location in the post-edit text, so an agent can rewrite it as it
    /// applies the edit instead of taking a second rename round-trip.
    /// </summary>
    private static async Task CollectRenameHintsAsync(
        Document newDoc, ImmutableList<RenameHint>.Builder hints, CancellationToken ct)
    {
        var root = await newDoc.GetSyntaxRootAsync(ct);
        if (root is null) return;

        var annotated = root.GetAnnotatedNodesAndTokens(RenameAnnotation.Kind).ToList();
        if (annotated.Count == 0) return;

        var text = await newDoc.GetTextAsync(ct);
        foreach (var item in annotated)
        {
            var name = item.IsToken ? item.AsToken().ValueText : item.ToString();
            var start = text.Lines.GetLinePosition(item.Span.Start);
            var end = text.Lines.GetLinePosition(item.Span.End);
            hints.Add(new RenameHint(
                newDoc.FilePath ?? "",
                name,
                new TextRange(start.Line + 1, start.Character + 1, end.Line + 1, end.Character + 1)));
        }
    }

    /// <summary>
    /// Turns a set of minimal <see cref="TextChange"/>s into applyable <see cref="TextEdit"/>s: each
    /// replacement is normalised to <paramref name="newline"/>, and the edits are returned <em>descending</em>
    /// by span start so sequential (bottom-to-top) application leaves earlier ranges valid. Returns empty when
    /// there are no changes.
    /// </summary>
    private static ImmutableList<TextEdit> ToOrderedEdits(
        SourceText oldText, IEnumerable<TextChange> changes, string newline) =>
        changes
            .OrderByDescending(c => c.Span.Start)
            .Select(c =>
            {
                var edit = CodeActionService.ToTextEdit(oldText, c);
                return edit with { NewText = SourceTextFacts.NormalizeNewlines(edit.NewText, newline) };
            })
            .ToImmutableList();
}

/// <summary>Small, pure observations about a <see cref="SourceText"/> used to keep applied edits faithful
/// to the existing file's line ending and encoding.</summary>
internal static class SourceTextFacts
{
    /// <summary>The dominant line ending in <paramref name="text"/>; defaults to <c>"\n"</c> when there is none.
    /// Walks the <see cref="SourceText"/> line table and indexes single characters rather than materialising
    /// the whole file into one contiguous string.</summary>
    public static string DetectNewline(SourceText text)
    {
        int crlf = 0, lf = 0, cr = 0;
        foreach (var line in text.Lines)
        {
            var breakLength = line.EndIncludingLineBreak - line.End;
            switch (breakLength)
            {
                case 2:
                    crlf++;
                    break;
                case 1 when text[line.End] == '\r':
                    cr++;
                    break;
                case 1:
                    lf++;
                    break;
            }
        }

        if (crlf == 0 && lf == 0 && cr == 0) return "\n";
        if (crlf >= lf && crlf >= cr) return "\r\n";
        return cr > lf ? "\r" : "\n";
    }

    /// <summary>Rewrites every line ending in <paramref name="text"/> to <paramref name="newline"/>.</summary>
    public static string NormalizeNewlines(string text, string newline)
    {
        if (text.Length == 0) return text;
        var lf = text.Replace("\r\n", "\n").Replace('\r', '\n');
        return newline == "\n" ? lf : lf.Replace("\n", newline);
    }

    public static string EncodingName(SourceText text) => text.Encoding?.WebName ?? "utf-8";

    public static bool HasBom(SourceText text) => text.Encoding?.GetPreamble().Length > 0;
}
