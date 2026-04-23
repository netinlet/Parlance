using System.CommandLine;
using System.CommandLine.Parsing;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;

namespace Parlance.Cli.Commands;

internal static class AgentCommand
{
    public static Command Create()
    {
        var command = new Command("agent", "Agent integration - install hooks per adapter, run reports, bench.");

        var adapterOption = new Option<string?>("--for") { Description = "Adapter name (claude, codex, ...)." };

        var install = new Command("install", "Install the adapter into its host.");
        install.Add(adapterOption);
        install.Add(new Argument<string[]>("args") { Arity = ArgumentArity.ZeroOrMore });
        install.SetAction(async (parseResult, _) =>
        {
            var adapter = parseResult.GetValue(adapterOption);
            if (string.IsNullOrWhiteSpace(adapter))
            {
                await Console.Error.WriteLineAsync("--for <adapter> required");
                Environment.ExitCode = 2;
                return;
            }

            Environment.ExitCode = await RunAdapterAsync(adapter, "install", GetPassthrough(parseResult));
        });

        var uninstall = new Command("uninstall", "Uninstall the adapter.");
        uninstall.Add(adapterOption);
        uninstall.Add(new Argument<string[]>("args") { Arity = ArgumentArity.ZeroOrMore });
        uninstall.SetAction(async (parseResult, _) =>
        {
            var adapter = parseResult.GetValue(adapterOption);
            if (string.IsNullOrWhiteSpace(adapter))
            {
                await Console.Error.WriteLineAsync("--for <adapter> required");
                Environment.ExitCode = 2;
                return;
            }

            Environment.ExitCode = await RunAdapterAsync(adapter, "uninstall", GetPassthrough(parseResult));
        });

        foreach (var neutral in new[]
                 {
                     ("status", "Show agent integration status and recent session summary."),
                     ("report", "Show session-level agent usage analysis."),
                     ("bench", "Show benchmark comparisons across variants.")
                 })
        {
            var subcommand = new Command(neutral.Item1, neutral.Item2);
            subcommand.Add(new Argument<string[]>("args") { Arity = ArgumentArity.ZeroOrMore });
            subcommand.SetAction(async (parseResult, _) =>
            {
                Environment.ExitCode = await RunCoreAsync(neutral.Item1, GetPassthrough(parseResult));
            });
            command.Add(subcommand);
        }

        command.Add(install);
        command.Add(uninstall);
        return command;
    }

    private static string[] GetPassthrough(ParseResult parseResult)
    {
        var argument = parseResult.CommandResult.Command.Arguments.OfType<Argument<string[]>>().FirstOrDefault();
        return argument is null ? Array.Empty<string>() : parseResult.GetValue(argument) ?? Array.Empty<string>();
    }

    private static Task<int> RunCoreAsync(string subcommand, string[] passthrough) =>
        RunNodeAsync(Path.Combine(Bundles.Root, "Parlance.Agent.Core", "cli.js"), [subcommand, .. passthrough]);

    private static Task<int> RunAdapterAsync(string adapter, string subcommand, string[] passthrough)
    {
        var folder = adapter.ToLowerInvariant() switch
        {
            "claude" => "Parlance.Agent.Adapter.Claude",
            _ => string.Empty
        };

        if (string.IsNullOrEmpty(folder))
        {
            Console.Error.WriteLine($"unknown adapter: {adapter}");
            return Task.FromResult(2);
        }

        return RunNodeAsync(Path.Combine(Bundles.Root, folder, "cli.js"), [subcommand, .. passthrough]);
    }

    private static async Task<int> RunNodeAsync(string jsPath, string[] args)
    {
        if (!File.Exists(jsPath))
        {
            await Console.Error.WriteLineAsync($"bundle missing: {jsPath}");
            return 1;
        }

        Process process;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "node",
                UseShellExecute = false
            };
            psi.ArgumentList.Add(jsPath);
            foreach (var arg in args) psi.ArgumentList.Add(arg);

            process = Process.Start(psi) ?? throw new InvalidOperationException("failed to start node");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            await Console.Error.WriteLineAsync("node is required on PATH to run parlance agent");
            return 1;
        }

        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    private static class Bundles
    {
        public static string Root
        {
            get
            {
                var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
                var packed = Path.Combine(assemblyDir, "bundles");
                if (Directory.Exists(packed)) return packed;

                var directory = new DirectoryInfo(assemblyDir);
                while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Parlance.sln")))
                {
                    directory = directory.Parent;
                }

                if (directory is null) throw new InvalidOperationException("can't locate Parlance.sln");
                return Path.Combine(directory.FullName, "src", "Parlance.Agent");
            }
        }
    }
}
