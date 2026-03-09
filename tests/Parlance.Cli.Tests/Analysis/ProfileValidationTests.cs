using Parlance.Cli.Analysis;

namespace Parlance.Cli.Tests.Analysis;

public sealed class ProfileValidationTests : IDisposable
{
    private readonly string _tempDir;

    public ProfileValidationTests()
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
    public async Task Analyze_InvalidProfile_ReturnsErrorNotException()
    {
        var file = Path.Combine(_tempDir, "Test.cs");
        File.WriteAllText(file, "class C { }");

        // Should not throw — should return a result with an error or handle gracefully
        var exception = await Record.ExceptionAsync(() =>
            WorkspaceAnalyzer.AnalyzeAsync([file], profile: "nonexistent"));

        // After the fix, this should not throw ArgumentException
        Assert.Null(exception);
    }
}
