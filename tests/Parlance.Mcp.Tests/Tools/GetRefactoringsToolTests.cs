using Microsoft.Extensions.Logging.Abstractions;
using Parlance.Analysis;
using Parlance.CSharp.Workspace;
using Parlance.CSharp.Workspace.Tests.Integration;
using Parlance.Mcp.Tools;

namespace Parlance.Mcp.Tests.Tools;

public sealed class GetRefactoringsToolTests : IAsyncLifetime
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
    public async Task GetRefactorings_AtMethodBody_ReturnsResult()
    {
        var filePath = Path.Combine(TestPaths.RepoRoot,
            "src", "Parlance.CSharp.Workspace", "WorkspaceQueryService.cs");
        var lines = await File.ReadAllLinesAsync(filePath);

        // Find a line with actual code
        var codeLine = Array.FindIndex(lines, l => l.Contains("FindDeclarationsAsync"));
        Assert.True(codeLine >= 0, "Could not find a code line");

        var codeCol = lines[codeLine].IndexOf("FindDeclarationsAsync", StringComparison.Ordinal);

        var result = await GetRefactoringsTool.GetRefactorings(
            _holder, _codeActions, NullLogger<GetRefactoringsTool>.Instance,
            filePath: filePath, line: codeLine + 1, column: codeCol + 1,
            endLine: null, endColumn: null,
            CancellationToken.None);

        // May or may not have refactorings depending on loaded providers
        Assert.True(result.Status is "found" or "no_refactorings",
            $"Expected 'found' or 'no_refactorings' but got '{result.Status}'");
    }

    [Fact]
    public async Task GetRefactorings_UnknownFile_ReturnsNotFound()
    {
        var result = await GetRefactoringsTool.GetRefactorings(
            _holder, _codeActions, NullLogger<GetRefactoringsTool>.Instance,
            filePath: "/nonexistent/file.cs", line: 1, column: 1,
            endLine: null, endColumn: null,
            CancellationToken.None);

        Assert.Equal("not_found", result.Status);
    }

    [Fact]
    public void GetRefactorings_NotLoaded_ReturnsNotLoaded()
    {
        var holder = new WorkspaceSessionHolder();
        var codeActions = new CodeActionService(holder, NullLogger<CodeActionService>.Instance);

        var result = GetRefactoringsTool.GetRefactorings(
            holder, codeActions, NullLogger<GetRefactoringsTool>.Instance,
            filePath: "/some/file.cs", line: 1, column: 1,
            endLine: null, endColumn: null,
            CancellationToken.None).Result;

        Assert.Equal("not_loaded", result.Status);
    }

    [Fact]
    public void GetRefactorings_LoadFailed_ReturnsLoadFailed()
    {
        var holder = new WorkspaceSessionHolder();
        holder.SetLoadFailure(new WorkspaceLoadFailure("boom", "/path.sln"));
        var codeActions = new CodeActionService(holder, NullLogger<CodeActionService>.Instance);

        var result = GetRefactoringsTool.GetRefactorings(
            holder, codeActions, NullLogger<GetRefactoringsTool>.Instance,
            filePath: "/some/file.cs", line: 1, column: 1,
            endLine: null, endColumn: null,
            CancellationToken.None).Result;

        Assert.Equal("load_failed", result.Status);
        Assert.Equal("boom", result.Message);
    }
}
