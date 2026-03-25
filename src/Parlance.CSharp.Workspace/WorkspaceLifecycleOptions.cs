namespace Parlance.CSharp.Workspace;

public sealed record WorkspaceLifecycleOptions(
    string SolutionPath,
    WorkspaceOpenOptions OpenOptions);
