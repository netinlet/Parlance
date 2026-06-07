using Parlance.Mcp.Tools;

namespace Parlance.Mcp.Tests.Tools;

public sealed class StalenessTests
{
    [Fact]
    public void Stale_HasStaleStatusAndActualVersion()
    {
        var result = AnalyzeToolResult.Stale(actual: 7, expected: 3);
        Assert.Equal("stale", result.Status);
        Assert.Equal(7, result.SnapshotVersion);
    }
}
