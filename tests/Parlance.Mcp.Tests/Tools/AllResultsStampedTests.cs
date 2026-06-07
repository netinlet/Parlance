using System.Reflection;
using Parlance.Mcp.Tools;

namespace Parlance.Mcp.Tests.Tools;

public sealed class AllResultsStampedTests
{
    [Fact]
    public void EveryToolResult_HasSnapshotVersionProperty()
    {
        var resultTypes = typeof(SearchSymbolsResult).Assembly.GetTypes()
            .Where(t => t.Namespace == "Parlance.Mcp.Tools"
                        && t.Name.EndsWith("Result", StringComparison.Ordinal)
                        && !t.IsAbstract);

        var missing = resultTypes
            .Where(t => t.GetProperty("SnapshotVersion") is null)
            .Select(t => t.Name)
            .ToList();

        Assert.True(missing.Count == 0, "Missing SnapshotVersion: " + string.Join(", ", missing));
    }
}
