using Microsoft.Extensions.Logging.Abstractions;

namespace Parlance.Mcp.Tests;

internal static class TestAnalytics
{
    private static readonly string TempDir = Path.Combine(Path.GetTempPath(), "parlance-test-analytics");

    public static ToolAnalytics Instance { get; } =
        new(new ParlanceMcpConfiguration("/fake/test.sln", TempDir), NullLoggerFactory.Instance);
}
