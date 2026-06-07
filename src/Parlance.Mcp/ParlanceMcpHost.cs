using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Parlance.Abstractions;
using Parlance.Analysis;
using Parlance.Analysis.Curation;
using Parlance.CSharp.Workspace;
using Parlance.Mcp.Serialization;
using Parlance.Mcp.Tools;

namespace Parlance.Mcp;

public static class ParlanceMcpHost
{
    public static async Task RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var configuration = ParlanceMcpConfiguration.FromArgs(args);

        var builder = Host.CreateApplicationBuilder(args);

        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(configuration.MinimumLogLevel);
        builder.Logging.AddConsole(options =>
        {
            options.LogToStandardErrorThreshold = LogLevel.Trace;
        });

        builder.Services.AddSingleton(configuration);
        builder.Services.AddSingleton(new WorkspaceLifecycleOptions(
            configuration.SolutionPath,
            new WorkspaceOpenOptions(Mode: WorkspaceMode.Server, EnableFileWatching: true)));
        builder.Services.AddSingleton<WorkspaceSessionHolder>();
        builder.Services.AddHostedService<WorkspaceSessionLifecycle>();
        builder.Services.AddSingleton<WorkspaceQueryService>();
        builder.Services.AddSingleton<CurationSetProvider>();
        builder.Services.AddSingleton<AnalysisService>();
        builder.Services.AddSingleton<CodeActionService>();
        builder.Services.AddSingleton<ToolAnalytics>();
        var rootAccessor = new WorkspaceRootAccessor();
        builder.Services.AddSingleton(rootAccessor);
        builder.Services.AddOptions<McpServerOptions>()
            .Configure<ToolAnalytics>((options, analytics) =>
                options.Filters.Request.CallToolFilters.Add(AnalyticsFilter.Create(analytics)));

        var toolJson = ParlanceToolJson.Create(new RepoPathJsonConverter(rootAccessor));

        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithTools<WorkspaceStatusTool>(toolJson)
            .WithTools<DescribeTypeTool>(toolJson)
            .WithTools<FindImplementationsTool>(toolJson)
            .WithTools<FindReferencesTool>(toolJson)
            .WithTools<GetCodeFixesTool>(toolJson)
            .WithTools<GetRefactoringsTool>(toolJson)
            .WithTools<GotoDefinitionTool>(toolJson)
            .WithTools<GetTypeAtTool>(toolJson)
            .WithTools<OutlineFileTool>(toolJson)
            .WithTools<PreviewCodeActionTool>(toolJson)
            .WithTools<GetSymbolDocsTool>(toolJson)
            .WithTools<CallHierarchyTool>(toolJson)
            .WithTools<GetTypeDependenciesTool>(toolJson)
            .WithTools<SafeToDeleteTool>(toolJson)
            .WithTools<SearchSymbolsTool>(toolJson)
            .WithTools<TypeHierarchyTool>(toolJson)
            .WithTools<DecompileTypeTool>(toolJson)
            .WithTools<AnalyzeTool>(toolJson);

        await builder.Build().RunAsync(cancellationToken);
    }
}
