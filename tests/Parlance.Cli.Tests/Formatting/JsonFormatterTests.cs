using System.Collections.Immutable;
using System.Text.Json;
using Parlance.Abstractions;
using Parlance.Analysis;
using Parlance.Cli.Formatting;

namespace Parlance.Cli.Tests.Formatting;

public sealed class JsonFormatterTests
{
    [Fact]
    public void Format_ProducesValidJsonWithExpectedProperties()
    {
        var result = new FileAnalysisResult(
            "default",
            new AnalysisSummary(1, 0, 1, 0, ImmutableDictionary<string, int>.Empty, 95.0),
            [new FileDiagnostic("CA1822", "Style", "warning", "Member can be static",
                "src/Foo.cs", 10, 10, 5, 15, "auto-fixable", null)]);

        var json = new JsonFormatter().Format(result);
        var doc = JsonDocument.Parse(json);

        Assert.True(doc.RootElement.TryGetProperty("summary", out _));
        Assert.True(doc.RootElement.TryGetProperty("diagnostics", out _));
        Assert.True(doc.RootElement.TryGetProperty("curationSet", out _));
    }

    [Fact]
    public void Format_UsesCamelCase()
    {
        var result = new FileAnalysisResult(
            "default",
            new AnalysisSummary(0, 0, 0, 0, ImmutableDictionary<string, int>.Empty, 100.0),
            []);

        var json = new JsonFormatter().Format(result);
        var doc = JsonDocument.Parse(json);

        // camelCase keys
        Assert.True(doc.RootElement.TryGetProperty("curationSet", out _));
        Assert.False(doc.RootElement.TryGetProperty("CurationSet", out _));
    }
}
