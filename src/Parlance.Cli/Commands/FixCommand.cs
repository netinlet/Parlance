using System.Collections.Immutable;
using System.CommandLine;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Parlance.Analyzers.Upstream;
using Parlance.CSharp.Workspace;

namespace Parlance.Cli.Commands;

internal static class FixCommand
{
    public static Command Create(IServiceProvider services)
    {
        var pathArg = new Argument<string>("path") { Description = "Path to .sln or .csproj" };
        var dryRunOption = new Option<bool>("--dry-run") { Description = "Preview changes without writing" };
        var suppressOption = new Option<string[]>("--suppress") { Description = "Rule IDs to suppress" };
        suppressOption.DefaultValueFactory = _ => Array.Empty<string>();

        var command = new Command("fix", "Apply auto-fixes to C# source files");
        command.Add(pathArg);
        command.Add(dryRunOption);
        command.Add(suppressOption);

        command.SetAction(async (parseResult, ct) =>
        {
            var path = parseResult.GetValue(pathArg)!;
            var dryRun = parseResult.GetValue(dryRunOption);
            var suppress = parseResult.GetValue(suppressOption) ?? [];

            if (!File.Exists(path))
            {
                await Console.Error.WriteLineAsync($"File not found: {path}");
                Environment.ExitCode = 2;
                return;
            }

            if (!path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                await Console.Error.WriteLineAsync("Path must be a .sln or .csproj file.");
                Environment.ExitCode = 2;
                return;
            }

            var loggerFactory = services.GetRequiredService<ILoggerFactory>();
            var openOptions = new WorkspaceOpenOptions(Mode: WorkspaceMode.Report, LoggerFactory: loggerFactory);

            CSharpWorkspaceSession session;
            try
            {
                session = path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
                    ? await CSharpWorkspaceSession.OpenSolutionAsync(path, openOptions, ct)
                    : await CSharpWorkspaceSession.OpenProjectAsync(path, openOptions, ct);
            }
            catch (WorkspaceLoadException ex)
            {
                await Console.Error.WriteLineAsync($"Failed to load workspace: {ex.Message}");
                Environment.ExitCode = 2;
                return;
            }

            await using (session)
            {
                var fixProviders = FixProviderLoader.LoadAll("net10.0");
                var fixableIds = fixProviders.SelectMany(fp => fp.FixableDiagnosticIds).ToHashSet();

                var analyzers = AnalyzerLoader.LoadAll("net10.0")
                    .Where(a => a.SupportedDiagnostics.Any(d => fixableIds.Contains(d.Id)))
                    .ToImmutableArray();

                if (analyzers.IsEmpty)
                {
                    Console.WriteLine("No auto-fixes available.");
                    return;
                }

                var originalSolution = session.CurrentSolution;
                var currentSolution = originalSolution;

                const int maxIterations = 50;
                for (var iteration = 0; iteration < maxIterations; iteration++)
                {
                    var applied = false;

                    foreach (var project in currentSolution.Projects)
                    {
                        var compilation = await project.GetCompilationAsync(ct);
                        if (compilation is null) continue;

                        var compilationWithAnalyzers = compilation.WithAnalyzers(analyzers);
                        var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync(ct);

                        var fixableDiags = diagnostics
                            .Where(d => d.Id != "AD0001")
                            .Where(d => !suppress.Contains(d.Id))
                            .ToList();

                        foreach (var diagnostic in fixableDiags)
                        {
                            var fixProvider = fixProviders.FirstOrDefault(fp =>
                                fp.FixableDiagnosticIds.Contains(diagnostic.Id));
                            if (fixProvider is null) continue;

                            var tree = diagnostic.Location.SourceTree;
                            if (tree is null) continue;

                            var docId = currentSolution.GetDocumentIdsWithFilePath(tree.FilePath).FirstOrDefault();
                            if (docId is null) continue;

                            var document = currentSolution.GetDocument(docId);
                            if (document is null) continue;

                            var actions = new List<CodeAction>();
                            var context = new CodeFixContext(document, diagnostic,
                                (action, _) => actions.Add(action), ct);
                            await fixProvider.RegisterCodeFixesAsync(context);

                            if (actions.Count == 0) continue;

                            var operations = await actions[0].GetOperationsAsync(ct);
                            foreach (var op in operations)
                            {
                                if (op is ApplyChangesOperation applyOp)
                                {
                                    currentSolution = applyOp.ChangedSolution;
                                    applied = true;
                                }
                            }

                            if (applied) break;
                        }

                        if (applied) break;
                    }

                    if (!applied) break;
                }

                // Collect changed files
                var fixedFiles = new List<(string FilePath, string NewContent)>();
                foreach (var project in currentSolution.Projects)
                {
                    foreach (var document in project.Documents)
                    {
                        if (document.FilePath is null) continue;
                        var origDocIds = originalSolution.GetDocumentIdsWithFilePath(document.FilePath);
                        if (origDocIds.IsEmpty) continue;
                        var origDoc = originalSolution.GetDocument(origDocIds[0]);
                        if (origDoc is null) continue;

                        var origText = (await origDoc.GetTextAsync(ct)).ToString();
                        var newText = (await document.GetTextAsync(ct)).ToString();
                        if (origText != newText)
                            fixedFiles.Add((document.FilePath, newText));
                    }
                }

                if (fixedFiles.Count == 0)
                {
                    Console.WriteLine("No auto-fixes available.");
                    return;
                }

                foreach (var (filePath, _) in fixedFiles)
                    Console.WriteLine($"--- {filePath}");

                if (dryRun)
                {
                    Console.WriteLine($"{fixedFiles.Count} file(s) would be modified. Remove --dry-run to apply.");
                }
                else
                {
                    foreach (var (filePath, newContent) in fixedFiles)
                        await File.WriteAllTextAsync(filePath, newContent, ct);
                    Console.WriteLine($"Applied fixes to {fixedFiles.Count} file(s).");
                }
            }
        });

        return command;
    }
}
