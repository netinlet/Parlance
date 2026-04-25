using System.CommandLine;
using Parlance.Mcp;

namespace Parlance.Cli.Commands;

internal static class McpCommand
{
    public static Command Create()
    {
        var command = new Command("mcp", "Run the Parlance MCP server over stdio.");
        command.Add(new Argument<string[]>("args") { Arity = ArgumentArity.ZeroOrMore });

        command.SetAction(async (parseResult, ct) =>
        {
            var args = parseResult.GetValue(parseResult.CommandResult.Command.Arguments.OfType<Argument<string[]>>().First())
                       ?? Array.Empty<string>();

            try
            {
                await ParlanceMcpHost.RunAsync(args, ct);
            }
            catch (InvalidOperationException ex)
            {
                await Console.Error.WriteLineAsync(ex.Message);
                Environment.ExitCode = 2;
            }
        });

        return command;
    }
}
