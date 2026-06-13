using Microsoft.Extensions.Logging.Abstractions;
using Parlance.Analysis;
using Parlance.Analysis.Tests;
using Parlance.CSharp.Workspace;
using Parlance.CSharp.Workspace.Tests.Integration;
using Parlance.Mcp.Tools;

namespace Parlance.Mcp.Tests.Tools;

// Proves the MCP apply-code-action `options` param reaches the domain: an extract-interface action applied
// with InterfaceName/NewFile overrides emits a create op for the renamed file. Targets the committed
// RefactorSample fixture (part of the loaded solution); ApplyCodeAction only computes the edit, so nothing
// is written to disk.
[Trait("Category", "Integration")]
public sealed class ApplyCodeActionToolOptionsTests(WorkspaceFixture fixture) : IClassFixture<WorkspaceFixture>
{
    private readonly WorkspaceSessionHolder _holder = fixture.Holder;
    private readonly CodeActionService _codeActions = new(fixture.Holder, AnalyzerProviderTestFactory.CreateWithBundled(), NullLogger<CodeActionService>.Instance);

    private static string SampleFile => Path.Combine(
        TestPaths.RepoRoot, "tests", "Parlance.Analysis.Tests", "Fixtures", "RefactorSample.cs");

    [Fact]
    public async Task ApplyCodeAction_WithInterfaceNameOption_RenamesExtractedInterface()
    {
        // Populate the shared action cache, then resolve the extract-interface action id.
        var refactorings = await _codeActions.GetRefactoringsAsync(SampleFile, 7, 14);
        var extract = refactorings.Single(r => r.Title.StartsWith("Extract interface"));

        var result = await ApplyCodeActionTool.ApplyCodeAction(
            _holder, _codeActions, extract.Id,
            options: new RefactoringOptionsInput(InterfaceName: "IRenamed", NewFile: true));

        Assert.Equal("success", result.Status);
        Assert.Contains(result.ResourceOperations,
            op => op.Kind == "create" && op.FilePath!.Value.Absolute.EndsWith("IRenamed.cs"));
    }
}
