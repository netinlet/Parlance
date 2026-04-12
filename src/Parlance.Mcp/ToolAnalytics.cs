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
            _logger.LogWarning(ex, "Failed to open analytics log file at {Path}. Analytics will be disabled",
                configuration.AnalyticsPath);
            _writer = null;
        }
    }

    public void RecordCall(string toolName, TimeSpan elapsed, bool success, string? args = null)
    {
        _logger.LogDebug("Tool call completed: {ToolName} in {ElapsedMs:F1}ms success={Success}",
            toolName, elapsed.TotalMilliseconds, success);

        if (_writer is null) return;

        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        var elapsedStr = $"{elapsed.TotalMilliseconds:F1}ms";
        var status = success ? "OK" : "Error";

        try
        {
            lock (_writeLock)
            {
                _writer.WriteLine($"{timestamp} | {toolName} | {elapsedStr} | {status} | {args}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write analytics entry for {ToolName}", toolName);
        }
    }

    public void Dispose() => _writer?.Dispose();

    public async ValueTask DisposeAsync()
    {
        if (_writer is not null)
            await _writer.DisposeAsync();
    }
}
