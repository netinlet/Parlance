using System.ComponentModel;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Parlance.CSharp.Workspace;

namespace Parlance.Mcp.Tools;

[McpServerToolType]
public sealed class WorkspaceStatusTool
{
    [McpServerTool(Name = "workspace-status", ReadOnly = true)]
    [Description(
        "Returns workspace health, loaded projects, target frameworks, language versions, and project dependencies")]
    public static WorkspaceStatusResult GetStatus(
        WorkspaceSessionHolder holder,
        ParlanceMcpConfiguration configuration,
        ILogger<WorkspaceStatusTool> logger)
    {
        switch (holder.State)
        {
            case WorkspaceState.LoadFailed failed:
                logger.LogWarning("Workspace load failed: {Message}", failed.Failure.Message);
                return WorkspaceStatusResult.FromLoadFailure(failed.Failure);
            case WorkspaceState.NotLoaded:
            case WorkspaceState.Disposed:
                logger.LogDebug("Workspace not yet loaded, returning loading status");
                return WorkspaceStatusResult.Loading(configuration.SolutionPath);
            case WorkspaceState.Loaded loaded:
                return WorkspaceStatusResult.FromSession(loaded.Session);
        }

        throw new InvalidOperationException("Unreachable");
    }
}
