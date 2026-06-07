namespace Parlance.Abstractions.Tests;

public sealed class RepoPathTests
{
    [Fact]
    public void Relative_StripsWorkspaceRoot()
    {
        var path = new RepoPath("/repo/src/Foo.cs");
        Assert.Equal(Path.Combine("src", "Foo.cs"), path.Relative("/repo"));
    }

    [Fact]
    public void Relative_EmptyRoot_ReturnsAbsolute()
    {
        var path = new RepoPath("/repo/src/Foo.cs");
        Assert.Equal("/repo/src/Foo.cs", path.Relative(""));
    }

    [Fact]
    public void ImplicitFromString_WrapsAbsolute()
    {
        RepoPath path = "/repo/a.cs";
        Assert.Equal("/repo/a.cs", path.Absolute);
    }

    [Fact]
    public void ToString_IsAbsolute()
    {
        Assert.Equal("/repo/a.cs", new RepoPath("/repo/a.cs").ToString());
    }
}
