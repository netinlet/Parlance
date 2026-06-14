namespace Parlance.Analyzers.Upstream.Tests;

public sealed class BundledAnalyzerSourceTests
{
    private readonly BundledAnalyzerSource _source = new();

    [Fact]
    public void Metadata_IsFirstPartyPriority20()
    {
        Assert.Equal("bundled", _source.Name);
        Assert.Equal(SourceTrust.FirstParty, _source.Trust);
        Assert.Equal(20, _source.Priority);
    }

    [Fact]
    public void Load_Net10_ReturnsAnalyzersAndFixes()
    {
        var result = _source.Load("net10.0", repoPath: "/unused");
        Assert.NotEmpty(result.Components.Analyzers);
        Assert.NotEmpty(result.Components.FixProviders);
        Assert.Empty(result.Failures);
    }

    [Theory]
    [InlineData("net9.0")]
    [InlineData("net7.0")]
    [InlineData("net48")]
    [InlineData("netstandard2.0")]
    public void Load_UnsupportedTfm_DegradesToNet10_DoesNotThrow(string tfm)
    {
        // The underlying loaders throw ArgumentException on an unsupported TFM; the source must
        // degrade to net10.0 so analyze/code-fix on, e.g., a net9.0 project still produces results.
        var result = _source.Load(tfm, repoPath: "/unused");
        Assert.NotEmpty(result.Components.Analyzers);
        Assert.NotEmpty(result.Components.FixProviders);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public void Probe_IsEmpty()
    {
        Assert.Empty(_source.Probe("/unused"));
    }
}
