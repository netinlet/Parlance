using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Parlance.CSharp.Workspace;

public sealed class WorkspaceSessionLifecycle(
    WorkspaceSessionHolder holder,
    WorkspaceLifecycleOptions options,
    ILoggerFactory loggerFactory,
    ILogger<WorkspaceSessionLifecycle> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Loading workspace: {SolutionPath}", options.SolutionPath);
        var startTimestamp = Stopwatch.GetTimestamp();

        try
        {
            var openOptions = options.OpenOptions with { LoggerFactory = loggerFactory };
            var session = await CSharpWorkspaceSession.OpenSolutionAsync(
                options.SolutionPath, openOptions, cancellationToken);

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
                elapsed.TotalMilliseconds, options.SolutionPath);

            holder.SetLoadFailure(new WorkspaceLoadFailure(ex.Message, options.SolutionPath));
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Disposing workspace session");
        await holder.DisposeAsync();
    }
}
