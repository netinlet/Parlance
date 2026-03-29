using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Parlance.Analysis;
using Parlance.Analysis.Curation;
using Parlance.Cli.Commands;
using Parlance.CSharp.Workspace;

var services = new ServiceCollection()
    .AddLogging(b => b.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace).SetMinimumLevel(LogLevel.Warning))
    .AddSingleton<CurationSetProvider>()
    .AddSingleton<WorkspaceSessionHolder>()
    .AddSingleton<WorkspaceQueryService>()
    .AddSingleton<AnalysisService>();

await using var provider = services.BuildServiceProvider();

var rootCommand = new RootCommand("Parlance — C# code quality analysis");
rootCommand.Add(AnalyzeCommand.Create(provider));
rootCommand.Add(FixCommand.Create(provider));
rootCommand.Add(RulesCommand.Create());

var result = await rootCommand.Parse(args).InvokeAsync();
return Environment.ExitCode != 0 ? Environment.ExitCode : result;
