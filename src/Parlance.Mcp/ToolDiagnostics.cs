using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Parlance.Mcp;

internal static class ToolDiagnostics
{
    public static IDisposable TimeToolCall(ILogger logger, string toolName)
    {
        var startTimestamp = Stopwatch.GetTimestamp();
        logger.LogDebug("Tool call started: {ToolName}", toolName);
        return new ToolTimer(logger, toolName, startTimestamp);
    }

    private sealed class ToolTimer(ILogger logger, string toolName, long startTimestamp) : IDisposable
    {
        public void Dispose()
        {
            var elapsed = Stopwatch.GetElapsedTime(startTimestamp);
            logger.LogDebug("Tool call completed: {ToolName} in {ElapsedMs:F1}ms",
                toolName, elapsed.TotalMilliseconds);
        }
    }
}
