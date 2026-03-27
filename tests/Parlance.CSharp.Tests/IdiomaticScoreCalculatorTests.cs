using System.Collections.Immutable;
using Parlance.Abstractions;

namespace Parlance.CSharp.Tests;

public sealed class IdiomaticScoreCalculatorTests
{
    private static readonly Location DummyLocation = new(1, 1, 1, 1);

    [Fact]
    public void NoDiagnostics_Returns100()
    {
        var result = IdiomaticScoreCalculator.Calculate([]);

        Assert.Equal(100, result.IdiomaticScore);
        Assert.Equal(0, result.TotalDiagnostics);
        Assert.Equal(0, result.Errors);
        Assert.Equal(0, result.Warnings);
        Assert.Equal(0, result.Suggestions);
        Assert.True(result.ByCategory.IsEmpty);
    }

    [Fact]
    public void SingleError_Deducts10()
    {
        ImmutableList<Diagnostic> diagnostics =
        [
            new("PARL9003", "Modernization", DiagnosticSeverity.Error,
                "test", DummyLocation)
        ];

        var result = IdiomaticScoreCalculator.Calculate(diagnostics);

        Assert.Equal(90, result.IdiomaticScore);
        Assert.Equal(1, result.Errors);
        Assert.Equal(1, result.ByCategory["Modernization"]);
    }

    [Fact]
    public void SingleWarning_Deducts5()
    {
        ImmutableList<Diagnostic> diagnostics =
        [
            new("TEST0001", "PatternMatching", DiagnosticSeverity.Warning,
                "test", DummyLocation)
        ];

        var result = IdiomaticScoreCalculator.Calculate(diagnostics);

        Assert.Equal(95, result.IdiomaticScore);
        Assert.Equal(1, result.Warnings);
    }

    [Fact]
    public void SingleSuggestion_Deducts2()
    {
        ImmutableList<Diagnostic> diagnostics =
        [
            new("TEST0002", "PatternMatching", DiagnosticSeverity.Suggestion,
                "test", DummyLocation)
        ];

        var result = IdiomaticScoreCalculator.Calculate(diagnostics);

        Assert.Equal(98, result.IdiomaticScore);
        Assert.Equal(1, result.Suggestions);
    }

    [Fact]
    public void MixedSeverities_CorrectDeductions()
    {
        ImmutableList<Diagnostic> diagnostics =
        [
            new("PARL9003", "Modernization", DiagnosticSeverity.Error,
                "test", DummyLocation),
            new("TEST0001", "PatternMatching", DiagnosticSeverity.Warning,
                "test", DummyLocation),
            new("TEST0002", "PatternMatching", DiagnosticSeverity.Suggestion,
                "test", DummyLocation),
        ];

        var result = IdiomaticScoreCalculator.Calculate(diagnostics);

        // 100 - 10 - 5 - 2 = 83
        Assert.Equal(83, result.IdiomaticScore);
        Assert.Equal(3, result.TotalDiagnostics);
        Assert.Equal(1, result.Errors);
        Assert.Equal(1, result.Warnings);
        Assert.Equal(1, result.Suggestions);
        Assert.Equal(1, result.ByCategory["Modernization"]);
        Assert.Equal(2, result.ByCategory["PatternMatching"]);
    }

    [Fact]
    public void ScoreFloorsAtZero()
    {
        var diagnostics = Enumerable.Range(0, 20)
            .Select(i => new Diagnostic($"PARL{i:D4}", "Test", DiagnosticSeverity.Error,
                "test", DummyLocation))
            .ToImmutableList();

        var result = IdiomaticScoreCalculator.Calculate(diagnostics);

        // 20 errors * -10 = -200, but floor is 0
        Assert.Equal(0, result.IdiomaticScore);
    }

    [Fact]
    public void SilentSeverity_NoDeduction()
    {
        ImmutableList<Diagnostic> diagnostics =
        [
            new("PARL9003", "Modernization", DiagnosticSeverity.Silent,
                "test", DummyLocation)
        ];

        var result = IdiomaticScoreCalculator.Calculate(diagnostics);

        Assert.Equal(100, result.IdiomaticScore);
        Assert.Equal(1, result.TotalDiagnostics);
    }
}
