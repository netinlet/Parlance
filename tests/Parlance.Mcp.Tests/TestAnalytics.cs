using Microsoft.Extensions.Logging.Abstractions;

namespace Parlance.Mcp.Tests;

internal static class TestAnalytics
{
    // Per-process unique path avoids contention between parallel test runs.
    // Writer is intentionally not disposed — tool tests don't verify analytics output.
    public static ToolAnalytics Instance { get; } =
        new(new ParlanceMcpConfiguration("/fake/test.sln",
            Path.Combine(Path.GetTempPath(), $"parlance-test-{Guid.NewGuid():N}")),
            NullLoggerFactory.Instance);
}
