using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using ModelContextProtocol;
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
builder.Services.AddSingleton<WorkspaceSessionHolder>();
builder.Services.AddHostedService<WorkspaceSessionLifecycle>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<WorkspaceStatusTool>();

await builder.Build().RunAsync();
