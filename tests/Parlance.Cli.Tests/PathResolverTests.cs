namespace Parlance.Cli.Tests;

public sealed class PathResolverTests : IDisposable
{
    private readonly string _tempDir;

    public PathResolverTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"parlance-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Resolves_SingleFile()
    {
        var file = Path.Combine(_tempDir, "Test.cs");
        File.WriteAllText(file, "class C {}");

        var result = PathResolver.Resolve([file]);

        Assert.Single(result);
        Assert.Equal(file, result[0]);
    }

    [Fact]
    public void Resolves_Directory_RecursivelyFinds_CsFiles()
    {
        var sub = Path.Combine(_tempDir, "sub");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(_tempDir, "A.cs"), "");
        File.WriteAllText(Path.Combine(sub, "B.cs"), "");
        File.WriteAllText(Path.Combine(_tempDir, "readme.txt"), "");

        var result = PathResolver.Resolve([_tempDir]);

        Assert.Equal(2, result.Count);
        Assert.All(result, f => Assert.EndsWith(".cs", f));
    }

    [Fact]
    public void Resolves_GlobPattern()
    {
        var sub = Path.Combine(_tempDir, "src");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "A.cs"), "");
        File.WriteAllText(Path.Combine(sub, "B.cs"), "");
        File.WriteAllText(Path.Combine(_tempDir, "C.cs"), "");

        var pattern = Path.Combine(_tempDir, "src", "*.cs");
        var result = PathResolver.Resolve([pattern]);

        Assert.Equal(2, result.Count);
        Assert.All(result, f => Assert.Contains("src", f));
    }

    [Fact]
    public void Resolves_MultipleInputs()
    {
        var file = Path.Combine(_tempDir, "A.cs");
        var sub = Path.Combine(_tempDir, "dir");
        Directory.CreateDirectory(sub);
        File.WriteAllText(file, "");
        File.WriteAllText(Path.Combine(sub, "B.cs"), "");

        var result = PathResolver.Resolve([file, sub]);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Returns_Empty_ForNoMatches()
    {
        var result = PathResolver.Resolve([Path.Combine(_tempDir, "*.cs")]);

        Assert.Empty(result);
    }

    [Fact]
    public void Deduplicates_Files()
    {
        var file = Path.Combine(_tempDir, "A.cs");
        File.WriteAllText(file, "");

        var result = PathResolver.Resolve([file, file, _tempDir]);

        Assert.Single(result);
    }
}
