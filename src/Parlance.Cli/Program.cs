using System.CommandLine;
using Parlance.Cli.Commands;

var rootCommand = new RootCommand("Parlance — C# code quality analysis and auto-fix tool");
rootCommand.Add(AnalyzeCommand.Create());
rootCommand.Add(FixCommand.Create());
return await rootCommand.Parse(args).InvokeAsync();
