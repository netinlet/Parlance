using System.CommandLine;

var rootCommand = new RootCommand("Parlance — C# code quality analysis and auto-fix tool");
return await rootCommand.Parse(args).InvokeAsync();
