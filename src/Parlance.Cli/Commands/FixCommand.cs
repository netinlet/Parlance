using System.CommandLine;

namespace Parlance.Cli.Commands;

internal static class FixCommand
{
    public static Command Create()
    {
        var command = new Command("fix", "Apply auto-fixes to C# source files");
        command.SetAction((_, _) => Task.CompletedTask);
        return command;
    }
}
