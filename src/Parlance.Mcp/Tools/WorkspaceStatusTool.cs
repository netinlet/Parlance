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
        ToolAnalytics analytics,
        ILogger<WorkspaceStatusTool> logger)
    {
        using var _ = analytics.TimeToolCall("workspace-status");

        if (holder.LoadFailure is { } failure)
        {
            logger.LogWarning("Workspace load failed: {Message}", failure.Message);
            return WorkspaceStatusResult.FromLoadFailure(failure);
        }

        if (!holder.IsLoaded)
        {
            logger.LogDebug("Workspace not yet loaded, returning loading status");
            return WorkspaceStatusResult.Loading(configuration.SolutionPath);
        }

        return WorkspaceStatusResult.FromSession(holder.Session);
    }
}
