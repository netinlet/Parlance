using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Parlance.CSharp.Workspace;

namespace Parlance.Analysis;

public static class AnalysisFileResolver
{
    public static ImmutableList<string> ResolveTargets(
        CSharpWorkspaceSession session,
        IEnumerable<string> targets,
        string? baseDirectory = null) =>
        [.. targets
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .SelectMany(t => ResolveTarget(session, t, baseDirectory))
            .Distinct(StringComparer.OrdinalIgnoreCase)];

    private static IEnumerable<string> ResolveTarget(CSharpWorkspaceSession session, string target, string? baseDirectory)
    {
        var fullPath = ResolvePath(target, baseDirectory);
        return Path.GetExtension(fullPath).ToLowerInvariant() switch
        {
            ".sln" when WorkspacePathEquals(session, fullPath) => GetSolutionFiles(session.CurrentSolution),
            ".sln" => throw new ArgumentException($"Solution '{fullPath}' is not the loaded workspace."),
            ".csproj" => GetProjectFiles(session.CurrentSolution, fullPath),
            _ => [fullPath],
        };
    }

    private static string ResolvePath(string path, string? baseDirectory) =>
        Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(baseDirectory ?? Environment.CurrentDirectory, path));

    private static IEnumerable<string> GetSolutionFiles(Solution solution) =>
        solution.Projects.SelectMany(GetDocumentFiles);

    private static IEnumerable<string> GetProjectFiles(Solution solution, string projectPath) =>
        MatchingProjects(solution, projectPath).SelectMany(GetDocumentFiles);

    private static IEnumerable<Project> MatchingProjects(Solution solution, string projectPath)
    {
        var projects = solution.Projects.Where(p => ProjectPathEquals(p, projectPath)).ToImmutableList();
        return projects.IsEmpty
            ? throw new ArgumentException($"Project '{projectPath}' is not loaded in the current workspace.")
            : projects;
    }

    private static bool ProjectPathEquals(Project project, string projectPath) =>
        project.FilePath is { } filePath &&
        string.Equals(Path.GetFullPath(filePath), projectPath, StringComparison.OrdinalIgnoreCase);

    private static bool WorkspacePathEquals(CSharpWorkspaceSession session, string workspacePath) =>
        string.Equals(Path.GetFullPath(session.WorkspacePath), workspacePath, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> GetDocumentFiles(Project project) =>
        project.Documents
            .Select(d => d.FilePath)
            .OfType<string>()
            .Where(File.Exists)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFullPath);
}
