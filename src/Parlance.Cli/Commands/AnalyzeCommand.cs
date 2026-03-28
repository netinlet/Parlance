using System.CommandLine;

namespace Parlance.Cli.Commands;

internal static class AnalyzeCommand
{
    public static Command Create()
    {
        var command = new Command("analyze", "Analyze C# source files for idiomatic patterns");
        command.SetAction((_, _) => Task.CompletedTask);
        return command;
    }
}
