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
}
