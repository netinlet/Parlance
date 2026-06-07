using System.Text.Json;
using System.Text.Json.Serialization;
using Parlance.Abstractions;

namespace Parlance.Mcp.Serialization;

/// <summary>Writes <see cref="RepoPath"/> as a workspace-relative string using the ambient root.</summary>
public sealed class RepoPathJsonConverter(WorkspaceRootAccessor root) : JsonConverter<RepoPath>
{
    // RepoPath is output-only: tool DTOs are serialized to the client and never read back.
    // Write emits a workspace-relative string, so reconstructing an absolute RepoPath here is
    // impossible — fail fast rather than silently store a relative path in Absolute.
    public override RepoPath Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException("RepoPath is output-only; deserialization is not supported.");

    public override void Write(Utf8JsonWriter writer, RepoPath value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Relative(root.Root));
}
