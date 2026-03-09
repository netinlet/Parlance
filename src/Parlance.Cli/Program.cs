using System.CommandLine;
using Microsoft.Extensions.Logging;
using Parlance.Cli.Commands;
using Parlance.Cli.Logging;

using var loggerFactory = LoggerFactory.Create(builder =>
    builder.AddProvider(new StderrLoggerProvider()));

var rootCommand = new RootCommand("Parlance — C# code quality analysis and auto-fix tool");
rootCommand.Add(AnalyzeCommand.Create(loggerFactory.CreateLogger("analyze")));
rootCommand.Add(FixCommand.Create(loggerFactory.CreateLogger("fix")));
rootCommand.Add(RulesCommand.Create());

var result = await rootCommand.Parse(args).InvokeAsync();
return Environment.ExitCode != 0 ? Environment.ExitCode : result;
