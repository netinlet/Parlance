using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Parlance.Mcp;

internal static class AnalyticsFilter
{
    internal static McpRequestFilter<CallToolRequestParams, CallToolResult> Create(ToolAnalytics analytics) =>
        next => async (request, ct) =>
        {
            var toolName = request.Params?.Name ?? "unknown";
            var args = request.Params?.Arguments is { } a
                ? JsonSerializer.Serialize(a)
                : null;
            var start = Stopwatch.GetTimestamp();
            try
            {
                var result = await next(request, ct);
                Record(analytics, toolName, Stopwatch.GetElapsedTime(start), success: result.IsError != true, args);
                return result;
            }
            catch
            {
                Record(analytics, toolName, Stopwatch.GetElapsedTime(start), success: false, args);
                throw;
            }
        };

    internal static void Record(ToolAnalytics analytics, string toolName, TimeSpan elapsed, bool success, string? args) =>
        analytics.RecordCall(toolName, elapsed, success, args);
}
