using System.Collections.Immutable;
using System.Text.Json;
using Parlance.Mcp.Serialization;

namespace Parlance.Mcp.Tests.Serialization;

public sealed class ParlanceToolJsonTests
{
    private sealed record Sample(string Signature, ImmutableList<string> Items);

    private static readonly JsonSerializerOptions Options = ParlanceToolJson.Create();

    [Fact]
    public void Generics_AreNotHtmlEscaped()
    {
        var json = JsonSerializer.Serialize(new Sample("Task<SearchSymbolsResult>", ["x"]), Options);
        Assert.Contains("Task<SearchSymbolsResult>", json);
        Assert.DoesNotContain("\\u003C", json);
    }

    [Fact]
    public void EmptyCollections_AreDropped()
    {
        var json = JsonSerializer.Serialize(new Sample("s", []), Options);
        Assert.DoesNotContain("items", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NonEmptyCollections_AreKept()
    {
        var json = JsonSerializer.Serialize(new Sample("s", ["a"]), Options);
        Assert.Contains("\"items\":[\"a\"]", json);
    }
}
