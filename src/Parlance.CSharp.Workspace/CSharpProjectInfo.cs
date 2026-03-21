using System.Collections.Immutable;

namespace Parlance.CSharp.Workspace;

public sealed record CSharpProjectInfo(
    WorkspaceProjectKey Key,
    string Name,
    string ProjectPath,
    ImmutableList<string> TargetFrameworks,
    string? ActiveTargetFramework,
    string? LangVersion,
    ProjectLoadStatus Status,
    ImmutableList<WorkspaceDiagnostic> Diagnostics,
    ImmutableList<string> ProjectReferences);
