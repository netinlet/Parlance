using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Parlance.CSharp.Workspace;

namespace Parlance.Mcp;

internal sealed class WorkspaceSessionLifecycle(
    WorkspaceSessionHolder holder,
    ParlanceMcpConfiguration configuration,
    ILoggerFactory loggerFactory,
    ILogger<WorkspaceSessionLifecycle> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Loading workspace: {SolutionPath}", configuration.SolutionPath);
        var startTimestamp = Stopwatch.GetTimestamp();

        try
        {
            var session = await CSharpWorkspaceSession.OpenSolutionAsync(
                configuration.SolutionPath,
                new WorkspaceOpenOptions(
                    Mode: WorkspaceMode.Server,
                    EnableFileWatching: true,
                    LoggerFactory: loggerFactory),
                cancellationToken);

            holder.SetSession(session);

            var elapsed = Stopwatch.GetElapsedTime(startTimestamp);
            logger.LogInformation(
                "Workspace loaded in {ElapsedMs:F0}ms: {Status}, {Count} project(s)",
                elapsed.TotalMilliseconds, session.Health.Status, session.Projects.Count);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var elapsed = Stopwatch.GetElapsedTime(startTimestamp);
            logger.LogError(ex,
                "Workspace load failed after {ElapsedMs:F0}ms: {SolutionPath}",
                elapsed.TotalMilliseconds, configuration.SolutionPath);

            holder.SetLoadFailure(new WorkspaceLoadFailure(ex.Message, configuration.SolutionPath));
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Disposing workspace session");
        await holder.DisposeAsync();
    }
}
