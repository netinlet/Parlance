using System.Collections.Immutable;
using Parlance.Abstractions;
using Parlance.Cli.Formatting;

namespace Parlance.Cli.Tests.Formatting;

public sealed class TextFormatterTests
{
    [Fact]
    public void Formats_DiagnosticsAndSummary()
    {
        var output = new AnalysisOutput(
            [
                new FileDiagnostic(
                    "src/Example.cs",
                    new Diagnostic(
                        "PARL9003", "Naming", DiagnosticSeverity.Warning,
                        "Use PascalCase for public members",
                        new Location(10, 5, 10, 15),
                        Rationale: "C# conventions require PascalCase",
                        SuggestedFix: "Rename to PascalCase"))
            ],
            new AnalysisSummary(1, 0, 1, 0, ImmutableDictionary<string, int>.Empty.Add("Naming", 1), 95.0),
            FilesAnalyzed: 3);

        var formatter = new TextFormatter();
        var result = formatter.Format(output);

        Assert.Contains("src/Example.cs(10,5)", result);
        Assert.Contains("warning PARL9003", result);
        Assert.Contains("Use PascalCase for public members", result);
        Assert.Contains("Rationale: C# conventions require PascalCase", result);
        Assert.Contains("Suggested: Rename to PascalCase", result);
        Assert.Contains("95/100", result);
        Assert.Contains("Files analyzed: 3", result);
    }

    [Fact]
    public void Formats_CleanOutput_WhenNoDiagnostics()
    {
        var output = new AnalysisOutput(
            [],
            new AnalysisSummary(0, 0, 0, 0, ImmutableDictionary<string, int>.Empty, 100.0),
            FilesAnalyzed: 5);

        var formatter = new TextFormatter();
        var result = formatter.Format(output);

        Assert.Contains("100/100", result);
        Assert.Contains("Files analyzed: 5", result);
        Assert.Contains("Total diagnostics: 0", result);
    }
}
