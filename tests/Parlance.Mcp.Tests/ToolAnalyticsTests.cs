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
    public void RecordCall_WritesEntryToFile()
    {
        using var analytics = CreateAnalytics();

        analytics.RecordCall("describe-type", TimeSpan.FromMilliseconds(42), success: true, args: "typeName=Foo");

        var files = Directory.GetFiles(_tempDir, "session-*.log");
        Assert.Single(files);
        var content = File.ReadAllText(files[0]);
        Assert.Contains("describe-type", content);
        Assert.Contains("OK", content);
        Assert.Contains("typeName=Foo", content);
    }

    [Fact]
    public void RecordCall_ErrorStatus_WritesError()
    {
        using var analytics = CreateAnalytics();

        analytics.RecordCall("search-symbols", TimeSpan.FromMilliseconds(10), success: false, args: null);

        var files = Directory.GetFiles(_tempDir, "session-*.log");
        var content = File.ReadAllText(files[0]);
        Assert.Contains("Error", content);
        Assert.DoesNotContain("OK", content);
    }

    [Fact]
    public void RecordCall_NullArgs_WritesEmptyArgsField()
    {
        using var analytics = CreateAnalytics();

        analytics.RecordCall("workspace-status", TimeSpan.FromMilliseconds(5), success: true, args: null);

        var files = Directory.GetFiles(_tempDir, "session-*.log");
        var lines = File.ReadAllLines(files[0]).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
        Assert.Single(lines);
        Assert.EndsWith("|", lines[0].TrimEnd());
    }

    [Fact]
    public void RecordCall_MultipleCalls_AllWrittenToSameFile()
    {
        using var analytics = CreateAnalytics();

        analytics.RecordCall("describe-type", TimeSpan.FromMilliseconds(10), success: true, args: null);
        analytics.RecordCall("find-references", TimeSpan.FromMilliseconds(20), success: true, args: null);
        analytics.RecordCall("workspace-status", TimeSpan.FromMilliseconds(5), success: false, args: null);

        var files = Directory.GetFiles(_tempDir, "session-*.log");
        Assert.Single(files);
        var lines = File.ReadAllLines(files[0]).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
        Assert.Equal(3, lines.Length);
    }

    [Fact]
    public void RecordCall_EntryFormat_HasExpectedPipeDelimitedStructure()
    {
        using var analytics = CreateAnalytics();

        analytics.RecordCall("analyze", TimeSpan.FromMilliseconds(99), success: true, args: "files=test.cs");

        var files = Directory.GetFiles(_tempDir, "session-*.log");
        var line = File.ReadAllLines(files[0]).First(l => !string.IsNullOrWhiteSpace(l));
        var parts = line.Split(" | ");
        Assert.Equal(5, parts.Length); // timestamp | tool | elapsed | status | args
        Assert.Contains("analyze", parts[1]);
        Assert.EndsWith("ms", parts[2].Trim());
        Assert.Equal("OK", parts[3].Trim());
        Assert.Contains("files=test.cs", parts[4]);
    }

    [Fact]
    public void CreatesDirectoryIfMissing()
    {
        var nested = Path.Combine(_tempDir, "nested", "deep");
        using var analytics = new ToolAnalytics(
            new ParlanceMcpConfiguration("/fake/path.sln", nested), NullLoggerFactory.Instance);

        analytics.RecordCall("workspace-status", TimeSpan.FromMilliseconds(1), success: true, args: null);

        Assert.True(Directory.Exists(nested));
        Assert.Single(Directory.GetFiles(nested, "session-*.log"));
    }

    [Fact]
    public void InvalidPath_DoesNotThrow_LogsDegraded()
    {
        using var analytics = new ToolAnalytics(
            new ParlanceMcpConfiguration("/fake/path.sln", "/\0invalid/path"), NullLoggerFactory.Instance);

        // Should not throw — analytics is non-critical
        analytics.RecordCall("workspace-status", TimeSpan.FromMilliseconds(1), success: true, args: null);
    }
}
