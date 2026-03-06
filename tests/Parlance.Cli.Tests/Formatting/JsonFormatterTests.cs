using System.Collections.Immutable;
using System.Text.Json;
using Parlance.Abstractions;
using Parlance.Cli.Formatting;

namespace Parlance.Cli.Tests.Formatting;

public sealed class JsonFormatterTests
{
    [Fact]
    public void Produces_ValidJson()
    {
        var output = new AnalysisOutput(
            [
                new FileDiagnostic(
                    "src/Example.cs",
                    new Diagnostic(
                        "PARL0001", "Naming", DiagnosticSeverity.Warning,
                        "Use PascalCase",
                        new Location(10, 5, 10, 15)))
            ],
            new AnalysisSummary(1, 0, 1, 0, ImmutableDictionary<string, int>.Empty.Add("Naming", 1), 95.0),
            FilesAnalyzed: 3);

        var formatter = new JsonFormatter();
        var json = formatter.Format(output);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(JsonValueKind.Array, root.GetProperty("diagnostics").ValueKind);
        Assert.Equal(1, root.GetProperty("diagnostics").GetArrayLength());
        Assert.Equal("PARL0001", root.GetProperty("diagnostics")[0].GetProperty("ruleId").GetString());
        Assert.Equal(95.0, root.GetProperty("summary").GetProperty("idiomaticScore").GetDouble());
        Assert.Equal(3, root.GetProperty("summary").GetProperty("filesAnalyzed").GetInt32());
    }

    [Fact]
    public void Uses_CamelCase_PropertyNames()
    {
        var output = new AnalysisOutput(
            [
                new FileDiagnostic(
                    "src/Example.cs",
                    new Diagnostic(
                        "PARL0001", "Naming", DiagnosticSeverity.Error,
                        "Test message",
                        new Location(1, 1, 1, 10),
                        SuggestedFix: "Fix it"))
            ],
            new AnalysisSummary(1, 1, 0, 0, ImmutableDictionary<string, int>.Empty.Add("Naming", 1), 90.0),
            FilesAnalyzed: 1);

        var formatter = new JsonFormatter();
        var json = formatter.Format(output);

        Assert.Contains("\"filePath\"", json);
        Assert.Contains("\"ruleId\"", json);
        Assert.Contains("\"suggestedFix\"", json);
        Assert.Contains("\"idiomaticScore\"", json);
        Assert.Contains("\"filesAnalyzed\"", json);
        Assert.Contains("\"totalDiagnostics\"", json);

        Assert.DoesNotContain("\"FilePath\"", json);
        Assert.DoesNotContain("\"RuleId\"", json);
        Assert.DoesNotContain("\"SuggestedFix\"", json);
        Assert.DoesNotContain("\"IdiomaticScore\"", json);
    }
}
