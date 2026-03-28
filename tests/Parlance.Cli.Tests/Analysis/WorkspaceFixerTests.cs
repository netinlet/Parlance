using Parlance.Cli.Analysis;

namespace Parlance.Cli.Tests.Analysis;

public sealed class WorkspaceFixerTests : IDisposable
{
    private readonly string _tempDir;

    public WorkspaceFixerTests()
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
    public async Task Returns_Empty_WhenNoFixesAvailable()
    {
        var file = Path.Combine(_tempDir, "Clean.cs");
        File.WriteAllText(file, """
            class C
            {
                void M() { }
            }
            """);

        var result = await WorkspaceFixer.FixAsync([file]);

        Assert.Empty(result.FixedFiles);
    }
}
