using Microsoft.CodeAnalysis.Diagnostics;

namespace Parlance.Analyzers.Upstream.Tests;

public sealed class AnalyzerLoaderTests
{
    [Fact]
    public void LoadAll_Net10_ReturnsAnalyzers()
    {
        var analyzers = AnalyzerLoader.LoadAll("net10.0");
        Assert.NotEmpty(analyzers);
    }

    [Fact]
    public void LoadAll_Net8_ReturnsAnalyzers()
    {
        var analyzers = AnalyzerLoader.LoadAll("net8.0");
        Assert.NotEmpty(analyzers);
    }

    [Fact]
    public void LoadAll_Net10_IncludesUpstreamAnalyzers()
    {
        var analyzers = AnalyzerLoader.LoadAll("net10.0");
        var allIds = analyzers
            .SelectMany(a => a.SupportedDiagnostics)
            .Select(d => d.Id)
            .ToHashSet();

        // Spot-check known rules from each upstream source
        Assert.Contains("CA1822", allIds);  // NetAnalyzers
        Assert.Contains("IDE0055", allIds); // CodeStyle
        Assert.Contains("RCS1003", allIds); // Roslynator
    }

    [Fact]
    public void LoadAll_UnknownTfm_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => AnalyzerLoader.LoadAll("net99.0"));
    }

    [Fact]
    public void LoadAll_Net10_ReturnsReasonableCount()
    {
        var analyzers = AnalyzerLoader.LoadAll("net10.0");
        Assert.True(analyzers.Length >= 50,
            $"Expected at least 50 analyzer types, got {analyzers.Length}");
    }

    [Fact]
    public void LoadAll_Net8VsNet10_BothHaveAnalyzers()
    {
        var net8 = AnalyzerLoader.LoadAll("net8.0");
        var net10 = AnalyzerLoader.LoadAll("net10.0");
        Assert.NotEmpty(net8);
        Assert.NotEmpty(net10);
    }
}
