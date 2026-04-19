using System.Collections.Immutable;
using System.ComponentModel;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using Parlance.CSharp.Workspace;

namespace Parlance.Mcp.Tools;

[McpServerToolType]
public sealed class ListProjectFilesTool
{
    [McpServerTool(Name = "list-project-files", ReadOnly = true)]
    [Description("List C# files loaded in the current Roslyn workspace. Supports optional project and wildcard path filtering.")]
    public static ListProjectFilesResult ListProjectFiles(
        WorkspaceSessionHolder holder,
        string? projectName = null,
        string? pathPattern = null,
        string pathStyle = "relative",
        int? maxFiles = null)
    {
        if (holder.LoadFailure is { } failure)
            return ListProjectFilesResult.LoadFailed(failure.Message);
        if (!holder.IsLoaded)
            return ListProjectFilesResult.NotLoaded();

        if (!IsKnownPathStyle(pathStyle))
            return ListProjectFilesResult.Failed($"Unknown pathStyle '{pathStyle}'. Use 'relative' or 'absolute'.");
        if (maxFiles is < 0)
            return ListProjectFilesResult.Failed("maxFiles must be greater than or equal to 0.");

        var session = holder.Session;
        var workspaceRoot = GetWorkspaceRoot(session.WorkspacePath);
        var matcher = string.IsNullOrWhiteSpace(pathPattern)
            ? null
            : new GlobPathPattern(NormalizeSlashes(pathPattern));

        var projects = session.CurrentSolution.Projects;
        if (!string.IsNullOrWhiteSpace(projectName))
            projects = projects.Where(p => string.Equals(p.Name, projectName, StringComparison.OrdinalIgnoreCase));

        var paths = projects
            .SelectMany(p => p.Documents)
            .Select(d => d.FilePath)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => Path.GetFullPath(p!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(p => new
            {
                Absolute = NormalizeSlashes(p),
                Relative = NormalizeSlashes(Path.GetRelativePath(workspaceRoot, p))
            })
            .Where(p => matcher?.IsMatch(p.Relative) != false)
            .OrderBy(p => p.Relative, StringComparer.OrdinalIgnoreCase)
            .ToImmutableList();

        var totalMatched = paths.Count;
        var limited = maxFiles is { } limit ? paths.Take(limit).ToImmutableList() : paths;
        var files = limited
            .Select(p => string.Equals(pathStyle, "absolute", StringComparison.OrdinalIgnoreCase) ? p.Absolute : p.Relative)
            .ToImmutableList();

        return ListProjectFilesResult.Success(
            session.SnapshotVersion,
            pathStyle.ToLowerInvariant(),
            pathPattern,
            totalMatched,
            files);
    }

    private static string GetWorkspaceRoot(string workspacePath) =>
        Path.GetDirectoryName(workspacePath) ?? workspacePath;

    private static bool IsKnownPathStyle(string pathStyle) =>
        string.Equals(pathStyle, "relative", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(pathStyle, "absolute", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeSlashes(string path) =>
        path.Replace('\\', '/');

    private sealed class GlobPathPattern(string pattern)
    {
        private readonly Regex _regex = new(
            "^" + string.Concat(Tokenize(pattern).Select(ToRegexFragment)) + "$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public bool IsMatch(string path) =>
            _regex.IsMatch(path);

        private static IEnumerable<string> Tokenize(string pattern)
        {
            for (var i = 0; i < pattern.Length;)
            {
                if (pattern.AsSpan(i).StartsWith("**/", StringComparison.Ordinal))
                {
                    yield return "**/";
                    i += 3;
                }
                else if (pattern.AsSpan(i).StartsWith("**", StringComparison.Ordinal))
                {
                    yield return "**";
                    i += 2;
                }
                else
                {
                    yield return pattern[i++].ToString();
                }
            }
        }

        private static string ToRegexFragment(string token) => token switch
        {
            "**/" => "(?:.*/)?",
            "**" => ".*",
            "*" => "[^/]*",
            "?" => "[^/]",
            _ => Regex.Escape(token)
        };
    }
}

public sealed record ListProjectFilesResult(
    string Status,
    string? Error,
    long SnapshotVersion,
    string PathStyle,
    string? PathPattern,
    int TotalMatched,
    int Returned,
    bool Truncated,
    ImmutableList<string> Files)
{
    public static ListProjectFilesResult LoadFailed(string message) => new(
        "load_failed", message, 0, "relative", null, 0, 0, false, []);

    public static ListProjectFilesResult NotLoaded() => new(
        "not_loaded", "Workspace is still loading", 0, "relative", null, 0, 0, false, []);

    public static ListProjectFilesResult Success(
        long snapshotVersion,
        string pathStyle,
        string? pathPattern,
        int totalMatched,
        ImmutableList<string> files) => new(
        "success", null, snapshotVersion, pathStyle, pathPattern, totalMatched,
        files.Count, files.Count < totalMatched, files);

    public static ListProjectFilesResult Failed(string message) => new(
        "error", message, 0, "relative", null, 0, 0, false, []);
}
