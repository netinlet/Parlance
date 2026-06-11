namespace Parlance.Abstractions.Tests;

public sealed class RepoPathTests
{
    [Fact]
    public void Relative_StripsWorkspaceRoot()
    {
        var path = new RepoPath("/repo/src/Foo.cs");
        Assert.Equal(Path.Combine("src", "Foo.cs"), path.Relative(new RepoPath("/repo")));
    }

    [Fact]
    public void Relative_EmptyRoot_ReturnsAbsolute()
    {
        var path = new RepoPath("/repo/src/Foo.cs");
        Assert.Equal("/repo/src/Foo.cs", path.Relative(new RepoPath("")));
    }

    [Fact]
    public void Relative_OutsideRoot_ReturnsDotDotPrefixed()
    {
        var path = new RepoPath("/other/Foo.cs");
        Assert.Equal(Path.Combine("..", "other", "Foo.cs"), path.Relative(new RepoPath("/repo")));
    }

    [Fact]
    public void ToString_IsAbsolute()
    {
        Assert.Equal("/repo/a.cs", new RepoPath("/repo/a.cs").ToString());
    }

    [Fact]
    public void ToString_Default_IsEmptyNotNull()
    {
        // default(RepoPath).Absolute is null; ToString must still honour object.ToString()'s
        // non-null contract rather than returning null into a downstream concat/log.
        Assert.Equal("", default(RepoPath).ToString());
    }

    [Fact]
    public void ToRepoPath_NullOrEmpty_ReturnsNull()
    {
        Assert.False(((string?)null).ToRepoPath().HasValue);
        Assert.False("".ToRepoPath().HasValue);
    }

    [Fact]
    public void ToRepoPath_NonEmpty_WrapsAbsolute()
    {
        var path = "/repo/a.cs".ToRepoPath();
        Assert.NotNull(path);
        Assert.Equal("/repo/a.cs", path!.Value.Absolute);
    }

    [Fact]
    public void Relative_EmptyAbsolute_ReturnsEmptyString()
    {
        Assert.Equal("", new RepoPath("").Relative(new RepoPath("/some/root")));
    }
}
