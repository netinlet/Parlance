using System.Text;
using Microsoft.CodeAnalysis.Text;

namespace Parlance.Analysis;

/// <summary>
/// Renders a line-level unified diff (git-style hunks with surrounding context) between two
/// <see cref="SourceText"/>s. This is the <em>preview</em> shape: it exists so an agent can <em>judge</em> a
/// fix or refactoring — read the changed region in context and decide whether the result is any good —
/// before committing to it. It is deliberately not the apply shape: <see cref="WorkspaceEditBuilder"/>
/// produces the minimal, machine-applyable edits an agent writes once it has decided.
/// </summary>
public static class UnifiedDiff
{
    private readonly record struct DiffLine(char Tag, int OldLine, int NewLine, string Text);

    /// <summary>
    /// Renders hunks (no file header — the path is carried by the caller) with <paramref name="context"/>
    /// unchanged lines around each change; adjacent changes within <c>2 × context</c> lines merge into one
    /// hunk. Returns the empty string when the texts are identical.
    /// </summary>
    public static string Render(SourceText oldText, SourceText newText, int context = 3)
    {
        var oldLines = SplitLines(oldText.ToString());
        var newLines = SplitLines(newText.ToString());

        var lines = Diff(oldLines, newLines);
        if (lines.TrueForAll(l => l.Tag == ' ')) return "";

        return FormatHunks(lines, context);
    }

    // Logical lines for diffing: EOL-normalised, and a trailing newline does not count as a final empty line
    // (so counts match git semantics). The final "\ No newline" nuance is intentionally not rendered.
    private static string[] SplitLines(string text)
    {
        if (text.Length == 0) return [];
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var parts = normalized.Split('\n');
        var count = parts.Length;
        if (count > 0 && parts[count - 1].Length == 0) count--;
        return parts[..count];
    }

    // Common prefix/suffix are trimmed first (so a local edit runs the O(n·m) LCS only over the small differing
    // middle), then the whole sequence is reassembled with 1-based old/new line numbers on every line.
    private static List<DiffLine> Diff(string[] a, string[] b)
    {
        int n = a.Length, m = b.Length;

        var start = 0;
        while (start < n && start < m && a[start] == b[start]) start++;

        int endA = n, endB = m;
        while (endA > start && endB > start && a[endA - 1] == b[endB - 1]) { endA--; endB--; }

        var midOps = Lcs(a[start..endA], b[start..endB]);

        var result = new List<DiffLine>(n + m);
        int oldNo = 0, newNo = 0;

        for (var i = 0; i < start; i++)
            result.Add(new DiffLine(' ', ++oldNo, ++newNo, a[i]));

        foreach (var (tag, text) in midOps)
        {
            switch (tag)
            {
                case ' ': result.Add(new DiffLine(' ', ++oldNo, ++newNo, text)); break;
                case '-': result.Add(new DiffLine('-', ++oldNo, newNo, text)); break;
                case '+': result.Add(new DiffLine('+', oldNo, ++newNo, text)); break;
            }
        }

        for (var i = endA; i < n; i++)
            result.Add(new DiffLine(' ', ++oldNo, ++newNo, a[i]));

        return result;
    }

    // Standard longest-common-subsequence diff over two line arrays, returning equal/delete/insert ops in order.
    private static List<(char Tag, string Text)> Lcs(string[] a, string[] b)
    {
        int n = a.Length, m = b.Length;
        var dp = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
            for (var j = m - 1; j >= 0; j--)
                dp[i, j] = a[i] == b[j] ? dp[i + 1, j + 1] + 1 : Math.Max(dp[i + 1, j], dp[i, j + 1]);

        var ops = new List<(char, string)>();
        int x = 0, y = 0;
        while (x < n && y < m)
        {
            if (a[x] == b[y]) { ops.Add((' ', a[x])); x++; y++; }
            else if (dp[x + 1, y] >= dp[x, y + 1]) { ops.Add(('-', a[x])); x++; }
            else { ops.Add(('+', b[y])); y++; }
        }
        while (x < n) ops.Add(('-', a[x++]));
        while (y < m) ops.Add(('+', b[y++]));
        return ops;
    }

    private static string FormatHunks(List<DiffLine> lines, int context)
    {
        var sb = new StringBuilder();
        var i = 0;
        while (i < lines.Count)
        {
            if (lines[i].Tag == ' ') { i++; continue; }

            // Open the hunk `context` equal lines before the change.
            var hunkStart = Math.Max(0, i - context);

            // Extend through the change, tolerating up to `context` consecutive equal lines so a nearby second
            // change merges into the same hunk; stop once the run of equals exceeds the context window.
            var lastChange = i;
            var j = i;
            while (j < lines.Count)
            {
                if (lines[j].Tag != ' ') lastChange = j;
                else if (j - lastChange > context) break;
                j++;
            }
            var hunkEnd = Math.Min(lines.Count, lastChange + 1 + context);

            EmitHunk(sb, lines, hunkStart, hunkEnd);
            i = hunkEnd;
        }
        return sb.ToString();
    }

    private static void EmitHunk(StringBuilder sb, List<DiffLine> lines, int start, int end)
    {
        int oldStart = 0, oldCount = 0, newStart = 0, newCount = 0;
        for (var k = start; k < end; k++)
        {
            var line = lines[k];
            if (line.Tag is ' ' or '-')
            {
                if (oldCount == 0) oldStart = line.OldLine;
                oldCount++;
            }
            if (line.Tag is ' ' or '+')
            {
                if (newCount == 0) newStart = line.NewLine;
                newCount++;
            }
        }

        // A side with no lines (pure insertion or deletion at a file edge) gets a 0 start, as git does.
        sb.Append("@@ -").Append(oldCount == 0 ? 0 : oldStart).Append(',').Append(oldCount)
          .Append(" +").Append(newCount == 0 ? 0 : newStart).Append(',').Append(newCount).Append(" @@\n");
        for (var k = start; k < end; k++)
            sb.Append(lines[k].Tag).Append(lines[k].Text).Append('\n');
    }
}
