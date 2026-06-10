using System.Text.Json;
using Parlance.Mcp.Serialization;
using Parlance.Mcp.Tools;

namespace Parlance.Mcp.Tests.Tools;

public sealed class FieldCutTests
{
    private static readonly JsonSerializerOptions Json = ParlanceToolJson.Create();

    [Fact]
    public void SymbolMatch_HasNoDisplayName()
    {
        Assert.Null(typeof(SymbolMatch).GetProperty("DisplayName"));
    }

    [Fact]
    public void DescribeTypeResult_HasNoName()
    {
        Assert.Null(typeof(DescribeTypeResult).GetProperty("Name"));
    }

    [Fact]
    public void GetTypeAtResult_HasNoTypeName()
    {
        Assert.Null(typeof(GetTypeAtResult).GetProperty("TypeName"));
    }

    [Fact]
    public void MemberEntry_HasNoName()
    {
        Assert.Null(typeof(MemberEntry).GetProperty("Name"));
    }

    [Fact]
    public void OutlineMember_HasNoName()
    {
        Assert.Null(typeof(OutlineMember).GetProperty("Name"));
    }

    [Fact]
    public void OutlineFileResult_OmitsFilePathOnSuccess()
    {
        var json = JsonSerializer.Serialize(OutlineFileResult.Found([], 0), Json);
        Assert.DoesNotContain("filePath", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnalyzeResult_KeepsEmptyDiagnosticsArray()
    {
        // [KeepWhenEmpty] exempts the analyze diagnostics list from the global empty-collection drop:
        // "analyzed, zero diagnostics" must survive as [] so a client can tell clean from never-run.
        var clean = AnalyzeToolResult.Success("default", new AnalyzeSummary(0, 0, 0, 0, 100), [], 5);
        var json = JsonSerializer.Serialize(clean, Json);
        Assert.Contains("\"diagnostics\":[]", json);
    }

    [Fact]
    public void DropEmptyCollections_StillDropsUnmarkedEmptyArrays()
    {
        // The opt-out is scoped: an unmarked empty list still vanishes (the global payload win).
        var json = JsonSerializer.Serialize(SearchSymbolsResult.NoMatches("q", 5), Json);
        Assert.DoesNotContain("\"matches\"", json);
    }
}
