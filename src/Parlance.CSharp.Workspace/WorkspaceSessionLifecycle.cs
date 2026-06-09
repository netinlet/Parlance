using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Parlance.Abstractions;

namespace Parlance.CSharp.Workspace;

public sealed class WorkspaceSessionLifecycle(
    WorkspaceSessionHolder holder,
    WorkspaceRootAccessor rootAccessor,
    WorkspaceLifecycleOptions options,
    ILoggerFactory loggerFactory,
    ILogger<WorkspaceSessionLifecycle> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Loading workspace: {SolutionPath}", options.SolutionPath);
        var startTimestamp = Stopwatch.GetTimestamp();

        // Publish the root from the configured solution path's directory up front so that
        // workspace-status requests served while the workspace is still loading — or after a load
        // failure — serialize repo-relative paths instead of leaking absolute ones (the root is
        // otherwise only set on successful load). On success it is reassigned to session.Root
        // (the same value).
        rootAccessor.Root = RepoPath.Containing(options.SolutionPath);

        var openOptions = options.OpenOptions with { LoggerFactory = loggerFactory };
        var outcome = await CSharpWorkspaceSession.TryOpenSolutionAsync(
            options.SolutionPath, openOptions, cancellationToken);
        var elapsed = Stopwatch.GetElapsedTime(startTimestamp);

        outcome.Switch(
            onSuccess: session =>
            {
                // Publish the root BEFORE the session: SetSession flips holder state to Loaded, and
                // the stdio transport can service a request between these two statements. If Root is
                // still "" when a loaded-branch request serializes, every RepoPath emits an absolute
                // path. Assigning Root first closes that window.
                rootAccessor.Root = session.Root;
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
