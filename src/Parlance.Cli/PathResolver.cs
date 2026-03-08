using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

namespace Parlance.Cli;

internal static class PathResolver
{
    public static IReadOnlyList<string> Resolve(string[] inputs)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var input in inputs)
        {
            if (File.Exists(input))
            {
                files.Add(Path.GetFullPath(input));
                continue;
            }

            if (Directory.Exists(input))
            {
                foreach (var cs in Directory.EnumerateFiles(input, "*.cs", SearchOption.AllDirectories))
                    files.Add(Path.GetFullPath(cs));
                continue;
            }

            // Treat as glob pattern — find the real root directory before any wildcard segment
            var (directory, pattern) = SplitGlobPattern(input);

            if (!Directory.Exists(directory))
                continue;

            var matcher = new Matcher();
            matcher.AddInclude(pattern);

            var result = matcher.Execute(new DirectoryInfoWrapper(new DirectoryInfo(directory)));
            foreach (var match in result.Files)
                files.Add(Path.GetFullPath(Path.Combine(directory, match.Path)));
        }

        return [.. files.Order(StringComparer.OrdinalIgnoreCase)];
    }

    internal static (string Directory, string Pattern) SplitGlobPattern(string input)
    {
        // Walk the path to find where wildcards begin, preserving the original
        // path prefix so we don't lose root separators (e.g. leading /)
        var separators = new[] { '/', '\\' };
        var pos = 0;
        var lastSepBeforeWildcard = -1;

        while (pos < input.Length)
        {
            var nextSep = input.IndexOfAny(separators, pos);
            var segmentEnd = nextSep < 0 ? input.Length : nextSep;
            var segment = input.AsSpan(pos, segmentEnd - pos);

            if (segment.Contains('*') || segment.Contains('?'))
            {
                // Everything before this segment is the root directory
                var pattern = input[pos..];
                var directory = lastSepBeforeWildcard >= 0
                    ? input[..lastSepBeforeWildcard]
                    : Directory.GetCurrentDirectory();

                if (string.IsNullOrEmpty(directory))
                    directory = Directory.GetCurrentDirectory();

                return (directory, pattern);
            }

            lastSepBeforeWildcard = nextSep;
            pos = nextSep < 0 ? input.Length : nextSep + 1;
        }

        // No wildcard found — use Path.GetDirectoryName / Path.GetFileName
        var dir = Path.GetDirectoryName(input);
        if (string.IsNullOrEmpty(dir))
            dir = Directory.GetCurrentDirectory();

        return (dir, Path.GetFileName(input));
    }
}
