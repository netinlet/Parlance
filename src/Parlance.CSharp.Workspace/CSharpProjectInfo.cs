namespace Parlance.CSharp.Workspace;

public sealed record CSharpProjectInfo(
    WorkspaceProjectKey Key,
    string Name,
    string ProjectPath,
    IReadOnlyList<string> TargetFrameworks,
    string? ActiveTargetFramework,
    string? LangVersion,
    ProjectLoadStatus Status,
    IReadOnlyList<WorkspaceDiagnostic> Diagnostics);
