using Parlance.Abstractions;
using Parlance.CSharp.Workspace.Tests.Integration;
using Parlance.Mcp.Tools;

namespace Parlance.Mcp.Tests.Tools;

/// <summary>
/// Guards the tool-chaining contract: tool output serializes paths workspace-relative (RepoPath), so a
/// client naturally feeds a relative value from one tool's result into the next tool's file argument.
/// Every file-input tool must normalize that relative form back to absolute before Roslyn lookup.
/// </summary>
[Trait("Category", "Integration")]
public sealed class InputPathNormalizationTests(WorkspaceFixture fixture) : IClassFixture<WorkspaceFixture>
{
    private string SerializedRepoPathOfAnyDocument()
    {
        var absolute = fixture.Session.CurrentSolution.Projects
            .SelectMany(p => p.Documents)
            .Select(d => d.FilePath)
            .First(p => p is not null)!;

        // The exact string a client receives from any tool's serialized RepoPath output.
        var relative = new RepoPath(absolute).Relative(fixture.Session.Root);
        Assert.NotEqual(absolute, relative); // sanity: it really is the relative form
        return relative;
    }

    [Fact]
    public async Task SerializedRepoPath_FedIntoOutlineFile_Resolves()
    {
        var relative = SerializedRepoPathOfAnyDocument();

        var result = await OutlineFileTool.OutlineFile(
            fixture.Holder, fixture.Query, relative, CancellationToken.None);

        Assert.Equal("found", result.Status); // not "not_found": the relative path resolved
    }

    [Fact]
    public async Task SerializedRepoPath_FedIntoGotoDefinitionByPosition_ResolvesTheFile()
    {
        var relative = SerializedRepoPathOfAnyDocument();

        var result = await GotoDefinitionTool.GotoDefinition(
            fixture.Holder, fixture.Query,
            symbolName: null, filePath: relative, line: 1, column: 1,
            CancellationToken.None);

        // The file resolved; an "error" here would mean the position-lookup path was rejected outright.
        Assert.NotEqual("error", result.Status);
    }
}
