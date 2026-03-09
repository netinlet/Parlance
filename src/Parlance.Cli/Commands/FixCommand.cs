using System.CommandLine;
using Parlance.Cli.Analysis;

namespace Parlance.Cli.Commands;

internal static class FixCommand
{
    public static Command Create()
    {
        var pathsArg = new Argument<string[]>("paths") { Arity = ArgumentArity.OneOrMore };
        pathsArg.Description = "Files, directories, or glob patterns to fix";

        var applyOption = new Option<bool>("--apply") { Description = "Apply fixes to files (default is dry-run)" };
        var suppressOption = new Option<string[]>("--suppress") { Description = "Rule IDs to suppress" };
        suppressOption.DefaultValueFactory = _ => Array.Empty<string>();
        var langVersionOption = new Option<string?>("--language-version") { Description = "C# language version (default: Latest)" };
        var tfmOption = new Option<string>("--target-framework") { Description = "Target framework (default: net10.0)" };
        tfmOption.DefaultValueFactory = _ => "net10.0";
        var command = new Command("fix", "Apply auto-fixes to C# source files");
        command.Add(pathsArg);
        command.Add(applyOption);
        command.Add(suppressOption);
        command.Add(langVersionOption);
        command.Add(tfmOption);

        command.SetAction(async (parseResult, ct) =>
        {
            var paths = parseResult.GetValue(pathsArg)!;
            var apply = parseResult.GetValue(applyOption);
            var suppress = parseResult.GetValue(suppressOption)!;
            var langVersion = parseResult.GetValue(langVersionOption);
            var targetFramework = parseResult.GetValue(tfmOption)!;

            var files = PathResolver.Resolve(paths);
            if (files.Count == 0)
            {
                Console.Error.WriteLine("No .cs files found matching the specified paths.");
                Environment.ExitCode = 2;
                return;
            }

            var result = await WorkspaceFixer.FixAsync(files, suppress, langVersion, targetFramework, ct);

            if (result.FixedFiles.Count == 0)
            {
                Console.WriteLine("No auto-fixes available.");
                return;
            }

            foreach (var file in result.FixedFiles)
            {
                Console.WriteLine($"--- {file.FilePath}");
                if (!apply)
                {
                    Console.WriteLine($"+++ {file.FilePath} (fixed)");
                    Console.WriteLine(file.NewContent);
                }
            }

            if (apply)
            {
                WorkspaceFixer.ApplyFixes(result);
                Console.WriteLine($"Applied fixes to {result.FixedFiles.Count} file(s).");
            }
            else
            {
                Console.WriteLine($"{result.FixedFiles.Count} file(s) would be modified. Use --apply to write changes.");
            }
        });

        return command;
    }
}
