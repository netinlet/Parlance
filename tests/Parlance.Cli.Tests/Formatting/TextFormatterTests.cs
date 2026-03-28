using System.Collections.Immutable;
using Parlance.Abstractions;
using Parlance.Analysis;
using Parlance.Cli.Formatting;

namespace Parlance.Cli.Tests.Formatting;

public sealed class TextFormatterTests
{
    [Fact]
    public void Format_DiagnosticsAndSummary()
    {
        var result = new FileAnalysisResult(
            "default",
            new AnalysisSummary(1, 0, 1, 0, ImmutableDictionary<string, int>.Empty.Add("Style", 1), 95.0),
            [new FileDiagnostic("CA1822", "Style", "warning", "Member can be static",
                "src/Foo.cs", 10, 10, 5, 15, "auto-fixable", null)]);

        var output = new TextFormatter().Format(result);

        Assert.Contains("src/Foo.cs(10,5)", output);
        Assert.Contains("warning CA1822", output);
        Assert.Contains("Member can be static", output);
        Assert.Contains("Fix: auto-fixable", output);
        Assert.Contains("95/100", output);
    }

    [Fact]
    public void Format_NoDiagnostics()
    {
        var result = new FileAnalysisResult(
            "default",
            new AnalysisSummary(0, 0, 0, 0, ImmutableDictionary<string, int>.Empty, 100.0),
            []);

        var output = new TextFormatter().Format(result);

        Assert.Contains("100/100", output);
        Assert.Contains("Total diagnostics: 0", output);
    }
}
