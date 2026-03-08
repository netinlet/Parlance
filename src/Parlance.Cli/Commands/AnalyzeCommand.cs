using System.CommandLine;
using Parlance.Cli.Analysis;
using Parlance.Cli.Formatting;

namespace Parlance.Cli.Commands;

internal static class AnalyzeCommand
{
    public static Command Create()
    {
        var pathsArg = new Argument<string[]>("paths") { Arity = ArgumentArity.OneOrMore };
        pathsArg.Description = "Files, directories, or glob patterns to analyze";

        var formatOption = new Option<string>("--format", "-f") { Description = "Output format: text, json" };
        formatOption.DefaultValueFactory = _ => "text";

        var failBelowOption = new Option<int?>("--fail-below") { Description = "Exit with code 1 if score is below threshold (0-100)" };
        var suppressOption = new Option<string[]>("--suppress") { Description = "Rule IDs to suppress" };
        suppressOption.DefaultValueFactory = _ => Array.Empty<string>();

        var maxDiagOption = new Option<int?>("--max-diagnostics") { Description = "Maximum number of diagnostics to report" };
        var langVersionOption = new Option<string?>("--language-version") { Description = "C# language version (default: Latest)" };

        var command = new Command("analyze", "Analyze C# source files for idiomatic patterns");
        command.Add(pathsArg);
        command.Add(formatOption);
        command.Add(failBelowOption);
        command.Add(suppressOption);
        command.Add(maxDiagOption);
        command.Add(langVersionOption);

        command.SetAction(async (parseResult, ct) =>
        {
            var paths = parseResult.GetValue(pathsArg)!;
            var format = parseResult.GetValue(formatOption)!;
            var failBelow = parseResult.GetValue(failBelowOption);
            var suppress = parseResult.GetValue(suppressOption)!;
            var maxDiag = parseResult.GetValue(maxDiagOption);
            var langVersion = parseResult.GetValue(langVersionOption);

            var files = PathResolver.Resolve(paths);
            if (files.Count == 0)
            {
                Console.Error.WriteLine("No .cs files found matching the specified paths.");
                Environment.ExitCode = 2;
                return;
            }

            var result = await WorkspaceAnalyzer.AnalyzeAsync(
                files, suppress, maxDiag, langVersion, ct: ct);

            IOutputFormatter formatter = format.ToLowerInvariant() switch
            {
                "json" => new JsonFormatter(),
                _ => new TextFormatter(),
            };

            Console.Write(formatter.Format(result));

            if (failBelow.HasValue && result.Summary.IdiomaticScore < failBelow.Value)
            {
                Environment.ExitCode = 1;
            }
        });

        return command;
    }
}
