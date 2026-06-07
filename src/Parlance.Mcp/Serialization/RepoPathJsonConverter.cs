using System.Text.Json;
using System.Text.Json.Serialization;
using Parlance.Abstractions;

namespace Parlance.Mcp.Serialization;

/// <summary>Writes <see cref="RepoPath"/> as a workspace-relative string using the ambient root.</summary>
public sealed class RepoPathJsonConverter(WorkspaceRootAccessor root) : JsonConverter<RepoPath>
{
    public override RepoPath Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(reader.GetString() ?? "");

    public override void Write(Utf8JsonWriter writer, RepoPath value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Relative(root.Root));
}
