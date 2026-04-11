using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.Logging;

namespace Parlance.Mcp;

public sealed class ToolAnalytics : IDisposable, IAsyncDisposable
{
    private readonly ILogger<ToolAnalytics> _logger;
    private readonly StreamWriter? _writer;
    private readonly Lock _writeLock = new();

    public ToolAnalytics(ParlanceMcpConfiguration configuration, ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<ToolAnalytics>();

        try
        {
            Directory.CreateDirectory(configuration.AnalyticsPath);
            var fileName = $"session-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.log";
            var filePath = Path.Combine(configuration.AnalyticsPath, fileName);
            _writer = new StreamWriter(filePath, append: false) { AutoFlush = true };
            _logger.LogInformation("Analytics logging to {Path}", filePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to open analytics log file at {Path}. Analytics will be stderr-only",
                configuration.AnalyticsPath);
            _writer = null;
        }
    }

    public IDisposable TimeToolCall(string toolName, object? parameters = null)
    {
        var startTimestamp = Stopwatch.GetTimestamp();
        _logger.LogDebug("Tool call started: {ToolName}", toolName);
        return new ToolTimer(this, toolName, parameters, startTimestamp);
    }

    public void Flush() => _writer?.Flush();

    public void Dispose() => _writer?.Dispose();

    public async ValueTask DisposeAsync()
    {
        if (_writer is not null)
            await _writer.DisposeAsync();
    }

    private void WriteEntry(string toolName, object? parameters, TimeSpan elapsed, bool success)
    {
        _logger.LogDebug("Tool call completed: {ToolName} in {ElapsedMs:F1}ms", toolName, elapsed.TotalMilliseconds);

        if (_writer is null) return;

        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        var elapsedStr = $"{elapsed.TotalMilliseconds:F1}ms";
        var status = success ? "OK" : "Error";
        var paramsStr = FormatParameters(parameters);

        try
        {
            lock (_writeLock)
            {
                _writer.WriteLine($"{timestamp} | {toolName} | {elapsedStr} | {status} | {paramsStr}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write analytics entry for {ToolName}", toolName);
        }
    }

    private static string FormatParameters(object? parameters)
    {
        if (parameters is null) return "";

        var props = parameters.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var parts = new List<string>();
        foreach (var prop in props)
        {
            var value = prop.GetValue(parameters);
            if (value is null) continue;
            parts.Add($"{prop.Name}={value}");
        }
        return string.Join(", ", parts);
    }

    private sealed class ToolTimer(ToolAnalytics analytics, string toolName, object? parameters, long startTimestamp) : IDisposable
    {
        public void Dispose()
        {
            var elapsed = Stopwatch.GetElapsedTime(startTimestamp);
            analytics.WriteEntry(toolName, parameters, elapsed, success: true);
        }
    }
}
