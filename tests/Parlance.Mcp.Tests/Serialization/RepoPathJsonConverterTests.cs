using System.Text.Json;
using Parlance.Abstractions; // RepoPath + WorkspaceRootAccessor both live here
using Parlance.Mcp.Serialization;

namespace Parlance.Mcp.Tests.Serialization;

public sealed class RepoPathJsonConverterTests
{
    private sealed record Holder(RepoPath Path, RepoPath? MaybePath);

    private static JsonSerializerOptions OptionsWithRoot(string root)
    {
        var accessor = new WorkspaceRootAccessor { Root = root };
        return ParlanceToolJson.Create(new RepoPathJsonConverter(accessor));
    }

    [Fact]
    public void Write_EmitsWorkspaceRelative()
    {
        var json = JsonSerializer.Serialize(
            new Holder(new RepoPath("/repo/src/Foo.cs"), null), OptionsWithRoot("/repo"));
        Assert.Contains("src/Foo.cs", json.Replace('\\', '/'));
        Assert.DoesNotContain("/repo/src/Foo.cs", json);
    }

    [Fact]
    public void Write_NullRepoPath_IsOmitted()
    {
        var json = JsonSerializer.Serialize(
            new Holder(new RepoPath("/repo/a.cs"), null), OptionsWithRoot("/repo"));
        Assert.DoesNotContain("maybePath", json, StringComparison.OrdinalIgnoreCase);
    }
}
