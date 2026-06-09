using System.Text.Json;
using System.Text.Json.Serialization;
using Parlance.Abstractions;
using Parlance.CSharp.Workspace;

namespace Parlance.Mcp.Serialization;

/// <summary>
/// Writes <see cref="RepoPath"/> as a workspace-relative string, reading the active root from the
/// single source of truth — the loaded <see cref="CSharpWorkspaceSession.Root"/> — rather than a
/// duplicated mutable global. Before the session loads (and after a load failure) it falls back to
/// the directory of the configured solution path, so a <c>workspace-status</c> served during loading
/// still relativizes instead of leaking an absolute path.
/// </summary>
public sealed class RepoPathJsonConverter(WorkspaceSessionHolder holder, WorkspaceLifecycleOptions options)
    : JsonConverter<RepoPath>
{
    // RepoPath is output-only: tool DTOs are serialized to the client and never read back.
    // Write emits a workspace-relative string, so reconstructing an absolute RepoPath here is
    // impossible — fail fast rather than silently store a relative path in Absolute.
    public override RepoPath Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException("RepoPath is output-only; deserialization is not supported.");

    public override void Write(Utf8JsonWriter writer, RepoPath value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Relative(CurrentRoot()));

    private RepoPath CurrentRoot() =>
        holder.State is WorkspaceState.Loaded loaded
            ? loaded.Session.Root
            : RepoPath.Containing(options.SolutionPath);
}
