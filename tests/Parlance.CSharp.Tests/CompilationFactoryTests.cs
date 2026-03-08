namespace Parlance.CSharp.Tests;

public sealed class CompilationFactoryTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"parlance-versiontest-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void SelectLatestVersion_PicksSemanticallyHighest()
    {
        // Simulate directory names like version folders under a packs root
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(Path.Combine(_tempDir, "8.0.24"));
        Directory.CreateDirectory(Path.Combine(_tempDir, "9.0.13"));
        Directory.CreateDirectory(Path.Combine(_tempDir, "10.0.3"));

        var selected = CompilationFactory.SelectLatestVersionDirectory(_tempDir);

        Assert.NotNull(selected);
        Assert.Equal("10.0.3", Path.GetFileName(selected));
    }

    [Fact]
    public void SelectLatestVersion_HandlesSingleVersion()
    {
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(Path.Combine(_tempDir, "10.0.0"));

        var selected = CompilationFactory.SelectLatestVersionDirectory(_tempDir);

        Assert.NotNull(selected);
        Assert.Equal("10.0.0", Path.GetFileName(selected));
    }

    [Fact]
    public void SelectLatestVersion_IgnoresNonVersionDirectories()
    {
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(Path.Combine(_tempDir, "9.0.13"));
        Directory.CreateDirectory(Path.Combine(_tempDir, "not-a-version"));
        Directory.CreateDirectory(Path.Combine(_tempDir, "10.0.3"));

        var selected = CompilationFactory.SelectLatestVersionDirectory(_tempDir);

        Assert.NotNull(selected);
        Assert.Equal("10.0.3", Path.GetFileName(selected));
    }

    [Fact]
    public void SelectLatestVersion_ReturnsNullForEmpty()
    {
        Directory.CreateDirectory(_tempDir);

        var selected = CompilationFactory.SelectLatestVersionDirectory(_tempDir);

        Assert.Null(selected);
    }
}
