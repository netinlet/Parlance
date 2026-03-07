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
    public async Task Fixes_PARL9001_UsingStatement()
    {
        var file = Path.Combine(_tempDir, "Test.cs");
        File.WriteAllText(file, """
            using System;
            using System.IO;
            class C
            {
                void M()
                {
                    using (var stream = new MemoryStream())
                    {
                        stream.WriteByte(1);
                    }
                }
            }
            """);

        var result = await WorkspaceFixer.FixAsync([file]);

        Assert.Single(result.FixedFiles);
        Assert.Contains("using var stream", result.FixedFiles[0].NewContent);
        Assert.DoesNotContain("using (var stream", result.FixedFiles[0].NewContent);
    }

    [Fact]
    public async Task Fixes_PARL0004_PatternMatching()
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
                        System.Console.WriteLine(s);
                    }
                }
            }
            """);

        var result = await WorkspaceFixer.FixAsync([file]);

        Assert.Single(result.FixedFiles);
        Assert.Contains("obj is string s", result.FixedFiles[0].NewContent);
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

    [Fact]
    public async Task Apply_WritesFiles()
    {
        var file = Path.Combine(_tempDir, "Test.cs");
        File.WriteAllText(file, """
            using System;
            using System.IO;
            class C
            {
                void M()
                {
                    using (var stream = new MemoryStream())
                    {
                        stream.WriteByte(1);
                    }
                }
            }
            """);

        var result = await WorkspaceFixer.FixAsync([file]);
        WorkspaceFixer.ApplyFixes(result);

        var written = File.ReadAllText(file);
        Assert.Contains("using var stream", written);
    }
}
