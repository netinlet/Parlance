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

        var openOptions = options.OpenOptions with { LoggerFactory = loggerFactory };
        var outcome = await CSharpWorkspaceSession.TryOpenSolutionAsync(
            options.SolutionPath, openOptions, cancellationToken);
        var elapsed = Stopwatch.GetElapsedTime(startTimestamp);

        outcome.Switch(
            onSuccess: session =>
            {
                holder.SetSession(session);
                logger.LogInformation(
                    "Workspace loaded in {ElapsedMs:F0}ms: {Status}, {Count} project(s)",
                    elapsed.TotalMilliseconds, session.Health.Status, session.Projects.Count);
            },
            onFailure: reason =>
            {
                logger.LogError(
                    "Workspace load failed after {ElapsedMs:F0}ms: {SolutionPath}: {Message}",
                    elapsed.TotalMilliseconds, options.SolutionPath, reason.Message);
                holder.SetLoadFailure(reason);
            });
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Disposing workspace session");
        await holder.DisposeAsync();
    }
}
