using System.Text.Json;
using Parlance.Abstractions;
using Parlance.CSharp.Workspace;
using Parlance.Mcp.Serialization;

namespace Parlance.Mcp.Tests.Serialization;

public sealed class RepoPathJsonConverterTests
{
    private sealed record Holder(RepoPath Path, RepoPath? MaybePath);

    // A fresh holder is NotLoaded, so the converter falls back to the configured solution's
    // directory — RepoPath.Containing(<root>/Dummy.slnx) == <root>. That exercises the same Write
    // path without standing up a real workspace session.
    private static JsonSerializerOptions OptionsWithRoot(string root)
    {
        var options = new WorkspaceLifecycleOptions(
            Path.Combine(root, "Dummy.slnx"), new WorkspaceOpenOptions());
        return ParlanceToolJson.Create(new RepoPathJsonConverter(new WorkspaceSessionHolder(), options));
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
