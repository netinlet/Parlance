using Microsoft.CodeAnalysis.Text;

namespace Parlance.Analysis.Tests;

public sealed class CodeActionServicePreviewTests
{
    // "line1\nfoo bar baz\nline3" — line 2 starts at offset 6; "bar" is offset 10-13, "baz" 14-17.
    private static readonly SourceText Text = SourceText.From("line1\nfoo bar baz\nline3");

    [Fact]
    public void ToTextEdit_carries_1_based_line_and_column()
    {
        var edit = CodeActionService.ToTextEdit(Text, new TextChange(new TextSpan(10, 3), "BAR"));

        Assert.Equal("bar", edit.OriginalText);
        Assert.Equal("BAR", edit.NewText);
        Assert.Equal(new TextRange(2, 5, 2, 8), edit.Range);
    }

    [Fact]
    public void ToTextEdit_keeps_same_line_edits_distinct_by_column()
    {
        var bar = CodeActionService.ToTextEdit(Text, new TextChange(new TextSpan(10, 3), "BAR"));
        var baz = CodeActionService.ToTextEdit(Text, new TextChange(new TextSpan(14, 3), "BAZ"));

        // Both on line 2, but distinguishable — the bug Phase 7 fixes (line-only ranges collapsed).
        Assert.Equal(2, bar.Range.StartLine);
        Assert.Equal(2, baz.Range.StartLine);
        Assert.NotEqual(bar.Range.StartColumn, baz.Range.StartColumn);
        Assert.Equal(5, bar.Range.StartColumn);
        Assert.Equal(9, baz.Range.StartColumn);
    }
}
