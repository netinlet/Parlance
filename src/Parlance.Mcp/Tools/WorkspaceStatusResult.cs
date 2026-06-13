using System.Collections.Immutable;
using Parlance.Abstractions;
using Parlance.CSharp.Workspace;

namespace Parlance.Mcp.Tools;

public sealed record WorkspaceStatusResult(
    string Status,
    RepoPath SolutionPath,
    // The absolute repo anchor, kept verbatim (not relativized). SolutionPath serializes
    // workspace-relative like every other path, so this is the one field a client reads to recover
    // the absolute root and resolve those relative paths from a different working directory.
    string WorkspaceRoot,
    long SnapshotVersion,
    int ProjectCount,
    ImmutableList<ProjectStatusEntry> Projects,
    ImmutableList<DiagnosticEntry> Diagnostics,
    ImmutableList<string> Notices)
{
    public static WorkspaceStatusResult FromSession(
        CSharpWorkspaceSession session,
        ImmutableList<string> notices) =>
        new(
            Status: session.Health.Status.ToString(),
            SolutionPath: new RepoPath(session.WorkspacePath),
            WorkspaceRoot: session.Root.Absolute,
            SnapshotVersion: session.SnapshotVersion,
            ProjectCount: session.Projects.Count,
            Projects: session.Projects
                .Select(p => new ProjectStatusEntry(
                    Name: p.Name,
                    Path: p.ProjectPath.ToRepoPath(),
                    Status: p.Status.ToString(),
                    TargetFrameworks: p.TargetFrameworks,
                    LangVersion: p.LangVersion,
                    DependsOn: p.ProjectReferences))
                .ToImmutableList(),
            Diagnostics: session.Health.Diagnostics
                .Select(d => new DiagnosticEntry(d.Code, d.Message, d.Severity.ToString()))
                .ToImmutableList(),
            Notices: notices);

    public static WorkspaceStatusResult FromLoadFailure(
        WorkspaceLoadFailure failure,
        ImmutableList<string> notices) =>
        new(
            Status: "Failed",
            SolutionPath: new RepoPath(failure.SolutionPath),
            WorkspaceRoot: RepoPath.Containing(failure.SolutionPath).Absolute,
            SnapshotVersion: 0,
            ProjectCount: 0,
            Projects: [],
            Diagnostics: [new DiagnosticEntry("LoadFailure", failure.Message, "Error")],
            Notices: notices);

    public static WorkspaceStatusResult Loading(string solutionPath, ImmutableList<string> notices) =>
        new(
            Status: "Loading",
            SolutionPath: new RepoPath(solutionPath),
            WorkspaceRoot: RepoPath.Containing(solutionPath).Absolute,
            SnapshotVersion: 0,
            ProjectCount: 0,
            Projects: [],
            Diagnostics: [],
            Notices: notices);
}

public sealed record ProjectStatusEntry(
    string Name,
    RepoPath? Path,
    string Status,
    ImmutableList<string> TargetFrameworks,
    string? LangVersion,
    ImmutableList<string> DependsOn);

public sealed record DiagnosticEntry(string Code, string Message, string Severity);
