using Microsoft.Extensions.Logging.Abstractions;

namespace Parlance.Mcp.Tests;

public sealed class AnalyticsFilterTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"parlance-filter-test-{Guid.NewGuid():N}");
    private readonly ToolAnalytics _analytics;

    public AnalyticsFilterTests()
    {
        _analytics = new ToolAnalytics(
            new ParlanceMcpConfiguration("/fake/path.sln", _tempDir),
            NullLoggerFactory.Instance);
    }

    public void Dispose()
    {
        _analytics.Dispose();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private string ReadLog()
    {
        var files = Directory.GetFiles(_tempDir, "session-*.log");
        return files.Length == 0 ? string.Empty : File.ReadAllText(files[0]);
    }

    [Fact]
    public void Record_SuccessfulCall_LogsOK()
    {
        AnalyticsFilter.Record(_analytics, "describe-type", TimeSpan.FromMilliseconds(42), success: true, args: null);

        Assert.Contains("describe-type", ReadLog());
        Assert.Contains("OK", ReadLog());
    }

    [Fact]
    public void Record_FailedCall_LogsError()
    {
        AnalyticsFilter.Record(_analytics, "analyze", TimeSpan.FromMilliseconds(15), success: false, args: null);

        Assert.Contains("analyze", ReadLog());
        Assert.Contains("Error", ReadLog());
    }

    [Fact]
    public void Record_WithArgs_LogsArgs()
    {
        AnalyticsFilter.Record(_analytics, "search-symbols", TimeSpan.FromMilliseconds(8), success: true, args: "searchQuery=Handler");

        Assert.Contains("searchQuery=Handler", ReadLog());
    }
}
