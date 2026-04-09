using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using ModelContextProtocol;
using Parlance.Analysis;
using Parlance.Analysis.Curation;
using Parlance.CSharp.Workspace;
using Parlance.Mcp;
using Parlance.Mcp.Tools;

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

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<WorkspaceStatusTool>()
    .WithTools<DescribeTypeTool>()
    .WithTools<FindImplementationsTool>()
    .WithTools<FindReferencesTool>()
    .WithTools<GetCodeFixesTool>()
    .WithTools<GetRefactoringsTool>()
    .WithTools<GotoDefinitionTool>()
    .WithTools<GetTypeAtTool>()
    .WithTools<OutlineFileTool>()
    .WithTools<PreviewCodeActionTool>()
    .WithTools<GetSymbolDocsTool>()
    .WithTools<CallHierarchyTool>()
    .WithTools<GetTypeDependenciesTool>()
    .WithTools<SafeToDeleteTool>()
    .WithTools<SearchSymbolsTool>()
    .WithTools<TypeHierarchyTool>()
    .WithTools<DecompileTypeTool>()
    .WithTools<AnalyzeTool>();

await builder.Build().RunAsync();
