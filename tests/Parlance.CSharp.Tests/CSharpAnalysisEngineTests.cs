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
    public async Task DefaultExpression_ReturnsDiagnostic()
    {
        var source = """
            class C
            {
                int M()
                {
                    return default(int);
                }
            }
            """;

        var result = await _engine.AnalyzeSourceAsync(source);

        var diag = Assert.Single(result.Diagnostics);
        Assert.Equal("PARL9003", diag.RuleId);
        Assert.Equal("Modernization", diag.Category);
        Assert.NotNull(diag.Rationale);
        Assert.NotNull(diag.SuggestedFix);
    }

    [Fact]
    public async Task SuppressRules_FiltersOut()
    {
        var source = """
            class C
            {
                int M()
                {
                    return default(int);
                }
            }
            """;

        var options = new AnalysisOptions(SuppressRules: ["PARL9003"]);
        var result = await _engine.AnalyzeSourceAsync(source, options);

        Assert.DoesNotContain(result.Diagnostics, d => d.RuleId == "PARL9003");
    }

    [Fact]
    public async Task MaxDiagnostics_CapsOutput()
    {
        var source = """
            class C
            {
                int M1() { return default(int); }
                string M2() { return default(string); }
                bool M3() { return default(bool); }
                double M4() { return default(double); }
                long M5() { return default(long); }
            }
            """;

        var options = new AnalysisOptions(SuppressRules: [], MaxDiagnostics: 2);
        var result = await _engine.AnalyzeSourceAsync(source, options);

        Assert.True(result.Diagnostics.Count <= 2);
        // Score reflects all diagnostics, not just the capped set
        Assert.True(result.Summary.TotalDiagnostics >= result.Diagnostics.Count);
    }

    [Fact]
    public async Task Language_IsCSharp()
    {
        Assert.Equal("csharp", _engine.Language);
    }
}
