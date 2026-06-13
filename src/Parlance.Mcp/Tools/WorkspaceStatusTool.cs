using System.ComponentModel;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Parlance.Abstractions;
using Parlance.Analysis;
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
        AnalyzerProvider analyzerProvider,
        ILogger<WorkspaceStatusTool> logger)
    {
        var repoPath = RepoPath.Containing(configuration.SolutionPath).Absolute;
        var notices = analyzerProvider.GetExternalSourceNotices(repoPath);

        WorkspaceStatusResult Loading()
        {
            logger.LogDebug("Workspace not yet loaded, returning loading status");
            return WorkspaceStatusResult.Loading(configuration.SolutionPath, notices);
        }

        return holder.State.Match(
            notLoaded: Loading,
            loaded: session => WorkspaceStatusResult.FromSession(session, notices),
            loadFailed: failure =>
            {
                logger.LogWarning("Workspace load failed: {Message}", failure.Message);
                return WorkspaceStatusResult.FromLoadFailure(failure, notices);
            },
            disposed: Loading);
    }
}
