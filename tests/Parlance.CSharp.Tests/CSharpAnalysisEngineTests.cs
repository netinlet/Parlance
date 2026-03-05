using Parlance.Abstractions;

namespace Parlance.CSharp.Tests;

public sealed class CSharpAnalysisEngineTests
{
    private readonly CSharpAnalysisEngine _engine = new();

    [Fact]
    public async Task CleanCode_Returns100()
    {
        var source = """
            class C
            {
                public void M()
                {
                    System.Console.WriteLine("hello");
                }
            }
            """;

        var result = await _engine.AnalyzeSourceAsync(source);

        Assert.Equal(100, result.Summary.IdiomaticScore);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task IsCastPattern_ReturnsDiagnosticWithRationale()
    {
        var source = """
            class C
            {
                void M(object obj)
                {
                    if (obj is string)
                    {
                        var s = (string)obj;
                    }
                }
            }
            """;

        var result = await _engine.AnalyzeSourceAsync(source);

        var diag = Assert.Single(result.Diagnostics);
        Assert.Equal("PARL0004", diag.RuleId);
        Assert.Equal("PatternMatching", diag.Category);
        Assert.Equal(DiagnosticSeverity.Warning, diag.Severity);
        Assert.NotNull(diag.Rationale);
        Assert.NotNull(diag.SuggestedFix);
        Assert.True(diag.Location.Line > 0);
    }

    [Fact]
    public async Task MultipleIssues_CorrectScoreAndCategories()
    {
        var source = """
            using System.Collections.Generic;
            class C
            {
                void M(object obj)
                {
                    var list = new List<int> { 1, 2, 3 };
                    if (obj is string)
                    {
                        var s = (string)obj;
                    }
                }
            }
            """;

        var result = await _engine.AnalyzeSourceAsync(source);

        Assert.True(result.Diagnostics.Count >= 2);
        Assert.True(result.Summary.IdiomaticScore < 100);
        Assert.True(result.Summary.ByCategory.ContainsKey("PatternMatching"));
        Assert.True(result.Summary.ByCategory.ContainsKey("Modernization"));
    }

    [Fact]
    public async Task SuppressRules_FiltersOut()
    {
        var source = """
            class C
            {
                void M(object obj)
                {
                    if (obj is string)
                    {
                        var s = (string)obj;
                    }
                }
            }
            """;

        var options = new AnalysisOptions(SuppressRules: ["PARL0004"]);
        var result = await _engine.AnalyzeSourceAsync(source, options);

        Assert.DoesNotContain(result.Diagnostics, d => d.RuleId == "PARL0004");
    }

    [Fact]
    public async Task MaxDiagnostics_CapsOutput()
    {
        var source = """
            using System.Collections.Generic;
            class C
            {
                void M(object obj)
                {
                    var a = new List<int> { 1 };
                    var b = new List<int> { 2 };
                    var c = new List<int> { 3 };
                    var d = new List<int> { 4 };
                    var e = new List<int> { 5 };
                }
            }
            """;

        var options = new AnalysisOptions(SuppressRules: [], MaxDiagnostics: 2);
        var result = await _engine.AnalyzeSourceAsync(source, options);

        Assert.True(result.Diagnostics.Count <= 2);
    }

    [Fact]
    public async Task Language_IsCSharp()
    {
        Assert.Equal("csharp", _engine.Language);
    }
}
