using Microsoft.Extensions.Logging;

namespace Parlance.Cli.Logging;

/// <summary>
/// A logger provider that writes error-level (and above) messages to stderr,
/// keeping diagnostic output separate from the CLI's structured result output on stdout.
/// </summary>
internal sealed class StderrLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new StderrLogger();

    public void Dispose() { }

    private sealed class StderrLogger : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            Console.Error.WriteLine(formatter(state, exception));
        }
    }
}
