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
                    List<int> list = new List<int> { 1, 2, 3 };
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
        // Score reflects all diagnostics, not just the capped set
        Assert.True(result.Summary.TotalDiagnostics >= result.Diagnostics.Count);
    }

    [Fact]
    public async Task Language_IsCSharp()
    {
        Assert.Equal("csharp", _engine.Language);
    }

    [Fact]
    public async Task LanguageVersion_CSharp10_SuppressesCSharp12Rules()
    {
        // PARL0001 requires C# 12. With language version set to 10, it should not fire.
        var source = """
            class C
            {
                private readonly string _name;
                private readonly int _age;

                public C(string name, int age)
                {
                    _name = name;
                    _age = age;
                }
            }
            """;

        var options = new AnalysisOptions(SuppressRules: [], LanguageVersion: "10");
        var result = await _engine.AnalyzeSourceAsync(source, options);

        Assert.DoesNotContain(result.Diagnostics, d => d.RuleId == "PARL0001");
    }

    [Fact]
    public async Task LanguageVersion_CSharp12_EnablesCSharp12Rules()
    {
        // PARL0001 requires C# 12. With language version set to 12, it should fire.
        var source = """
            class C
            {
                private readonly string _name;
                private readonly int _age;

                public C(string name, int age)
                {
                    _name = name;
                    _age = age;
                }
            }
            """;

        var options = new AnalysisOptions(SuppressRules: [], LanguageVersion: "12");
        var result = await _engine.AnalyzeSourceAsync(source, options);

        Assert.Contains(result.Diagnostics, d => d.RuleId == "PARL0001");
    }

    [Fact]
    public async Task LanguageVersion_Default_UsesLatest()
    {
        // When no language version is specified, should use Latest and fire C# 12+ rules
        var source = """
            class C
            {
                private readonly string _name;
                private readonly int _age;

                public C(string name, int age)
                {
                    _name = name;
                    _age = age;
                }
            }
            """;

        var result = await _engine.AnalyzeSourceAsync(source);

        Assert.Contains(result.Diagnostics, d => d.RuleId == "PARL0001");
    }

    [Fact]
    public async Task LanguageVersion_CSharp6_SuppressesAllModernizationRules()
    {
        // With C# 6, none of the PARL modernization rules should fire
        var source = """
            using System.Collections.Generic;
            class C
            {
                private readonly string _name;

                public C(string name)
                {
                    _name = name;
                }

                void M(object obj)
                {
                    if (obj is string)
                    {
                        var s = (string)obj;
                    }
                }
            }
            """;

        var options = new AnalysisOptions(SuppressRules: [], LanguageVersion: "6");
        var result = await _engine.AnalyzeSourceAsync(source, options);

        // PARL0001 (C# 12), PARL0004 (C# 7) — neither should fire at C# 6
        Assert.DoesNotContain(result.Diagnostics, d => d.RuleId == "PARL0001");
        Assert.DoesNotContain(result.Diagnostics, d => d.RuleId == "PARL0004");
    }
}
