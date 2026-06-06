using Parlance.Abstractions;

namespace Parlance.Analysis.Tests;

public sealed class DiagnosticSeverityFormattingTests
{
    [Theory]
    [InlineData(DiagnosticSeverity.Error, "error")]
    [InlineData(DiagnosticSeverity.Warning, "warning")]
    [InlineData(DiagnosticSeverity.Suggestion, "suggestion")]
    [InlineData(DiagnosticSeverity.Silent, "silent")]
    public void ToWireString_returns_stable_wire_format(DiagnosticSeverity severity, string expected)
    {
        Assert.Equal(expected, severity.ToWireString());
    }

    [Theory]
    [InlineData("error", DiagnosticSeverity.Error)]
    [InlineData("warning", DiagnosticSeverity.Warning)]
    [InlineData("suggestion", DiagnosticSeverity.Suggestion)]
    [InlineData("silent", DiagnosticSeverity.Silent)]
    public void FromWireString_parses_wire_format(string wire, DiagnosticSeverity expected)
    {
        Assert.Equal(expected, DiagnosticSeverity.FromWireString(wire));
    }

    [Fact]
    public void FromWireString_rejects_unknown_value()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DiagnosticSeverity.FromWireString("Error"));
    }
}
