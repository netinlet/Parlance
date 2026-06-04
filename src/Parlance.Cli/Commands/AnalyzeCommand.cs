using System.Collections.Immutable;
using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Parlance.Analysis;
using Parlance.Cli.Formatting;
using Parlance.CSharp.Workspace;

namespace Parlance.Cli.Commands;

internal static class AnalyzeCommand
{
    public static Command Create(IServiceProvider services)
    {
        var pathArg = new Argument<string>("path") { Description = "Path to .sln, .slnx, or .csproj" };
        var formatOption = new Option<string>("--format", "-f") { Description = "Output format: text, json" };
        formatOption.DefaultValueFactory = _ => "text";
        formatOption.AcceptOnlyFromAmong("text", "json");
        var suppressOption = new Option<string[]>("--suppress") { Description = "Rule IDs to suppress" };
        suppressOption.DefaultValueFactory = _ => Array.Empty<string>();
        suppressOption.AllowMultipleArgumentsPerToken = true;
        var maxDiagOption = new Option<int?>("--max-diagnostics") { Description = "Maximum number of diagnostics to report" };
        var curationSetOption = new Option<string?>("--curation-set") { Description = "Named curation set (default: project defaults)" };

        var command = new Command("analyze", "Analyze C# source files for idiomatic patterns");
        command.Add(pathArg);
        command.Add(formatOption);
        command.Add(suppressOption);
        command.Add(maxDiagOption);
        command.Add(curationSetOption);

        command.SetAction(async (parseResult, ct) =>
        {
            var path = parseResult.GetValue(pathArg)!;
            var format = parseResult.GetValue(formatOption)!;
            var suppress = parseResult.GetValue(suppressOption) ?? [];
            var maxDiag = parseResult.GetValue(maxDiagOption);
            var curationSet = parseResult.GetValue(curationSetOption);

            if (!path.IsLoadableProject)
            {
                await Console.Error.WriteLineAsync("Path must point to a .sln, .slnx, or .csproj file.");
                Environment.ExitCode = 2;
                return;
            }

            if (!File.Exists(path))
            {
                await Console.Error.WriteLineAsync($"File not found: {path}");
                Environment.ExitCode = 2;
                return;
            }

            var holder = services.GetRequiredService<WorkspaceSessionHolder>();
            var analysis = services.GetRequiredService<AnalysisService>();
            var loggerFactory = services.GetRequiredService<ILoggerFactory>();
            var openOptions = new WorkspaceOpenOptions(Mode: WorkspaceMode.Report, LoggerFactory: loggerFactory);

            var outcome = path.IsSolution
                ? await CSharpWorkspaceSession.TryOpenSolutionAsync(path, openOptions, ct)
                : await CSharpWorkspaceSession.TryOpenProjectAsync(path, openOptions, ct);

            CSharpWorkspaceSession? session = null;
            outcome.Switch(
                onSuccess: s => session = s,
                onFailure: reason =>
                {
                    Console.Error.WriteLine($"Failed to load workspace: {reason.Message}");
                    Environment.ExitCode = 2;
                });

            if (session is null)
                return;

            // Holder takes ownership; it disposes the session when the DI container disposes.
            holder.SetSession(session);

            var allFiles = session.CurrentSolution.Projects
                .SelectMany(p => p.Documents)
                .Select(d => d.FilePath)
                .OfType<string>()
                .Where(p => File.Exists(p) &&
                            !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                .ToImmutableList();

            var suppression = suppress.Length > 0 ? RuleSuppression.From(suppress) : null;

            FileAnalysisResult result;
            try
            {
                result = await analysis.AnalyzeFilesAsync(
                    allFiles, new AnalyzeOptions(curationSet, maxDiag, suppression), ct);
            }
            catch (ArgumentException ex)
            {
                await Console.Error.WriteLineAsync(ex.Message);
                Environment.ExitCode = 2;
                return;
            }

            IOutputFormatter formatter = format.ToLowerInvariant() switch
            {
                "text" => new TextFormatter(),
                "json" => new JsonFormatter(),
                _ => throw new InvalidOperationException($"Unknown format: {format}"),
            };

            Console.Write(formatter.Format(result));
        });

        return command;
    }
}
