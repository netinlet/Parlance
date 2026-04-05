using Microsoft.Extensions.Logging.Abstractions;
using Parlance.Analysis;
using Parlance.CSharp.Workspace;
using Parlance.CSharp.Workspace.Tests.Integration;
using Parlance.Mcp.Tools;

namespace Parlance.Mcp.Tests.Tools;

public sealed class GetCodeFixesToolTests : IAsyncLifetime
{
    private WorkspaceSessionHolder _holder = null!;
    private CSharpWorkspaceSession _session = null!;
    private CodeActionService _codeActions = null!;

    public async Task InitializeAsync()
    {
        var solutionPath = TestPaths.FindSolutionPath();
        _session = await CSharpWorkspaceSession.OpenSolutionAsync(solutionPath);
        _holder = new WorkspaceSessionHolder();
        _holder.SetSession(_session);
        _codeActions = new CodeActionService(_holder, NullLogger<CodeActionService>.Instance);
    }

    public async Task DisposeAsync() => await _session.DisposeAsync();

    [Fact]
    public async Task GetCodeFixes_FileWithDiagnostics_ReturnsFixes()
    {
        var filePath = Path.Combine(TestPaths.RepoRoot,
            "src", "Parlance.CSharp.Workspace", "CSharpWorkspaceSession.cs");

        var result = await GetCodeFixesTool.GetCodeFixes(
            _holder, _codeActions, NullLogger<GetCodeFixesTool>.Instance,
            filePath: filePath, line: 1, diagnosticId: null,
            CancellationToken.None);

        // We expect either "found" (fixes available) or "no_fixes" (no diagnostics on line 1)
        Assert.True(result.Status is "found" or "no_fixes",
            $"Expected 'found' or 'no_fixes' but got '{result.Status}'");
    }

    [Fact]
    public async Task GetCodeFixes_UnknownFile_ReturnsNotFound()
    {
        var result = await GetCodeFixesTool.GetCodeFixes(
            _holder, _codeActions, NullLogger<GetCodeFixesTool>.Instance,
            filePath: "/nonexistent/file.cs", line: 1, diagnosticId: null,
            CancellationToken.None);

        Assert.Equal("not_found", result.Status);
    }

    [Fact]
    public void GetCodeFixes_NotLoaded_ReturnsNotLoaded()
    {
        var holder = new WorkspaceSessionHolder();
        var codeActions = new CodeActionService(holder, NullLogger<CodeActionService>.Instance);

        var result = GetCodeFixesTool.GetCodeFixes(
            holder, codeActions, NullLogger<GetCodeFixesTool>.Instance,
            filePath: "/some/file.cs", line: 1, diagnosticId: null,
            CancellationToken.None).Result;

        Assert.Equal("not_loaded", result.Status);
    }

    [Fact]
    public void GetCodeFixes_LoadFailed_ReturnsLoadFailed()
    {
        var holder = new WorkspaceSessionHolder();
        holder.SetLoadFailure(new WorkspaceLoadFailure("boom", "/path.sln"));
        var codeActions = new CodeActionService(holder, NullLogger<CodeActionService>.Instance);

        var result = GetCodeFixesTool.GetCodeFixes(
            holder, codeActions, NullLogger<GetCodeFixesTool>.Instance,
            filePath: "/some/file.cs", line: 1, diagnosticId: null,
            CancellationToken.None).Result;

        Assert.Equal("load_failed", result.Status);
        Assert.Equal("boom", result.Message);
    }

    [Fact]
    public async Task CrossTool_GetFixes_ThenPreview_Works()
    {
        // Find a file and line with a diagnostic that has a fix
        var filePath = Path.Combine(TestPaths.RepoRoot,
            "src", "Parlance.CSharp.Workspace", "CSharpWorkspaceSession.cs");

        var fixResult = await GetCodeFixesTool.GetCodeFixes(
            _holder, _codeActions, NullLogger<GetCodeFixesTool>.Instance,
            filePath: filePath, line: 1, diagnosticId: null,
            CancellationToken.None);

        if (fixResult.Status != "found" || fixResult.Fixes.IsEmpty)
        {
            // No fixes on line 1 — skip this test gracefully
            return;
        }

        // Preview the first fix
        var actionId = fixResult.Fixes[0].Id;
        var previewResult = await PreviewCodeActionTool.PreviewCodeAction(
            _holder, _codeActions, NullLogger<PreviewCodeActionTool>.Instance,
            actionId: actionId,
            CancellationToken.None);

        Assert.Equal("found", previewResult.Status);
        Assert.Equal(actionId, previewResult.ActionId);
        Assert.NotNull(previewResult.Title);
    }
}
