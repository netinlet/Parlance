namespace Parlance.Analyzers.Upstream.Tests;

public sealed class RefactoringProviderLoaderTests
{
    [Fact]
    public void LoadFromPaths_NonexistentPath_Throws()
    {
        Assert.Throws<FileNotFoundException>(() =>
            RefactoringProviderLoader.LoadFromPaths(["/no/such/dir"]));
    }

    [Fact]
    public void LoadFromPaths_EmptyDirectory_ReturnsEmpty()
    {
        var dir = Directory.CreateTempSubdirectory("parlance-refactor-").FullName;
        try
        {
            var providers = RefactoringProviderLoader.LoadFromPaths([dir]);
            Assert.Empty(providers);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
