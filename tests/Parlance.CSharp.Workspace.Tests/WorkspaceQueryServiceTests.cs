using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using Parlance.CSharp.Workspace.Tests.Integration;

namespace Parlance.CSharp.Workspace.Tests;

public sealed class WorkspaceQueryServiceTests : IAsyncLifetime
{
    private CSharpWorkspaceSession _session = null!;
    private WorkspaceQueryService _query = null!;

    public async Task InitializeAsync()
    {
        var solutionPath = TestPaths.FindSolutionPath();
        _session = await CSharpWorkspaceSession.OpenSolutionAsync(solutionPath);
        var holder = new WorkspaceSessionHolder();
        holder.SetSession(_session);
        _query = new WorkspaceQueryService(holder, NullLogger<WorkspaceQueryService>.Instance);
    }

    public async Task DisposeAsync() => await _session.DisposeAsync();

    [Fact]
    public async Task FindSymbolsAsync_FindsTypeByName()
    {
        var results = await _query.FindSymbolsAsync("CSharpWorkspaceSession", SymbolFilter.Type);
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.Symbol.Name == "CSharpWorkspaceSession");
    }

    [Fact]
    public async Task FindSymbolsAsync_SolutionTypesRankFirst()
    {
        // "Diagnostic" exists in both Parlance.Abstractions and Microsoft.CodeAnalysis
        var results = await _query.FindSymbolsAsync("Diagnostic", SymbolFilter.Type);
        Assert.NotEmpty(results);
        var first = results[0];
        Assert.Contains("Parlance", first.Symbol.ContainingNamespace.ToDisplayString());
    }

    [Fact]
    public async Task FindSymbolsAsync_ReturnsProjectContext()
    {
        var results = await _query.FindSymbolsAsync("CSharpWorkspaceSession", SymbolFilter.Type);
        var match = results.First(r => r.Symbol.Name == "CSharpWorkspaceSession");
        Assert.Equal("Parlance.CSharp.Workspace", match.Project.Name);
    }

    [Fact]
    public async Task FindSymbolsAsync_EmptyForNonexistentType()
    {
        var results = await _query.FindSymbolsAsync("ThisTypeDoesNotExist12345", SymbolFilter.Type);
        Assert.Empty(results);
    }

    [Fact]
    public async Task GetCompilationAsync_ByName_ReturnsCompilation()
    {
        var compilation = await _query.GetCompilationAsync("Parlance.CSharp.Workspace");
        Assert.NotNull(compilation);
    }

    [Fact]
    public async Task GetCompilationAsync_ByName_ReturnsNullForUnknown()
    {
        var compilation = await _query.GetCompilationAsync("NonExistentProject");
        Assert.Null(compilation);
    }

    [Fact]
    public async Task GetCompilationsAsync_YieldsAllProjects()
    {
        var count = 0;
        await foreach (var (project, compilation) in _query.GetCompilationsAsync())
        {
            Assert.NotNull(compilation);
            count++;
        }
        Assert.True(count > 0);
    }

    [Fact]
    public async Task GetSemanticModelAsync_ReturnsModelForKnownFile()
    {
        var sessionFile = Path.Combine(TestPaths.RepoRoot,
            "src", "Parlance.CSharp.Workspace", "CSharpWorkspaceSession.cs");
        var model = await _query.GetSemanticModelAsync(sessionFile);
        Assert.NotNull(model);
    }

    [Fact]
    public async Task GetSemanticModelAsync_ReturnsNullForUnknownFile()
    {
        var model = await _query.GetSemanticModelAsync("/nonexistent/file.cs");
        Assert.Null(model);
    }

    [Fact]
    public async Task FindImplementationsAsync_FindsConcreteTypes()
    {
        var symbols = await _query.FindSymbolsAsync("IAnalysisEngine", SymbolFilter.Type);
        Assert.NotEmpty(symbols);
        var iface = symbols[0].Symbol;

        var implementations = await _query.FindImplementationsAsync(iface);
        Assert.NotEmpty(implementations);
    }

    [Fact]
    public async Task FindReferencesAsync_FindsUsages()
    {
        var symbols = await _query.FindSymbolsAsync("CSharpWorkspaceSession", SymbolFilter.Type);
        Assert.NotEmpty(symbols);

        var references = await _query.FindReferencesAsync(symbols[0].Symbol);
        Assert.NotEmpty(references);
    }

    [Fact]
    public async Task GetSymbolAtPositionAsync_ResolvesType()
    {
        // Find the class declaration line dynamically
        var sessionFile = Path.Combine(TestPaths.RepoRoot,
            "src", "Parlance.CSharp.Workspace", "CSharpWorkspaceSession.cs");
        var lines = await File.ReadAllLinesAsync(sessionFile);
        var classLine = Array.FindIndex(lines, l => l.Contains("class CSharpWorkspaceSession"));
        Assert.True(classLine >= 0, "Could not find class declaration");

        var classCol = lines[classLine].IndexOf("CSharpWorkspaceSession", StringComparison.Ordinal);
        var symbol = await _query.GetSymbolAtPositionAsync(sessionFile, classLine, classCol);
        Assert.NotNull(symbol);
        Assert.Equal("CSharpWorkspaceSession", symbol.Name);
    }
}
