namespace Parlance.Analyzers.Upstream.Tests;

public sealed class ProfileProviderTests
{
    [Theory]
    [InlineData("net10.0", "default")]
    [InlineData("net8.0", "default")]
    public void GetProfileContent_ReturnsContent(string tfm, string profile)
    {
        var content = ProfileProvider.GetProfileContent(tfm, profile);

        Assert.NotNull(content);
        Assert.NotEmpty(content);
        Assert.Contains("[*.cs]", content);
    }

    [Fact]
    public void GetProfileContent_UnknownProfile_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            ProfileProvider.GetProfileContent("net10.0", "nonexistent"));
    }

    [Fact]
    public void GetAvailableProfiles_ReturnsExpectedProfiles()
    {
        var profiles = ProfileProvider.GetAvailableProfiles();

        Assert.Contains("default", profiles);
    }
}
