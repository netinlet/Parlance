using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Parlance.Analysis.Tests;

public sealed class WorkspaceEditBuilderTests
{
    private const string RepoDir = "/repo/";

    private static (Solution Solution, ProjectId ProjectId) NewProject()
    {
        var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var solution = workspace.CurrentSolution.AddProject(
            ProjectInfo.Create(projectId, VersionStamp.Create(), "P", "P", LanguageNames.CSharp));
        return (solution, projectId);
    }

    private static (Solution Solution, DocumentId DocId) WithDocument(
        Solution solution, ProjectId projectId, string name, string text, Encoding? encoding = null)
    {
        var docId = DocumentId.CreateNewId(projectId);
        var source = SourceText.From(text, encoding ?? Encoding.UTF8);
        return (solution.AddDocument(docId, name, source, filePath: RepoDir + name), docId);
    }

    private static Task<CodeActionEdit> Build(Solution current, Solution changed, Func<string, long?>? buffer = null) =>
        WorkspaceEditBuilder.BuildAsync("fix-1", "T", current, changed, buffer ?? (_ => null), CancellationToken.None);

    // Applies a DocumentEdit's text edits to the original source the way an agent would, exercising the
    // line/column ranges round-trip. Non-overlapping ascending edits are applied last-to-first so earlier
    // offsets stay valid.
    private static string Apply(string original, IEnumerable<TextEdit> edits)
    {
        var text = SourceText.From(original);
        foreach (var e in edits.OrderByDescending(e => e.Range.StartLine).ThenByDescending(e => e.Range.StartColumn))
        {
            var start = text.Lines.GetPosition(new LinePosition(e.Range.StartLine - 1, e.Range.StartColumn - 1));
            var end = text.Lines.GetPosition(new LinePosition(e.Range.EndLine - 1, e.Range.EndColumn - 1));
            text = text.WithChanges(new TextChange(TextSpan.FromBounds(start, end), e.NewText));
        }
        return text.ToString();
    }

    [Fact]
    public async Task SingleFileTextChange_ProducesOneDocumentEdit_NoResourceOps()
    {
        var (s0, project) = NewProject();
        var (current, docId) = WithDocument(s0, project, "A.cs", "class A {}");
        var changed = current.WithDocumentText(docId, SourceText.From("class B {}"));

        var edit = await Build(current, changed);

        Assert.Empty(edit.ResourceOperations);
        var doc = Assert.Single(edit.DocumentEdits);
        Assert.Equal("/repo/A.cs", doc.FilePath);
        Assert.NotEmpty(doc.Edits);
        // The edits' ranges round-trip: applied to the original they reproduce the changed text exactly.
        Assert.Equal("class B {}", Apply("class A {}", doc.Edits));
    }

    [Fact]
    public async Task MultipleChangedFiles_ProduceADocumentEditPerFile()
    {
        var (s0, project) = NewProject();
        var (s1, a) = WithDocument(s0, project, "A.cs", "class A {}");
        var (current, b) = WithDocument(s1, project, "B.cs", "class B {}");

        var changed = current
            .WithDocumentText(a, SourceText.From("class A2 {}"))
            .WithDocumentText(b, SourceText.From("class B2 {}"));

        var edit = await Build(current, changed);

        Assert.Empty(edit.ResourceOperations);
        Assert.Equal(2, edit.DocumentEdits.Count);
        Assert.Contains(edit.DocumentEdits, d => d.FilePath == "/repo/A.cs");
        Assert.Contains(edit.DocumentEdits, d => d.FilePath == "/repo/B.cs");
    }

    [Fact]
    public async Task AddedDocument_ProducesCreateFileResourceOp_WithFullContent()
    {
        var (s0, project) = NewProject();
        var (current, _) = WithDocument(s0, project, "A.cs", "class A {}");

        var newDocId = DocumentId.CreateNewId(project);
        var changed = current.AddDocument(newDocId, "New.cs", SourceText.From("class N {}"), filePath: "/repo/New.cs");

        var edit = await Build(current, changed);

        Assert.Empty(edit.DocumentEdits);
        var op = Assert.IsType<ResourceOperation.CreateFile>(Assert.Single(edit.ResourceOperations));
        Assert.Equal("/repo/New.cs", op.FilePath);
        Assert.Equal("class N {}", op.Content);
    }

    [Fact]
    public async Task RemovedDocument_ProducesDeleteFileResourceOp()
    {
        var (s0, project) = NewProject();
        var (current, docId) = WithDocument(s0, project, "A.cs", "class A {}");

        var changed = current.RemoveDocument(docId);

        var edit = await Build(current, changed);

        var op = Assert.IsType<ResourceOperation.DeleteFile>(Assert.Single(edit.ResourceOperations));
        Assert.Equal("/repo/A.cs", op.FilePath);
    }

    [Fact]
    public async Task RenamedDocument_ProducesRenameFileResourceOp()
    {
        var (s0, project) = NewProject();
        var (current, docId) = WithDocument(s0, project, "A.cs", "class A {}");

        var changed = current.WithDocumentFilePath(docId, "/repo/Renamed.cs");

        var edit = await Build(current, changed);

        var op = Assert.IsType<ResourceOperation.RenameFile>(Assert.Single(edit.ResourceOperations));
        Assert.Equal("/repo/A.cs", op.OldFilePath);
        Assert.Equal("/repo/Renamed.cs", op.NewFilePath);
    }

    [Fact]
    public async Task MoveTypeShape_AddsFileAndEditsOriginal()
    {
        // The shape "move type to its own file" produces: original document edited + a new file created.
        var (s0, project) = NewProject();
        var (current, original) = WithDocument(s0, project, "Pair.cs", "class A {} class B {}");

        var moved = current.WithDocumentText(original, SourceText.From("class A {}"));
        var newDocId = DocumentId.CreateNewId(project);
        var changed = moved.AddDocument(newDocId, "B.cs", SourceText.From("class B {}"), filePath: "/repo/B.cs");

        var edit = await Build(current, changed);

        Assert.Single(edit.DocumentEdits);
        Assert.Equal("/repo/Pair.cs", edit.DocumentEdits[0].FilePath);
        var op = Assert.IsType<ResourceOperation.CreateFile>(Assert.Single(edit.ResourceOperations));
        Assert.Equal("/repo/B.cs", op.FilePath);
        Assert.Equal("class B {}", op.Content);
    }

    [Fact]
    public async Task DocumentEdit_CarriesBufferVersion_WhenOverlayOpen()
    {
        var (s0, project) = NewProject();
        var (current, docId) = WithDocument(s0, project, "A.cs", "class A {}");
        var changed = current.WithDocumentText(docId, SourceText.From("class B {}"));

        var edit = await Build(current, changed, path => path == "/repo/A.cs" ? 7 : null);

        Assert.Equal(7, edit.DocumentEdits[0].DocumentVersion);
    }

    [Fact]
    public async Task DocumentEdit_DetectsCrlfNewline()
    {
        var (s0, project) = NewProject();
        var (current, docId) = WithDocument(s0, project, "A.cs", "class A {}\r\n// tail\r\n");
        var changed = current.WithDocumentText(docId, SourceText.From("class B {}\r\n// tail\r\n"));

        var edit = await Build(current, changed);

        Assert.Equal("\r\n", edit.DocumentEdits[0].Newline);
    }
}

public sealed class SourceTextFactsTests
{
    [Theory]
    [InlineData("a\r\nb\r\n", "\r\n")]
    [InlineData("a\nb\n", "\n")]
    [InlineData("a\rb\r", "\r")]
    [InlineData("no newlines", "\n")]
    [InlineData("", "\n")]
    public void DetectNewline_PicksDominantEnding(string input, string expected) =>
        Assert.Equal(expected, SourceTextFacts.DetectNewline(SourceText.From(input)));

    [Theory]
    [InlineData("x\ny", "\r\n", "x\r\ny")]
    [InlineData("x\r\ny", "\n", "x\ny")]
    [InlineData("x\ry", "\n", "x\ny")]
    [InlineData("no breaks", "\r\n", "no breaks")]
    [InlineData("", "\r\n", "")]
    public void NormalizeNewlines_RewritesEveryEnding(string input, string newline, string expected) =>
        Assert.Equal(expected, SourceTextFacts.NormalizeNewlines(input, newline));

    [Fact]
    public void EncodingName_DefaultsToUtf8_WhenUnknown() =>
        Assert.Equal("utf-8", SourceTextFacts.EncodingName(SourceText.From("x"))); // SourceText.From(string) has no encoding
}
