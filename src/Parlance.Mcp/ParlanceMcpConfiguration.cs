using Microsoft.Extensions.Logging;

namespace Parlance.Mcp;

public sealed record ParlanceMcpConfiguration(string SolutionPath, LogLevel MinimumLogLevel = LogLevel.Information)
{
    public static ParlanceMcpConfiguration FromArgs(string[] args)
    {
        var solutionPath = GetSolutionPath(args);
        var logLevel = GetLogLevel(args);

        return new ParlanceMcpConfiguration(Path.GetFullPath(solutionPath), logLevel);
    }

    private static string GetSolutionPath(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] is not "--solution-path")
                continue;

            if (i + 1 >= args.Length)
                throw new InvalidOperationException("--solution-path requires a value.");

            return args[i + 1];
        }

        var envValue = Environment.GetEnvironmentVariable("PARLANCE_SOLUTION_PATH");
        if (!string.IsNullOrWhiteSpace(envValue))
            return envValue;

        var discovered = DiscoverSolutionFile(Environment.CurrentDirectory);
        if (discovered is not null)
            return discovered;

        throw new InvalidOperationException(
            "Solution path is required. Use --solution-path <path>, set PARLANCE_SOLUTION_PATH environment variable, or run from a directory containing a .sln file.");
    }

    private static string? DiscoverSolutionFile(string directory)
    {
        var slnFiles = Directory.GetFiles(directory, "*.sln");
        return slnFiles.Length switch
        {
            1 => slnFiles[0],
            0 => null,
            _ => throw new InvalidOperationException(
                $"Multiple .sln files found in '{directory}'. Use --solution-path to specify which one.")
        };
    }

    private static LogLevel GetLogLevel(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] is not "--log-level")
                continue;

            if (i + 1 >= args.Length)
                throw new InvalidOperationException("--log-level requires a value.");

            if (Enum.TryParse<LogLevel>(args[i + 1], ignoreCase: true, out var level))
                return level;

            throw new InvalidOperationException(
                $"Invalid log level '{args[i + 1]}'. Valid values: {string.Join(", ", Enum.GetNames<LogLevel>())}");
        }

        return LogLevel.Information;
    }
}
