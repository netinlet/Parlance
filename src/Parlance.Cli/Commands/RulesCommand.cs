using System.CommandLine;

namespace Parlance.Cli.Commands;

internal static class RulesCommand
{
    public static Command Create()
    {
        var command = new Command("rules", "List available analysis rules");
        command.SetAction((_, _) => Task.CompletedTask);
        return command;
    }
}
