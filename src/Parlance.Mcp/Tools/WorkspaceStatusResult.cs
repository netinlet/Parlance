using System.Collections.Immutable;
using Parlance.CSharp.Workspace;

namespace Parlance.Mcp.Tools;

public sealed record WorkspaceStatusResult(
    string Status,
    string SolutionPath,
    long SnapshotVersion,
    int ProjectCount,
    ImmutableList<ProjectStatusEntry> Projects,
    ImmutableList<DiagnosticEntry> Diagnostics)
{
    public static WorkspaceStatusResult FromSession(CSharpWorkspaceSession session) =>
        new(
            Status: session.Health.Status.ToString(),
            SolutionPath: session.WorkspacePath,
            SnapshotVersion: session.SnapshotVersion,
            ProjectCount: session.Projects.Count,
            Projects: session.Projects
                .Select(p => new ProjectStatusEntry(
                    Name: p.Name,
                    Path: p.ProjectPath,
                    Status: p.Status.ToString(),
                    TargetFramework: p.ActiveTargetFramework,
                    TargetFrameworks: p.TargetFrameworks,
                    LangVersion: p.LangVersion,
                    DependsOn: p.ProjectReferences))
                .ToImmutableList(),
            Diagnostics: session.Health.Diagnostics
                .Select(d => new DiagnosticEntry(d.Code, d.Message, d.Severity.ToString()))
                .ToImmutableList());

    public static WorkspaceStatusResult FromLoadFailure(WorkspaceLoadFailure failure) =>
        new(
            Status: "Failed",
            SolutionPath: failure.SolutionPath,
            SnapshotVersion: 0,
            ProjectCount: 0,
            Projects: [],
            Diagnostics: [new DiagnosticEntry("LoadFailure", failure.Message, "Error")]);

    public static WorkspaceStatusResult Loading() =>
        new(
            Status: "Loading",
            SolutionPath: "",
            SnapshotVersion: 0,
            ProjectCount: 0,
            Projects: [],
            Diagnostics: []);
}

public sealed record ProjectStatusEntry(
    string Name,
    string Path,
    string Status,
    string? TargetFramework,
    ImmutableList<string> TargetFrameworks,
    string? LangVersion,
    ImmutableList<string> DependsOn);

public sealed record DiagnosticEntry(string Code, string Message, string Severity);
