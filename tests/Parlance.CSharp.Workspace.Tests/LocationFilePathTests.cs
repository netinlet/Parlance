using Parlance.Abstractions;

namespace Parlance.CSharp.Workspace.Tests;

public sealed class LocationFilePathTests
{
    [Fact]
    public void ExistingFourArgConstruction_StillWorks()
    {
        var loc = new Location(1, 2, 3, 4);

        Assert.Equal(1, loc.Line);
        Assert.Equal(2, loc.Column);
        Assert.Equal(3, loc.EndLine);
        Assert.Equal(4, loc.EndColumn);
        Assert.Null(loc.FilePath);
    }

    [Fact]
    public void FiveArgConstruction_SetsFilePath()
    {
        var loc = new Location(1, 2, 3, 4, "/path/to/file.cs");

        Assert.Equal("/path/to/file.cs", loc.FilePath);
    }

    [Fact]
    public void NamedFilePath_Works()
    {
        var loc = new Location(1, 2, 3, 4, FilePath: "/path/to/file.cs");

        Assert.Equal("/path/to/file.cs", loc.FilePath);
    }

    [Fact]
    public void Equality_WithSameFilePath()
    {
        var loc1 = new Location(1, 2, 3, 4, "/file.cs");
        var loc2 = new Location(1, 2, 3, 4, "/file.cs");

        Assert.Equal(loc1, loc2);
    }

    [Fact]
    public void Equality_DifferentFilePath_NotEqual()
    {
        var loc1 = new Location(1, 2, 3, 4, "/a.cs");
        var loc2 = new Location(1, 2, 3, 4, "/b.cs");

        Assert.NotEqual(loc1, loc2);
    }

    [Fact]
    public void Equality_NullVsSetFilePath_NotEqual()
    {
        var loc1 = new Location(1, 2, 3, 4);
        var loc2 = new Location(1, 2, 3, 4, "/file.cs");

        Assert.NotEqual(loc1, loc2);
    }

    [Fact]
    public void Equality_BothNullFilePath_Equal()
    {
        var loc1 = new Location(1, 2, 3, 4);
        var loc2 = new Location(1, 2, 3, 4);

        Assert.Equal(loc1, loc2);
    }
}
