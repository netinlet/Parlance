namespace Parlance.Analyzers.Upstream.Tests;

public sealed class FixProviderLoaderTests
{
    [Fact]
    public void LoadAll_Net10_ReturnsProviders()
    {
        var providers = FixProviderLoader.LoadAll("net10.0");
        Assert.NotEmpty(providers);
    }

    [Fact]
    public void LoadAll_Net10_HasFixableIds()
    {
        var providers = FixProviderLoader.LoadAll("net10.0");
        var fixableIds = providers.SelectMany(p => p.FixableDiagnosticIds).ToHashSet();
        Assert.True(fixableIds.Count > 0, "Expected fix providers to expose fixable diagnostic IDs");
    }

    [Fact]
    public void LoadAll_Net8_ReturnsProviders()
    {
        var providers = FixProviderLoader.LoadAll("net8.0");
        Assert.NotEmpty(providers);
    }

    [Fact]
    public void LoadAll_UnknownTfm_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => FixProviderLoader.LoadAll("net99.0"));
    }

    [Fact]
    public void LoadAll_ParlAnalyzersHaveNoFixProviders()
    {
        var providers = FixProviderLoader.LoadAll("net10.0");
        var fixableIds = providers.SelectMany(p => p.FixableDiagnosticIds).ToHashSet();
        Assert.DoesNotContain(fixableIds, id => id.StartsWith("PARL"));
    }
}
