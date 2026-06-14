using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.Logging;
using Parlance.Analysis.Curation;
using Parlance.Analyzers.Upstream;
using Parlance.CSharp;
using Parlance.CSharp.Workspace;
using static Parlance.Abstractions.DiagnosticSeverityFormatting;

namespace Parlance.Analysis;

public sealed class AnalysisService(
    WorkspaceSessionHolder holder,
    WorkspaceQueryService query,
    CurationSetProvider curationProvider,
    AnalyzerProvider analyzerProvider,
    ILogger<AnalysisService> logger)
{
    // Singleton service; MCP dispatches tool calls concurrently, so the once-per-path guard must
    // be a concurrent set (a plain HashSet can corrupt during a concurrent Add/resize).
    private readonly ConcurrentDictionary<string, byte> _loggedFailurePaths = new(StringComparer.OrdinalIgnoreCase);

    public async Task<FileAnalysisResult> AnalyzeFilesAsync(
        ImmutableList<string> filePaths,
        AnalyzeOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new AnalyzeOptions();
        var session = holder.LoadedSession;
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

            var repoPath = session.Root.Absolute;
            var providerResult = analyzerProvider.GetComponents(targetFramework, repoPath);
            LogLoadReportOnce(providerResult.Failures, project.Name);

            var analyzers = providerResult.Components.Analyzers;

            if (analyzers.IsEmpty)
            {
                logger.LogWarning("No analyzers loaded for project {Name}", project.Name);
                continue;
            }

            var compilationWithAnalyzers = compilation.WithProjectAnalyzers(analyzers, project);
            var roslynDiagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync(ct);

            // Filter to requested files and exclude AD0001 (analyzer infrastructure errors)
            var fileDiagnostics = roslynDiagnostics
                .Where(d => d.Id != "AD0001")
                .Where(d =>
                {
                    var path = d.Location.GetLineSpan().Path;
                    return fileSet.Contains(path);
                });

            foreach (var d in fileDiagnostics)
            {
                var lineSpan = d.Location.GetLineSpan();
                var start = lineSpan.StartLinePosition;
                var end = lineSpan.EndLinePosition;

                allCurated.Add(new CuratedDiagnostic(
                    d.Id,
                    d.Descriptor.Category,
                    FromRoslyn(d.Severity),
                    d.GetMessage(),
                    lineSpan.Path ?? "",
                    start.Line + 1,
                    start.Character + 1,
                    end.Line + 1,
                    end.Character + 1,
                    null, null));
            }
        }

        var collected = CollapseIdenticalDiagnostics(allCurated.ToImmutable());

        // Apply --suppress filter before scoring so totals/score are consistent
        if (options.Suppress is { IsEmpty: false } suppress)
            collected = collected.Where(d => !suppress.IsSuppressed(d.RuleId)).ToImmutableList();

        // Apply curation
        var curated = CurationFilter.Apply(curationSet, collected);

        // Convert to Parlance diagnostics for scoring
        var parlanceDiagnostics = curated.Select(d => new Abstractions.Diagnostic(
            d.RuleId, d.Category, d.Severity, d.Message,
            new Abstractions.Location(d.Line, d.Column, d.EndLine, d.EndColumn, d.FilePath),
            d.Rationale)).ToImmutableList();

        // Score
        var summary = IdiomaticScoreCalculator.Calculate(parlanceDiagnostics);

        // Convert to file diagnostics
        var fileDiagnosticResults = curated.Select(d => new FileDiagnostic(
            d.RuleId, d.Category, d.Severity.ToWireString(), d.Message,
            d.FilePath, d.Line, d.EndLine, d.Column, d.EndColumn,
            d.FixClassification, d.Rationale)).ToImmutableList();

        // Cap after scoring
        if (options.MaxDiagnostics is > 0 && fileDiagnosticResults.Count > options.MaxDiagnostics.Value)
            fileDiagnosticResults = fileDiagnosticResults.Take(options.MaxDiagnostics.Value).ToImmutableList();

        var curationSetName = curationSet?.Name ?? "default";
        return new FileAnalysisResult(curationSetName, summary, fileDiagnosticResults);
    }

    private static Abstractions.DiagnosticSeverity FromRoslyn(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Error => Abstractions.DiagnosticSeverity.Error,
        DiagnosticSeverity.Warning => Abstractions.DiagnosticSeverity.Warning,
        DiagnosticSeverity.Info => Abstractions.DiagnosticSeverity.Suggestion,
        _ => Abstractions.DiagnosticSeverity.Silent
    };

    /// <summary>
    /// Collapses byte-identical diagnostics — same rule id, severity, message, file path, and source
    /// span. <see cref="AnalyzerProvider"/> already dedups analyzers by type FullName, so the same
    /// analyzer type never runs twice; this is the residual guard against a single analyzer (or Roslyn
    /// across multiple syntax trees) emitting the exact same diagnostic more than once.
    /// </summary>
    private static ImmutableList<CuratedDiagnostic> CollapseIdenticalDiagnostics(
        ImmutableList<CuratedDiagnostic> diagnostics) =>
        diagnostics
            .GroupBy(d => (d.RuleId, d.Severity, d.Message, d.FilePath, d.Line, d.Column, d.EndLine, d.EndColumn))
            .Select(g => g.First())
            .ToImmutableList();

    /// <summary>
    /// Logs each <see cref="DllLoadFailure"/> at Warning level, at most once per DLL path per session.
    /// </summary>
    private void LogLoadReportOnce(ImmutableList<DllLoadFailure> failures, string projectName)
    {
        foreach (var failure in failures)
        {
            if (_loggedFailurePaths.TryAdd(failure.DllPath, 0))
                logger.LogWarning(
                    "Analyzer DLL load failure for project {ProjectName}: {DllPath} — {Reason}",
                    projectName, failure.DllPath, failure.Reason);
        }
    }
}
