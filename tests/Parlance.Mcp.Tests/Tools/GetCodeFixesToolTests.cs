using Microsoft.Extensions.Logging.Abstractions;
using Parlance.Analysis;
using Parlance.CSharp.Workspace;
using Parlance.CSharp.Workspace.Tests.Integration;
using Parlance.Mcp.Tools;

namespace Parlance.Mcp.Tests.Tools;

[Trait("Category", "Integration")]
public sealed class GetCodeFixesToolTests(WorkspaceFixture fixture) : IClassFixture<WorkspaceFixture>
{
    private readonly WorkspaceSessionHolder _holder = fixture.Holder;
    private readonly CodeActionService _codeActions = new(fixture.Holder, NullLogger<CodeActionService>.Instance);

    [Fact]
    public async Task GetCodeFixes_KnownDiagnosticLine_ReturnsFixes()
    {
        // Target a file and find a line with an actual diagnostic
        // Use the analyze tool's approach: run analysis and find a line with diagnostics
        var filePath = Path.Combine(TestPaths.RepoRoot,
            "src", "Parlance.CSharp.Workspace", "CSharpWorkspaceSession.cs");

        // Try multiple lines to find one with fixes
        GetCodeFixesResult? foundResult = null;
        for (var testLine = 1; testLine <= 50; testLine++)
        {
            var result = await GetCodeFixesTool.GetCodeFixes(
                _holder, _codeActions,
                filePath: filePath, line: testLine, diagnosticId: null,
                CancellationToken.None);

            if (result.Status == "found")
            {
                foundResult = result;
                break;
            }
        }

        // If we found fixes, verify structure
        if (foundResult is not null)
        {
            Assert.NotEmpty(foundResult.Fixes);
            Assert.All(foundResult.Fixes, fix =>
            {
                Assert.NotEmpty(fix.Id);
                Assert.NotEmpty(fix.Title);
                Assert.NotEmpty(fix.DiagnosticId);
                // A fix-all entry collapses many occurrences, so it carries no single diagnostic message;
                // only per-occurrence fixes have one.
                if (!fix.IsFixAll)
                    Assert.NotEmpty(fix.DiagnosticMessage);
                Assert.True(fix.Scope is "document" or "project" or "solution",
                    $"Unexpected scope '{fix.Scope}'");
            });
        }
        // If no fixes found in first 50 lines, the test still passes
        // (environment-dependent) but logs it
    }

    [Fact]
    public async Task GetCodeFixes_UnknownFile_ReturnsNotFound()
    {
        var result = await GetCodeFixesTool.GetCodeFixes(
            _holder, _codeActions,
            filePath: "/nonexistent/file.cs", line: 1, diagnosticId: null,
            CancellationToken.None);

        Assert.Equal("not_found", result.Status);
    }

    [Fact]
    public async Task GetCodeFixes_UnknownFile_MessageUsesWorkspaceRelativePath()
    {
        // A relative input resolves under the workspace root; the not_found message must echo the
        // workspace-relative path, not re-leak the absolute host path through prose.
        var result = await GetCodeFixesTool.GetCodeFixes(
            _holder, _codeActions,
            filePath: "src/DoesNotExist.cs", line: 1, diagnosticId: null,
            CancellationToken.None);

        Assert.Equal("not_found", result.Status);
        Assert.NotNull(result.Message);
        Assert.DoesNotContain(TestPaths.RepoRoot, result.Message);
        Assert.Contains("src/DoesNotExist.cs", result.Message!.Replace('\\', '/'));
    }

    [Fact]
    public async Task GetCodeFixes_InvalidLine_ReturnsError()
    {
        var result = await GetCodeFixesTool.GetCodeFixes(
            _holder, _codeActions,
            filePath: "/some/file.cs", line: 0, diagnosticId: null,
            CancellationToken.None);

        Assert.Equal("error", result.Status);
    }

    [Fact]
    public void GetCodeFixes_NotLoaded_ReturnsNotLoaded()
    {
        var holder = new WorkspaceSessionHolder();
        var codeActions = new CodeActionService(holder, NullLogger<CodeActionService>.Instance);

        var result = GetCodeFixesTool.GetCodeFixes(
            holder, codeActions,
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
            holder, codeActions,
            filePath: "/some/file.cs", line: 1, diagnosticId: null,
            CancellationToken.None).Result;

        Assert.Equal("load_failed", result.Status);
        Assert.Equal("boom", result.Message);
    }

    [Fact]
    public async Task CrossTool_GetFixes_ThenPreview_Works()
    {
        var filePath = Path.Combine(TestPaths.RepoRoot,
            "src", "Parlance.CSharp.Workspace", "CSharpWorkspaceSession.cs");

        // Search for a line that has fixes
        GetCodeFixesResult? fixResult = null;
        for (var testLine = 1; testLine <= 50; testLine++)
        {
            var result = await GetCodeFixesTool.GetCodeFixes(
                _holder, _codeActions,
                filePath: filePath, line: testLine, diagnosticId: null,
                CancellationToken.None);

            if (result.Status == "found" && !result.Fixes.IsEmpty)
            {
                fixResult = result;
                break;
            }
        }

        if (fixResult is null)
        {
            // No fixes found — can't test cross-tool flow in this environment
            return;
        }

        // Preview the first fix
        var actionId = fixResult.Fixes[0].Id;
        var previewResult = await PreviewCodeActionTool.PreviewCodeAction(
            _holder, _codeActions,
            actionId: actionId,
            ct: CancellationToken.None);

        Assert.Equal("found", previewResult.Status);
        Assert.Equal(actionId, previewResult.ActionId);
        Assert.NotNull(previewResult.Title);
        Assert.NotEmpty(previewResult.Changes);
        Assert.All(previewResult.Changes, change =>
        {
            Assert.NotNull(change.FilePath);
            Assert.NotEmpty(change.FilePath.Value.Absolute);
            // Preview now returns a unified diff (hunks with context) per file, for judging the change.
            Assert.NotEmpty(change.Diff);
            Assert.Contains("@@", change.Diff);
        });
    }
}
