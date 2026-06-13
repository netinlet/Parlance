using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.Text;

namespace Parlance.Analysis.Tests;

public sealed class UnifiedDiffTests
{
    private static string Render(string oldText, string newText, int context = 3) =>
        UnifiedDiff.Render(SourceText.From(oldText), SourceText.From(newText), context);

    [Fact]
    public void IdenticalText_ReturnsEmptyString() =>
        Assert.Equal("", Render("a\nb\nc\n", "a\nb\nc\n"));

    [Fact]
    public void SingleLineChange_ShowsRemovedAndAddedLineWithContext()
    {
        var diff = Render("a\nb\nc\nd\ne\n", "a\nb\nX\nd\ne\n");

        Assert.Contains("-c", diff);
        Assert.Contains("+X", diff);
        // Surrounding unchanged lines appear as context (space-prefixed), so the change can be read in situ.
        Assert.Contains(" b", diff);
        Assert.Contains(" d", diff);
    }

    [Fact]
    public void HunkHeader_UsesUnifiedFormat()
    {
        var diff = Render("a\nb\nc\nd\ne\n", "a\nb\nX\nd\ne\n");

        Assert.Matches(@"^@@ -\d+,\d+ \+\d+,\d+ @@", diff.Split('\n')[0]);
    }

    [Fact]
    public void PureInsertion_ShowsAddedLine_NoRemoval()
    {
        var diff = Render("a\nb\n", "a\nNEW\nb\n");

        Assert.Contains("+NEW", diff);
        Assert.DoesNotContain("\n-", "\n" + diff); // no removed (-prefixed) line
    }

    [Fact]
    public void PureDeletion_ShowsRemovedLine_NoAddition()
    {
        var diff = Render("a\nGONE\nb\n", "a\nb\n");

        Assert.Contains("-GONE", diff);
        Assert.DoesNotContain("\n+", "\n" + diff); // no added (+prefixed) line
    }

    [Fact]
    public void DistantChanges_ProduceSeparateHunks()
    {
        // Two changes far enough apart (well beyond 2*context unchanged lines between) get their own hunks.
        var before = "l1\nl2\nl3\nl4\nl5\nl6\nl7\nl8\nl9\nl10\nl11\nl12\n";
        var after = "X1\nl2\nl3\nl4\nl5\nl6\nl7\nl8\nl9\nl10\nl11\nX12\n";

        var diff = Render(before, after);

        var headers = Regex.Matches(diff, @"^@@ ", RegexOptions.Multiline).Count;
        Assert.Equal(2, headers);
    }

    [Fact]
    public void ContextIsBounded_DoesNotDumpWholeFile()
    {
        // A one-line change in a long file must not emit every line — only the change plus `context` lines.
        var before = string.Concat(Enumerable.Range(1, 100).Select(i => $"line{i}\n"));
        var after = before.Replace("line50\n", "lineFIFTY\n");

        var diff = Render(before, after, context: 2);

        Assert.DoesNotContain("line10\n", diff);
        Assert.DoesNotContain("line90\n", diff);
        Assert.Contains("-line50", diff);
        Assert.Contains("+lineFIFTY", diff);
        // change (2 lines) + 2 context each side + 1 header ≈ 7 lines, certainly far fewer than 100.
        Assert.True(diff.Split('\n').Length < 15, $"diff unexpectedly large:\n{diff}");
    }
}
