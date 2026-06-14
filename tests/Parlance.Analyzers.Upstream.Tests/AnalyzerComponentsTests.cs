namespace Parlance.Analyzers.Upstream.Tests;

public sealed class AnalyzerComponentsTests
{
    [Fact]
    public void Empty_HasNoComponents()
    {
        Assert.Empty(AnalyzerComponents.Empty.Analyzers);
        Assert.Empty(AnalyzerComponents.Empty.FixProviders);
        Assert.Empty(AnalyzerComponents.Empty.RefactoringProviders);
    }

    [Fact]
    public void SourceLoadResult_Empty_HasEmptyComponentsAndNoFailures()
    {
        Assert.Equal(AnalyzerComponents.Empty, SourceLoadResult.Empty.Components);
        Assert.Empty(SourceLoadResult.Empty.Failures);
    }
}
