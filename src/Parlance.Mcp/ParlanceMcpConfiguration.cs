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
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] is "--solution-path")
                return args[i + 1];
        }

        var envValue = Environment.GetEnvironmentVariable("PARLANCE_SOLUTION_PATH");
        if (!string.IsNullOrWhiteSpace(envValue))
            return envValue;

        throw new InvalidOperationException(
            "Solution path is required. Use --solution-path <path> or set PARLANCE_SOLUTION_PATH environment variable.");
    }

    private static LogLevel GetLogLevel(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] is "--log-level")
            {
                if (Enum.TryParse<LogLevel>(args[i + 1], ignoreCase: true, out var level))
                    return level;

                throw new InvalidOperationException(
                    $"Invalid log level '{args[i + 1]}'. Valid values: {string.Join(", ", Enum.GetNames<LogLevel>())}");
            }
        }

        return LogLevel.Information;
    }
}
