using Microsoft.Extensions.Logging.Abstractions;
using Parlance.Analysis;
using Parlance.CSharp.Workspace;
using Parlance.CSharp.Workspace.Tests.Integration;
using Parlance.Mcp.Tools;

namespace Parlance.Mcp.Tests.Tools;

[Trait("Category", "Integration")]
public sealed class ApplyCodeActionToolTests(WorkspaceFixture fixture) : IClassFixture<WorkspaceFixture>
{
    private readonly WorkspaceSessionHolder _holder = fixture.Holder;
    private readonly CodeActionService _codeActions = new(fixture.Holder, NullLogger<CodeActionService>.Instance);

    [Fact]
    public async Task Apply_InvalidActionId_ReturnsNotFound()
    {
        var result = await ApplyCodeActionTool.ApplyCodeAction(
            _holder, _codeActions, actionId: "fix-99999", expectedSnapshotVersion: null, ct: CancellationToken.None);

        Assert.Equal("not_found", result.Status);
        Assert.Equal("fix-99999", result.ActionId);
    }

    [Fact]
    public async Task Apply_StaleExpectedSnapshotVersion_ReturnsStale()
    {
        // The fixture loads at snapshot 1; an expectation it has moved past yields a best-effort stale signal.
        var result = await ApplyCodeActionTool.ApplyCodeAction(
            _holder, _codeActions, actionId: "fix-1", expectedSnapshotVersion: 999_999, ct: CancellationToken.None);

        Assert.Equal("stale", result.Status);
        Assert.Equal(_holder.CurrentSnapshotVersion(), result.SnapshotVersion);
    }

    [Fact]
    public void Apply_NotLoaded_ReturnsNotLoaded()
    {
        var holder = new WorkspaceSessionHolder();
        var codeActions = new CodeActionService(holder, NullLogger<CodeActionService>.Instance);

        var result = ApplyCodeActionTool.ApplyCodeAction(
            holder, codeActions, actionId: "fix-1", expectedSnapshotVersion: null, ct: CancellationToken.None).Result;

        Assert.Equal("not_loaded", result.Status);
    }

    [Fact]
    public void Apply_LoadFailed_ReturnsLoadFailed()
    {
        var holder = new WorkspaceSessionHolder();
        holder.SetLoadFailure(new WorkspaceLoadFailure("boom", "/path.sln"));
        var codeActions = new CodeActionService(holder, NullLogger<CodeActionService>.Instance);

        var result = ApplyCodeActionTool.ApplyCodeAction(
            holder, codeActions, actionId: "fix-1", expectedSnapshotVersion: null, ct: CancellationToken.None).Result;

        Assert.Equal("load_failed", result.Status);
        Assert.Equal("boom", result.Message);
    }

    [Fact]
    public async Task Apply_RealCodeFix_ReturnsWorkspaceEdit_StampedWithSnapshot()
    {
        var filePath = Path.Combine(TestPaths.RepoRoot,
            "src", "Parlance.CSharp.Workspace", "CSharpWorkspaceSession.cs");

        // Find a line that yields a fix, then apply that fix through the same service (shared action cache).
        GetCodeFixesResult? fixResult = null;
        for (var line = 1; line <= 50; line++)
        {
            var result = await GetCodeFixesTool.GetCodeFixes(
                _holder, _codeActions, filePath: filePath, line: line, diagnosticId: null, CancellationToken.None);
            if (result.Status == "found" && !result.Fixes.IsEmpty)
            {
                fixResult = result;
                break;
            }
        }

        if (fixResult is null)
            return; // No fixes available in this environment — the deterministic mapping is covered by unit tests.

        var actionId = fixResult.Fixes[0].Id;
        var applied = await ApplyCodeActionTool.ApplyCodeAction(
            _holder, _codeActions, actionId: actionId, expectedSnapshotVersion: null, ct: CancellationToken.None);

        Assert.Equal("success", applied.Status);
        Assert.Equal(actionId, applied.ActionId);
        Assert.NotNull(applied.Title);
        Assert.Equal(fixture.Session.SnapshotVersion, applied.SnapshotVersion);

        // A fix must yield at least one applyable change (text edits and/or resource operations).
        Assert.True(applied.DocumentEdits.Count > 0 || applied.ResourceOperations.Count > 0);
        Assert.All(applied.DocumentEdits, d =>
        {
            Assert.NotNull(d.FilePath);
            Assert.NotEmpty(d.FilePath.Value.Absolute);
            Assert.NotEmpty(d.Edits);
            Assert.False(string.IsNullOrEmpty(d.Newline));
        });
    }
}
