using Parlance.Analysis;
using Parlance.Analyzers.Upstream;

namespace Parlance.Analysis.Tests;

public static class AnalyzerProviderTestFactory
{
    public static AnalyzerProvider CreateWithBundled() =>
        new([new BundledAnalyzerSource(), new RoslynFeaturesAnalyzerSource()]);

    public static AnalyzerProvider CreateEmpty() =>
        new([]);
}
