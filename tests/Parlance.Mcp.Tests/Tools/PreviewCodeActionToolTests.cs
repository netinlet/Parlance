using Microsoft.Extensions.Logging.Abstractions;
using Parlance.Analysis;
using Parlance.CSharp.Workspace;
using Parlance.CSharp.Workspace.Tests.Integration;
using Parlance.Mcp.Tools;

namespace Parlance.Mcp.Tests.Tools;

public sealed class PreviewCodeActionToolTests(WorkspaceFixture fixture) : IClassFixture<WorkspaceFixture>
{
    private readonly WorkspaceSessionHolder _holder = fixture.Holder;
    private readonly CodeActionService _codeActions = new(fixture.Holder, NullLogger<CodeActionService>.Instance);

    [Fact]
    public async Task Preview_InvalidActionId_ReturnsNotFound()
    {
        var result = await PreviewCodeActionTool.PreviewCodeAction(
            _holder, _codeActions,
            actionId: "fix-99999",
            CancellationToken.None);

        Assert.Equal("not_found", result.Status);
        Assert.Equal("fix-99999", result.ActionId);
    }

    [Fact]
    public async Task Preview_MalformedActionId_ReturnsNotFound()
    {
        var result = await PreviewCodeActionTool.PreviewCodeAction(
            _holder, _codeActions,
            actionId: "garbage",
            CancellationToken.None);

        Assert.Equal("not_found", result.Status);
    }

    [Fact]
    public void Preview_NotLoaded_ReturnsNotLoaded()
    {
        var holder = new WorkspaceSessionHolder();
        var codeActions = new CodeActionService(holder, NullLogger<CodeActionService>.Instance);

        var result = PreviewCodeActionTool.PreviewCodeAction(
            holder, codeActions,
            actionId: "fix-1",
            CancellationToken.None).Result;

        Assert.Equal("not_loaded", result.Status);
    }

    [Fact]
    public void Preview_LoadFailed_ReturnsLoadFailed()
    {
        var holder = new WorkspaceSessionHolder();
        holder.SetLoadFailure(new WorkspaceLoadFailure("boom", "/path.sln"));
        var codeActions = new CodeActionService(holder, NullLogger<CodeActionService>.Instance);

        var result = PreviewCodeActionTool.PreviewCodeAction(
            holder, codeActions,
            actionId: "fix-1",
            CancellationToken.None).Result;

        Assert.Equal("load_failed", result.Status);
        Assert.Equal("boom", result.Message);
    }
}
