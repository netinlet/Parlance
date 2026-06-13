namespace Parlance.Analyzers.Upstream.Tests;

public sealed class RoslynFeaturesAnalyzerSourceTests
{
    private readonly RoslynFeaturesAnalyzerSource _source = new();

    [Fact]
    public void Metadata_IsFirstPartyPriority10()
    {
        Assert.Equal("roslyn-features", _source.Name);
        Assert.Equal(SourceTrust.FirstParty, _source.Trust);
        Assert.Equal(10, _source.Priority);
    }

    [Fact]
    public void Load_ReturnsFixAndRefactoringProviders_NoAnalyzers()
    {
        var result = _source.Load("net10.0", repoPath: "/unused");
        Assert.Empty(result.Components.Analyzers);
        Assert.NotEmpty(result.Components.FixProviders);
        Assert.NotEmpty(result.Components.RefactoringProviders);
    }
}
