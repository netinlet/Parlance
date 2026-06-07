using Parlance.Mcp.Tools;

namespace Parlance.Mcp.Tests.Tools;

public sealed class SnapshotStampTests
{
    [Fact]
    public void SearchSymbolsResult_CarriesSnapshotVersion()
    {
        var result = SearchSymbolsResult.Found("q", [], 0, false, 42);
        Assert.Equal(42, result.SnapshotVersion);
    }

    [Fact]
    public void DefaultSnapshotVersion_IsZero()
    {
        Assert.Equal(0, SearchSymbolsResult.NotLoaded().SnapshotVersion);
    }
}
