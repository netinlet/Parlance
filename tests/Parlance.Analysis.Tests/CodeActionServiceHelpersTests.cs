using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;

namespace Parlance.Analysis.Tests;

// Covers the two pure helpers behind the code-action apply path: the annotation-scoped formatting pass that
// gives apply/preview LSP-host parity (#1), and the non-overlapping text-change merge that powers the
// document fix-all (#4).
public sealed class CodeActionServiceFormattingTests
{
    private static (Solution Solution, DocumentId DocId) NewDoc(string text)
    {
        var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var solution = workspace.CurrentSolution.AddProject(
            ProjectInfo.Create(projectId, VersionStamp.Create(), "P", "P", LanguageNames.CSharp));
        var docId = DocumentId.CreateNewId(projectId);
        return (solution.AddDocument(docId, "A.cs", SourceText.From(text), filePath: "/repo/A.cs"), docId);
    }

    [Fact]
    public async Task FormatChangedDocuments_FormatsRegionTaggedWithFormatterAnnotation()
    {
        var (current, docId) = NewDoc("class A { }\n");
        // A badly-spaced body, with the whole root tagged Formatter.Annotation — what a code action emits.
        var badRoot = CSharpSyntaxTree.ParseText("class A {\nvoid M(){int x=1;}\n}\n").GetRoot()
            .WithAdditionalAnnotations(Formatter.Annotation);
        var changed = current.WithDocumentSyntaxRoot(docId, badRoot);

        var formatted = await CodeActionService.FormatChangedDocumentsAsync(current, changed, CancellationToken.None);
        var text = (await formatted.GetDocument(docId)!.GetTextAsync()).ToString();

        // The formatter ran over the annotated span: operators get their spacing.
        Assert.Contains("int x = 1;", text);
    }

    [Fact]
    public async Task FormatChangedDocuments_LeavesUnannotatedChangeUntouched()
    {
        var (current, docId) = NewDoc("class A { }\n");
        const string bad = "class A {\nvoid M(){int x=1;}\n}\n"; // plain text — no Formatter.Annotation
        var changed = current.WithDocumentText(docId, SourceText.From(bad));

        var formatted = await CodeActionService.FormatChangedDocumentsAsync(current, changed, CancellationToken.None);
        var text = (await formatted.GetDocument(docId)!.GetTextAsync()).ToString();

        // No annotated span to act on → the formatter does not reformat the rest of the file.
        Assert.Equal(bad, text);
    }

    [Fact]
    public async Task FormatChangedDocuments_DoesNotTouchUnchangedDocuments()
    {
        var (current, docId) = NewDoc("class A { }\n");

        var formatted = await CodeActionService.FormatChangedDocumentsAsync(current, current, CancellationToken.None);

        Assert.Same(current, formatted);
    }
}

public sealed class MergeNonOverlappingTests
{
    [Fact]
    public void OrdersByPosition_DropsDuplicates_AndSkipsOverlaps()
    {
        var c = new TextChange(new TextSpan(5, 1), "C");
        var a = new TextChange(new TextSpan(10, 3), "AAA");
        var aDup = new TextChange(new TextSpan(10, 3), "AAA");
        var b = new TextChange(new TextSpan(20, 2), "BB");
        var overlapsB = new TextChange(new TextSpan(21, 2), "XX");

        var merged = CodeActionService.MergeNonOverlapping([b, a, aDup, overlapsB, c]);

        // Sorted ascending; the duplicate of 'a' collapsed; 'overlapsB' dropped because it overlaps 'b'.
        Assert.Equal(3, merged.Length);
        Assert.Equal([new TextSpan(5, 1), new TextSpan(10, 3), new TextSpan(20, 2)],
            merged.Select(m => m.Span));
        Assert.Equal(["C", "AAA", "BB"], merged.Select(m => m.NewText));
    }

    [Fact]
    public void AdjacentChanges_AreBothKept()
    {
        var a = new TextChange(new TextSpan(0, 2), "AA");
        var b = new TextChange(new TextSpan(2, 2), "BB"); // starts exactly where a ends — not overlapping

        var merged = CodeActionService.MergeNonOverlapping([a, b]);

        Assert.Equal(2, merged.Length);
    }

    [Fact]
    public void MergedChanges_ApplyCleanly_ToSourceText()
    {
        var text = SourceText.From("0123456789");
        var merged = CodeActionService.MergeNonOverlapping(
        [
            new TextChange(new TextSpan(8, 1), "H"),
            new TextChange(new TextSpan(2, 1), "B"),
        ]);

        // The whole point of the merge: WithChanges never sees overlapping spans, so this does not throw.
        // '2'→"B" and '8'→"H".
        Assert.Equal("01B34567H9", text.WithChanges(merged).ToString());
    }
}
