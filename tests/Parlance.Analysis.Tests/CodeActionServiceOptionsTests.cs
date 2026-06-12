using System.Collections.Immutable;
using Microsoft.Extensions.Logging.Abstractions;
using Parlance.CSharp.Workspace;
using Parlance.CSharp.Workspace.Tests.Integration;

namespace Parlance.Analysis.Tests;

// Drives the option-gated refactorings end-to-end over the scratch/LiveRefactor solution: Extract
// interface previously threw (no IExtractInterfaceOptionsService in the headless host); these prove it
// now resolves with defaults and honours agent overrides. ApplyAsync only computes the WorkspaceEdit —
// nothing is written to disk, so the scratch .orig files are undisturbed.
[Trait("Category", "Integration")]
public sealed class CodeActionServiceOptionsTests : IAsyncLifetime
{
    private CSharpWorkspaceSession _session = null!;
    private CodeActionService _service = null!;

    private static string LiveRefactorSolution => Path.Combine(
        TestPaths.RepoRoot, "scratch", "LiveRefactor", "LiveRefactor.slnx");

    private static string LineItemFile => Path.Combine(
        TestPaths.RepoRoot, "scratch", "LiveRefactor", "LineItem.cs");

    public async Task InitializeAsync()
    {
        _session = Assert.IsType<WorkspaceLoadResult.Success>(
            await CSharpWorkspaceSession.TryOpenSolutionAsync(LiveRefactorSolution)).Session;
        var holder = new WorkspaceSessionHolder();
        holder.SetSession(_session);
        _service = new CodeActionService(holder, NullLogger<CodeActionService>.Instance);
    }

    public async Task DisposeAsync() => await _session.DisposeAsync();

    private async Task<RefactoringEntry> ExtractInterfaceRefactoring()
    {
        var refactorings = await _service.GetRefactoringsAsync(LineItemFile, 7, 14);
        return refactorings.Single(r => r.Title.StartsWith("Extract interface"));
    }

    [Fact]
    public async Task ExtractInterface_Default_ProducesInterfaceNamedAfterType()
    {
        var extract = await ExtractInterfaceRefactoring();

        var edit = await _service.ApplyAsync(extract.Id);

        // Default policy is SameFile: the interface is declared into LineItem.cs itself (a document edit),
        // so there is no CreateFile op — only NewFile: true emits a separate file (see the override test).
        Assert.NotNull(edit);
        Assert.Null(edit!.ErrorMessage);
        Assert.Empty(edit.ResourceOperations);
        var documentEdit = Assert.Single(edit.DocumentEdits);
        Assert.EndsWith("LineItem.cs", documentEdit.FilePath);
        Assert.Contains(documentEdit.Edits, e => e.NewText.Contains("ILineItem"));
    }

    [Fact]
    public async Task ExtractInterface_Override_RenamesInterface()
    {
        var extract = await ExtractInterfaceRefactoring();

        var edit = await _service.ApplyAsync(extract.Id,
            new RefactoringOptions(InterfaceName: "IInvoiceLine", NewFile: true));

        Assert.NotNull(edit);
        Assert.Null(edit!.ErrorMessage);
        Assert.Contains(edit.ResourceOperations,
            op => op is ResourceOperation.CreateFile c && c.FilePath.EndsWith("IInvoiceLine.cs"));
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
