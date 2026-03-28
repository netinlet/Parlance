using System.Collections.Immutable;
using Parlance.Analysis.Curation;

namespace Parlance.Analysis.Tests.Curation;

public sealed class CurationFilterTests
{
    [Fact]
    public void Matches_ExactRuleId()
    {
        var rule = new CurationRule("CA1062", null, "warning", "auto-fixable", "null-safety");
        Assert.True(CurationFilter.Matches(rule, "CA1062", "Design"));
    }

    [Fact]
    public void DoesNotMatch_DifferentRuleId()
    {
        var rule = new CurationRule("CA1062", null, "warning", null, null);
        Assert.False(CurationFilter.Matches(rule, "CA2000", "Design"));
    }

    [Fact]
    public void Matches_PrefixPattern()
    {
        var rule = new CurationRule("CA*", null, "warning", null, null);
        Assert.True(CurationFilter.Matches(rule, "CA1062", "Design"));
        Assert.True(CurationFilter.Matches(rule, "CA2000", "Reliability"));
        Assert.False(CurationFilter.Matches(rule, "RCS1001", "Roslynator"));
    }

    [Fact]
    public void Matches_Category()
    {
        var rule = new CurationRule(null, "Design", "warning", null, null);
        Assert.True(CurationFilter.Matches(rule, "CA1062", "Design"));
        Assert.False(CurationFilter.Matches(rule, "CA2000", "Reliability"));
    }

    [Fact]
    public void Matches_RuleIdTakesPrecedenceOverCategory()
    {
        var rule = new CurationRule("CA1062", "Performance", "warning", null, null);
        Assert.True(CurationFilter.Matches(rule, "CA1062", "Design"));
    }

    [Fact]
    public void Apply_FiltersToIncludedRulesOnly()
    {
        var set = new CurationSet("test", "test set",
            [new CurationRule("CA1062", null, "warning", "auto-fixable", null)],
            []);

        var diagnostics = ImmutableList.Create(
            MakeDiagnostic("CA1062", "Design", "warning"),
            MakeDiagnostic("CA2000", "Reliability", "warning"));

        var result = CurationFilter.Apply(set, diagnostics);

        Assert.Single(result);
        Assert.Equal("CA1062", result[0].RuleId);
    }

    [Fact]
    public void Apply_OverridesSeverity()
    {
        var set = new CurationSet("test", "test set",
            [new CurationRule("CA1062", null, "error", null, null)],
            []);

        var diagnostics = ImmutableList.Create(
            MakeDiagnostic("CA1062", "Design", "warning"));

        var result = CurationFilter.Apply(set, diagnostics);

        Assert.Single(result);
        Assert.Equal("error", result[0].Severity);
    }

    [Fact]
    public void Apply_AttachesFixClassification()
    {
        var set = new CurationSet("test", "test set",
            [new CurationRule("CA1062", null, "warning", "auto-fixable", null)],
            []);

        var diagnostics = ImmutableList.Create(
            MakeDiagnostic("CA1062", "Design", "warning"));

        var result = CurationFilter.Apply(set, diagnostics);

        Assert.Equal("auto-fixable", result[0].FixClassification);
    }

    [Fact]
    public void Apply_ResolvesRationale()
    {
        var set = new CurationSet("test", "test set",
            [new CurationRule("CA1062", null, "warning", null, "null-safety")],
            [new CurationRationale("null-safety", "AI code omits null guards")]);

        var diagnostics = ImmutableList.Create(
            MakeDiagnostic("CA1062", "Design", "warning"));

        var result = CurationFilter.Apply(set, diagnostics);

        Assert.Equal("AI code omits null guards", result[0].Rationale);
    }

    [Fact]
    public void Apply_NullSet_ReturnsAllDiagnosticsUnchanged()
    {
        var diagnostics = ImmutableList.Create(
            MakeDiagnostic("CA1062", "Design", "warning"),
            MakeDiagnostic("CA2000", "Reliability", "error"));

        var result = CurationFilter.Apply(null, diagnostics);

        Assert.Equal(2, result.Count);
        Assert.Equal("warning", result[0].Severity);
        Assert.Null(result[0].FixClassification);
    }

    private static CuratedDiagnostic MakeDiagnostic(string ruleId, string category, string severity) =>
        new(ruleId, category, severity, $"Message for {ruleId}", "test.cs", 1, 1, 1, 10, null, null);
}
