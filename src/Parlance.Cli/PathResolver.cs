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

            // Treat as glob pattern
            var directory = Path.GetDirectoryName(input);
            var pattern = Path.GetFileName(input);

            if (string.IsNullOrEmpty(directory))
                directory = Directory.GetCurrentDirectory();

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
}
