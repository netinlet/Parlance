using System.CommandLine;
using Parlance.Cli.Commands;

var rootCommand = new RootCommand("Parlance — C# code quality analysis and auto-fix tool");
rootCommand.Add(AnalyzeCommand.Create());
rootCommand.Add(FixCommand.Create());
rootCommand.Add(RulesCommand.Create());

var result = await rootCommand.Parse(args).InvokeAsync();
return Environment.ExitCode != 0 ? Environment.ExitCode : result;
