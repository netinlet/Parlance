using Parlance.Cli.Analysis;

namespace Parlance.Cli.Tests.Analysis;

public sealed class WorkspaceAnalyzerTests : IDisposable
{
    private readonly string _tempDir;

    public WorkspaceAnalyzerTests()
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
    public async Task Analyzes_FileWithDiagnostic()
    {
        var file = Path.Combine(_tempDir, "Test.cs");
        File.WriteAllText(file, """
            class C
            {
                void M(object obj)
                {
                    if (obj is string)
                    {
                        var s = (string)obj;
                    }
                }
            }
            """);

        var result = await WorkspaceAnalyzer.AnalyzeAsync([file]);

        Assert.True(result.Diagnostics.Count > 0);
        Assert.Contains(result.Diagnostics, d => d.Diagnostic.RuleId == "PARL0004");
        Assert.Equal(1, result.FilesAnalyzed);
    }

    [Fact]
    public async Task Analyzes_CleanFile_ScoresHigh()
    {
        var file = Path.Combine(_tempDir, "Clean.cs");
        File.WriteAllText(file, """
            class C
            {
                void M() { }
            }
            """);

        var result = await WorkspaceAnalyzer.AnalyzeAsync([file]);

        // Score may not be exactly 100 due to upstream analyzer diagnostics (CA/IDE/RCS)
        Assert.True(result.Summary.IdiomaticScore >= 80,
            $"Expected score >= 80 but got {result.Summary.IdiomaticScore}");
    }

    [Fact]
    public async Task Respects_SuppressRules()
    {
        var file = Path.Combine(_tempDir, "Test.cs");
        File.WriteAllText(file, """
            class C
            {
                void M(object obj)
                {
                    if (obj is string)
                    {
                        var s = (string)obj;
                    }
                }
            }
            """);

        var result = await WorkspaceAnalyzer.AnalyzeAsync([file], suppressRules: ["PARL0004"]);

        Assert.DoesNotContain(result.Diagnostics, d => d.Diagnostic.RuleId == "PARL0004");
    }
}
