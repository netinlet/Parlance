using Microsoft.Extensions.Logging.Abstractions;
using Parlance.Analysis;
using Parlance.CSharp.Workspace;
using Parlance.CSharp.Workspace.Tests.Integration;
using Parlance.Mcp.Tools;

namespace Parlance.Mcp.Tests.Tools;

// Proves the MCP apply-code-action `options` param reaches the domain: an extract-interface action
// applied with InterfaceName/NewFile overrides emits a create op for the renamed file. Loads the
// scratch/LiveRefactor solution (which holds LineItem.cs); ApplyCodeAction only computes the edit, so
// nothing is written to disk.
[Trait("Category", "Integration")]
public sealed class ApplyCodeActionToolOptionsTests : IAsyncLifetime
{
    private CSharpWorkspaceSession _session = null!;
    private WorkspaceSessionHolder _holder = null!;
    private CodeActionService _codeActions = null!;

    private static string LiveRefactorSolution => Path.Combine(
        TestPaths.RepoRoot, "scratch", "LiveRefactor", "LiveRefactor.slnx");

    private static string LineItemFile => Path.Combine(
        TestPaths.RepoRoot, "scratch", "LiveRefactor", "LineItem.cs");

    public async Task InitializeAsync()
    {
        _session = Assert.IsType<WorkspaceLoadResult.Success>(
            await CSharpWorkspaceSession.TryOpenSolutionAsync(LiveRefactorSolution)).Session;
        _holder = new WorkspaceSessionHolder();
        _holder.SetSession(_session);
        _codeActions = new CodeActionService(_holder, NullLogger<CodeActionService>.Instance);
    }

    public async Task DisposeAsync() => await _session.DisposeAsync();

    [Fact]
    public async Task ApplyCodeAction_WithInterfaceNameOption_RenamesExtractedInterface()
    {
        // Populate the shared action cache, then resolve the extract-interface action id.
        var refactorings = await _codeActions.GetRefactoringsAsync(LineItemFile, 7, 14);
        var extract = refactorings.Single(r => r.Title.StartsWith("Extract interface"));

        var result = await ApplyCodeActionTool.ApplyCodeAction(
            _holder, _codeActions, extract.Id,
            options: new RefactoringOptionsInput(InterfaceName: "IInvoiceLine", NewFile: true));

        Assert.Equal("success", result.Status);
        Assert.Contains(result.ResourceOperations,
            op => op.Kind == "create" && op.FilePath!.Value.Absolute.EndsWith("IInvoiceLine.cs"));
    }
}
