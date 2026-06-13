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

        var lifecycleOptions = new WorkspaceLifecycleOptions(
            configuration.SolutionPath,
            new WorkspaceOpenOptions(Mode: WorkspaceMode.Server, EnableFileWatching: true));
        var holder = new WorkspaceSessionHolder();

        builder.Services.AddSingleton(configuration);
        builder.Services.AddSingleton(lifecycleOptions);
        builder.Services.AddSingleton(holder);
        builder.Services.AddHostedService<WorkspaceSessionLifecycle>();
        builder.Services.AddSingleton<WorkspaceQueryService>();
        builder.Services.AddSingleton<CurationSetProvider>();
        builder.Services.AddSingleton<AnalysisService>();
        builder.Services.AddSingleton<CodeActionService>();
        builder.Services.AddSingleton<ToolAnalytics>();
        builder.Services.AddOptions<McpServerOptions>()
            .Configure<ToolAnalytics>((options, analytics) =>
                options.Filters.Request.CallToolFilters.Add(AnalyticsFilter.Create(analytics)));

        // The converter resolves the root from the holder (single source of truth) at write time,
        // falling back to the configured solution's directory before the session loads.
        var toolJson = ParlanceToolJson.Create(new RepoPathJsonConverter(holder, lifecycleOptions));

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
            .WithTools<AnalyzeTool>(toolJson)
            .WithTools<SyncBufferTool>(toolJson)
            .WithTools<ApplyCodeActionTool>(toolJson);

        await builder.Build().RunAsync(cancellationToken);
    }
}
