using System.Collections.Immutable;
using Microsoft.Extensions.Logging.Abstractions;
using Parlance.CSharp.Workspace;
using Parlance.CSharp.Workspace.Tests.Integration;

namespace Parlance.Analysis.Tests;

// Drives the option-gated extract-interface refactoring end-to-end through CodeActionService over a
// committed, purpose-built fixture (Fixtures/RefactorSample.cs) — no scratch dependency. Extract interface
// previously threw (no IExtractInterfaceOptionsService in the headless host); these prove it now resolves
// with defaults and honours agent overrides. ApplyAsync only computes the WorkspaceEdit — nothing is written
// to disk, so the shared read-only session is undisturbed.
[Trait("Category", "Integration")]
public sealed class CodeActionServiceOptionsTests(WorkspaceFixture fixture) : IClassFixture<WorkspaceFixture>
{
    private readonly CodeActionService _service = new(fixture.Holder, NullLogger<CodeActionService>.Instance);

    private static string SampleFile => Path.Combine(
        TestPaths.RepoRoot, "tests", "Parlance.Analysis.Tests", "Fixtures", "RefactorSample.cs");

    private async Task<RefactoringEntry> ExtractInterfaceRefactoring()
    {
        var refactorings = await _service.GetRefactoringsAsync(SampleFile, 7, 14);
        return refactorings.Single(r => r.Title.StartsWith("Extract interface"));
    }

    [Fact]
    public async Task ExtractInterface_Default_ProducesInterfaceNamedAfterType()
    {
        var extract = await ExtractInterfaceRefactoring();

        var edit = await _service.ApplyAsync(extract.Id);

        // Default policy is SameFile: the interface is declared into RefactorSample.cs itself (a document
        // edit), so there is no CreateFile op — only NewFile: true emits a separate file (see the override test).
        Assert.NotNull(edit);
        Assert.Null(edit!.ErrorMessage);
        Assert.Empty(edit.ResourceOperations);
        var documentEdit = Assert.Single(edit.DocumentEdits);
        Assert.EndsWith("RefactorSample.cs", documentEdit.FilePath);
        Assert.Contains(documentEdit.Edits, e => e.NewText.Contains("IRefactorSample"));
    }

    [Fact]
    public async Task ExtractInterface_Override_RenamesInterfaceAndEmitsCreateFile()
    {
        var extract = await ExtractInterfaceRefactoring();

        var edit = await _service.ApplyAsync(extract.Id,
            new RefactoringOptions(InterfaceName: "IRenamed", NewFile: true));

        Assert.NotNull(edit);
        Assert.Null(edit!.ErrorMessage);
        Assert.Contains(edit.ResourceOperations,
            op => op is ResourceOperation.CreateFile c && c.FilePath.EndsWith("IRenamed.cs"));
    }

    [Fact]
    public async Task ExtractInterface_UnknownMember_ReturnsFailedWithMessage()
    {
        var extract = await ExtractInterfaceRefactoring();

        var edit = await _service.ApplyAsync(extract.Id,
            new RefactoringOptions(Members: ImmutableList.Create("Nope")));

        Assert.NotNull(edit);
        Assert.Contains("Nope", edit!.ErrorMessage);
    }
}
