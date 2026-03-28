using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.Logging;
using Parlance.Analysis.Curation;
using Parlance.Analyzers.Upstream;
using Parlance.CSharp;
using Parlance.CSharp.Workspace;

namespace Parlance.Analysis;

public sealed class AnalysisService(
    WorkspaceSessionHolder holder,
    WorkspaceQueryService query,
    CurationSetProvider curationProvider,
    ILogger<AnalysisService> logger)
{
    public async Task<FileAnalysisResult> AnalyzeFilesAsync(
        ImmutableList<string> filePaths,
        AnalyzeOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new AnalyzeOptions();
        var session = holder.Session;
        var solution = session.CurrentSolution;

        // Validate curation set up front
        CurationSet? curationSet = null;
        if (options.CurationSetName is not null)
        {
            curationSet = curationProvider.Load(options.CurationSetName);
            if (curationSet is null)
            {
                var available = curationProvider.Available();
                throw new ArgumentException(
                    $"Curation set '{options.CurationSetName}' not found. Available: {(available.IsEmpty ? "(none)" : string.Join(", ", available))}");
            }
        }

        // Resolve files to projects
        var filesByProject = new Dictionary<ProjectId, List<string>>();
        var unmatchedFiles = new List<string>();

        foreach (var filePath in filePaths)
        {
            var docIds = solution.GetDocumentIdsWithFilePath(filePath);
            if (docIds.IsEmpty)
            {
                unmatchedFiles.Add(filePath);
                logger.LogWarning("File not found in workspace: {FilePath}", filePath);
                continue;
            }

            var docId = docIds.First();
            if (!filesByProject.TryGetValue(docId.ProjectId, out var list))
            {
                list = [];
                filesByProject[docId.ProjectId] = list;
            }
            list.Add(filePath);
        }

        // Run analyzers per project
        var allCurated = ImmutableList.CreateBuilder<CuratedDiagnostic>();

        foreach (var (projectId, files) in filesByProject)
        {
            var project = solution.GetProject(projectId);
            if (project is null) continue;

            var compilation = await query.GetCompilationAsync(project, ct);
            var fileSet = files.ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Determine target framework from project info
            var projectInfo = session.Projects.FirstOrDefault(p => p.Name == project.Name);
            var targetFramework = projectInfo?.ActiveTargetFramework ?? "net10.0";

            ImmutableArray<DiagnosticAnalyzer> analyzers;
            try
            {
                analyzers = AnalyzerLoader.LoadAll(targetFramework);
            }
            catch (ArgumentException)
            {
                // Unsupported framework, fall back to net10.0
                logger.LogWarning("Unsupported framework {Tfm} for project {Name}, falling back to net10.0",
                    targetFramework, project.Name);
                analyzers = AnalyzerLoader.LoadAll("net10.0");
            }

            if (analyzers.IsEmpty)
            {
                logger.LogWarning("No analyzers loaded for project {Name}", project.Name);
                continue;
            }

            var compilationWithAnalyzers = compilation.WithAnalyzers(analyzers);
            var roslynDiagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync(ct);

            // Filter to requested files and exclude AD0001 (analyzer infrastructure errors)
            var fileDiagnostics = roslynDiagnostics
                .Where(d => d.Id != "AD0001")
                .Where(d =>
                {
                    var path = d.Location.GetLineSpan().Path;
                    return path is not null && fileSet.Contains(path);
                });

            foreach (var d in fileDiagnostics)
            {
                var lineSpan = d.Location.GetLineSpan();
                var start = lineSpan.StartLinePosition;
                var end = lineSpan.EndLinePosition;

                allCurated.Add(new CuratedDiagnostic(
                    d.Id,
                    d.Descriptor.Category,
                    d.Severity switch
                    {
                        Microsoft.CodeAnalysis.DiagnosticSeverity.Error => "error",
                        Microsoft.CodeAnalysis.DiagnosticSeverity.Warning => "warning",
                        Microsoft.CodeAnalysis.DiagnosticSeverity.Info => "suggestion",
                        _ => "silent"
                    },
                    d.GetMessage(),
                    lineSpan.Path ?? "",
                    start.Line + 1,
                    start.Character + 1,
                    end.Line + 1,
                    end.Character + 1,
                    null, null));
            }
        }

        // Apply curation
        var curated = CurationFilter.Apply(curationSet, allCurated.ToImmutable());

        // Convert to Parlance diagnostics for scoring
        var parlanceDiagnostics = curated.Select(d => new Abstractions.Diagnostic(
            d.RuleId, d.Category,
            d.Severity switch
            {
                "error" => Abstractions.DiagnosticSeverity.Error,
                "warning" => Abstractions.DiagnosticSeverity.Warning,
                "suggestion" => Abstractions.DiagnosticSeverity.Suggestion,
                _ => Abstractions.DiagnosticSeverity.Silent
            },
            d.Message,
            new Abstractions.Location(d.Line, d.Column, d.EndLine, d.EndColumn, d.FilePath),
            d.Rationale)).ToImmutableList();

        // Score
        var summary = IdiomaticScoreCalculator.Calculate(parlanceDiagnostics);

        // Convert to file diagnostics
        var fileDiagnosticResults = curated.Select(d => new FileDiagnostic(
            d.RuleId, d.Category, d.Severity, d.Message,
            d.FilePath, d.Line, d.EndLine, d.Column, d.EndColumn,
            d.FixClassification, d.Rationale)).ToImmutableList();

        // Cap after scoring
        if (options.MaxDiagnostics is > 0 && fileDiagnosticResults.Count > options.MaxDiagnostics.Value)
            fileDiagnosticResults = fileDiagnosticResults.Take(options.MaxDiagnostics.Value).ToImmutableList();

        var curationSetName = curationSet?.Name ?? "default";
        return new FileAnalysisResult(curationSetName, summary, fileDiagnosticResults);
    }
}
