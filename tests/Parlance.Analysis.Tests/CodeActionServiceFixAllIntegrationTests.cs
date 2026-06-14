using Microsoft.Extensions.Logging.Abstractions;
using Parlance.CSharp.Workspace.Tests.Integration;

namespace Parlance.Analysis.Tests;

// Exercises the document fix-all entry end-to-end (#4) against the real loaded solution. The repo `.editorconfig`
// prefers `var`, so IDE0008 ("use explicit type instead of 'var'") does NOT fire on repo source — now that
// .editorconfig options actually reach the analyzers. Instead this targets a dedicated fixture under
// Fixtures/, whose sibling `.editorconfig` (root = true) pins prefer-explicit-type, making IDE0008 a reliable
// multi-occurrence, FixAll-capable rule there regardless of the repo's own style.
[Trait("Category", "Integration")]
public sealed class CodeActionServiceFixAllIntegrationTests(WorkspaceFixture fixture)
    : IClassFixture<WorkspaceFixture>
{
    private readonly CodeActionService _codeActions = new(fixture.Holder, AnalyzerProviderTestFactory.CreateWithBundled(), NullLogger<CodeActionService>.Instance);

    private static string TargetFile => Path.Combine(
        TestPaths.RepoRoot, "tests", "Parlance.Analysis.Tests", "Fixtures", "VarHeavySample.cs");

    [Fact]
    public async Task GetCodeFixes_RuleFiresMultipleTimes_OffersASingleDocumentFixAll()
    {
        var (line, fixes) = await FindFixesAtFirstVarLineAsync();

        Assert.NotEqual(0, line);
        var fixAlls = fixes.Where(f => f.IsFixAll).ToList();
        // Exactly one collapsed entry for the rule — not one per occurrence.
        var fixAll = Assert.Single(fixAlls);
        Assert.Equal("IDE0008", fixAll.DiagnosticId);
        Assert.Equal("document", fixAll.Scope);
        Assert.Contains("Fix all", fixAll.Title);

        // The per-occurrence entries are deduped: no exact (title) duplicate survives.
        var perOccurrence = fixes.Where(f => !f.IsFixAll).Select(f => f.Title).ToList();
        Assert.Equal(perOccurrence.Distinct().Count(), perOccurrence.Count);
    }

    [Fact]
    public async Task ApplyDocumentFixAll_RewritesEveryOccurrence_InOneEdit()
    {
        var (_, fixes) = await FindFixesAtFirstVarLineAsync();
        var fixAll = fixes.Single(f => f.IsFixAll);

        var edit = await _codeActions.ApplyAsync(fixAll.Id, ct: CancellationToken.None);

        Assert.NotNull(edit);
        Assert.Null(edit!.ErrorMessage);
        Assert.False(edit.IsExpired);
        var documentEdit = Assert.Single(edit.DocumentEdits);
        // A fix-all over a var-heavy file collapses many occurrences into one applyable edit.
        Assert.True(documentEdit.Edits.Count > 1,
            $"expected multiple text edits from the fix-all, got {documentEdit.Edits.Count}");
    }

    // Scans the target file for the first local `var` declaration and returns the IDE0008 fixes there.
    private async Task<(int Line, System.Collections.Immutable.ImmutableList<CodeFixEntry> Fixes)>
        FindFixesAtFirstVarLineAsync()
    {
        var lines = await File.ReadAllLinesAsync(TargetFile);
        for (var i = 0; i < lines.Length; i++)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(lines[i], @"^\s*var \w"))
                continue;
            var fixes = await _codeActions.GetCodeFixesAsync(TargetFile, i + 1, "IDE0008", CancellationToken.None);
            if (fixes.Any(f => f.IsFixAll))
                return (i + 1, fixes);
        }

        return (0, []);
    }
}
