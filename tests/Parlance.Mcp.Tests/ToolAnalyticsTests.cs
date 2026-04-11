using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Parlance.Mcp.Tests;

public sealed class ToolAnalyticsTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"parlance-test-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private ToolAnalytics CreateAnalytics() =>
        new(new ParlanceMcpConfiguration("/fake/path.sln", _tempDir), NullLoggerFactory.Instance);

    [Fact]
    public void TimeToolCall_WritesEntryToFile()
    {
        using var analytics = CreateAnalytics();

        using (analytics.TimeToolCall("describe-type", new { typeName = "Foo" }))
        {
            // simulate work
        }

        analytics.Flush();

        var files = Directory.GetFiles(_tempDir, "session-*.log");
        Assert.Single(files);

        var content = File.ReadAllText(files[0]);
        Assert.Contains("describe-type", content);
        Assert.Contains("typeName=Foo", content);
        Assert.Contains("OK", content);
    }

    [Fact]
    public void TimeToolCall_NoParams_WritesEmptyParamsField()
    {
        using var analytics = CreateAnalytics();

        using (analytics.TimeToolCall("workspace-status"))
        {
        }

        analytics.Flush();

        var files = Directory.GetFiles(_tempDir, "session-*.log");
        var lines = File.ReadAllLines(files[0]).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
        Assert.Single(lines);
        // Line ends with empty params: "... | OK |"
        Assert.EndsWith("|", lines[0].TrimEnd());
    }

    [Fact]
    public void TimeToolCall_MultipleParams_FormatsCorrectly()
    {
        using var analytics = CreateAnalytics();

        using (analytics.TimeToolCall("search-symbols", new { searchQuery = "Handler", kind = "method", maxResults = 25 }))
        {
        }

        analytics.Flush();

        var files = Directory.GetFiles(_tempDir, "session-*.log");
        var content = File.ReadAllText(files[0]);
        Assert.Contains("searchQuery=Handler", content);
        Assert.Contains("kind=method", content);
        Assert.Contains("maxResults=25", content);
    }

    [Fact]
    public void TimeToolCall_NullParamValues_Skipped()
    {
        using var analytics = CreateAnalytics();

        using (analytics.TimeToolCall("goto-definition", new { symbolName = "Foo", filePath = (string?)null }))
        {
        }

        analytics.Flush();

        var files = Directory.GetFiles(_tempDir, "session-*.log");
        var content = File.ReadAllText(files[0]);
        Assert.Contains("symbolName=Foo", content);
        Assert.DoesNotContain("filePath", content);
    }

    [Fact]
    public void CreatesDirectoryIfMissing()
    {
        var nested = Path.Combine(_tempDir, "nested", "deep");
        using var analytics = new ToolAnalytics(
            new ParlanceMcpConfiguration("/fake/path.sln", nested), NullLoggerFactory.Instance);

        using (analytics.TimeToolCall("workspace-status"))
        {
        }

        analytics.Flush();

        Assert.True(Directory.Exists(nested));
        Assert.Single(Directory.GetFiles(nested, "session-*.log"));
    }

    [Fact]
    public void InvalidPath_DoesNotThrow_LogsDegraded()
    {
        // Use an invalid path that can't be created
        using var analytics = new ToolAnalytics(
            new ParlanceMcpConfiguration("/fake/path.sln", "/\0invalid/path"), NullLoggerFactory.Instance);

        // Should not throw — analytics is non-critical
        using (analytics.TimeToolCall("workspace-status"))
        {
        }
    }

    [Fact]
    public void MultipleCalls_AllWrittenToSameFile()
    {
        using var analytics = CreateAnalytics();

        using (analytics.TimeToolCall("describe-type", new { typeName = "A" })) { }
        using (analytics.TimeToolCall("find-references", new { symbolName = "B" })) { }
        using (analytics.TimeToolCall("workspace-status")) { }

        analytics.Flush();

        var files = Directory.GetFiles(_tempDir, "session-*.log");
        Assert.Single(files);
        var lines = File.ReadAllLines(files[0]).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
        Assert.Equal(3, lines.Length);
    }

    [Fact]
    public void EntryFormat_HasExpectedPipeDelimitedStructure()
    {
        using var analytics = CreateAnalytics();

        using (analytics.TimeToolCall("analyze", new { files = "test.cs" })) { }

        analytics.Flush();

        var files = Directory.GetFiles(_tempDir, "session-*.log");
        var line = File.ReadAllLines(files[0]).First(l => !string.IsNullOrWhiteSpace(l));
        var parts = line.Split(" | ");
        Assert.Equal(5, parts.Length); // timestamp | tool | elapsed | status | params
        Assert.Contains("analyze", parts[1]);
        Assert.EndsWith("ms", parts[2].Trim());
        Assert.Equal("OK", parts[3].Trim());
        Assert.Contains("files=test.cs", parts[4]);
    }
}
