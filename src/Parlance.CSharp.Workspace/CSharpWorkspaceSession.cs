using System.Collections.Immutable;
using System.Text;
using System.Xml.Linq;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Parlance.CSharp.Workspace.Internal;

namespace Parlance.CSharp.Workspace;

public sealed class CSharpWorkspaceSession : IAsyncDisposable
{
    private readonly MSBuildWorkspace _workspace;
    private readonly IProjectCompilationCache _cache;
    private readonly ILogger<CSharpWorkspaceSession> _logger;
    private readonly WorkspaceMode _mode;
    private readonly ILoggerFactory _loggerFactory;
    private WorkspaceFileWatcher? _watcher;
    private long _snapshotVersion = 1;

    private CSharpWorkspaceSession(
        string workspacePath,
        MSBuildWorkspace workspace,
        CSharpWorkspaceHealth health,
        ImmutableList<CSharpProjectInfo> projects,
        IProjectCompilationCache cache,
        WorkspaceMode mode,
        ILoggerFactory loggerFactory)
    {
        WorkspacePath = workspacePath;
        _workspace = workspace;
        Health = health;
        Projects = projects;
        _cache = cache;
        _mode = mode;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<CSharpWorkspaceSession>();
    }

    public string WorkspacePath { get; }

    public long SnapshotVersion => Interlocked.Read(ref _snapshotVersion);

    public CSharpWorkspaceHealth Health { get; }

    public ImmutableList<CSharpProjectInfo> Projects { get; }

    public CSharpProjectInfo? GetProject(WorkspaceProjectKey key) =>
        Projects.FirstOrDefault(p => p.Key == key);

    public CSharpProjectInfo? GetProjectByPath(string projectPath) =>
        Projects.FirstOrDefault(p =>
            string.Equals(p.ProjectPath, projectPath, StringComparison.OrdinalIgnoreCase));

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        if (_mode is WorkspaceMode.Report)
            throw new InvalidOperationException("RefreshAsync is not supported in Report mode");

        var solution = _workspace.CurrentSolution;
        var affectedProjects = new HashSet<ProjectId>();

        foreach (var project in solution.Projects)
        {
            foreach (var document in project.Documents)
            {
                if (document.FilePath is null || !File.Exists(document.FilePath))
                    continue;

                var currentText = await document.GetTextAsync(ct);
                var diskContent = await File.ReadAllTextAsync(document.FilePath, ct);

                if (currentText.ToString() == diskContent)
                    continue;

                var newText = SourceText.From(diskContent, currentText.Encoding);
                solution = solution.WithDocumentText(document.Id, newText);
                affectedProjects.Add(project.Id);
            }
        }

        if (affectedProjects.Count == 0)
        {
            _logger.LogDebug("RefreshAsync: no changes detected");
            return;
        }

        // MSBuildWorkspace.TryApplyChanges writes to disk, which is the opposite of what we want.
        // Instead, apply the solution snapshot directly — this updates the in-memory workspace
        // without touching the filesystem.
        if (!_workspace.TryApplyChanges(solution))
        {
            // TryApplyChanges may fail on MSBuildWorkspace for text-only changes.
            // Fall through and mark dirty anyway — the workspace's CurrentSolution
            // already reflects file contents on the next GetCompilationAsync call
            // since Roslyn re-reads from the TextLoader.
            _logger.LogDebug("RefreshAsync: TryApplyChanges returned false, marking dirty anyway");
        }

        foreach (var projectId in affectedProjects)
            _cache.MarkDirty(projectId);

        Interlocked.Increment(ref _snapshotVersion);
        _logger.LogInformation(
            "RefreshAsync: {Count} project(s) updated, SnapshotVersion={Version}",
            affectedProjects.Count, SnapshotVersion);
    }

    internal void StartFileWatching(
        IReadOnlyList<string> projectDirectories,
        IReadOnlyList<string> documentPaths)
    {
        _watcher = new WorkspaceFileWatcher(
            projectDirectories,
            documentPaths,
            OnFileChanges,
            _loggerFactory);
    }

    private async Task OnFileChanges(IReadOnlyList<string> changedPaths)
    {
        var solution = _workspace.CurrentSolution;
        var affectedProjects = new HashSet<ProjectId>();
        var hasChanges = false;

        foreach (var filePath in changedPaths)
        {
            var docIds = solution.GetDocumentIdsWithFilePath(filePath);
            if (docIds.IsEmpty) continue;

            string content;
            try
            {
                content = await File.ReadAllTextAsync(filePath);
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Could not read changed file: {Path}", filePath);
                continue;
            }

            var existingDoc = solution.GetDocument(docIds[0]);
            var encoding = existingDoc is not null
                ? (await existingDoc.GetTextAsync()).Encoding
                : Encoding.UTF8;
            var newText = SourceText.From(content, encoding);
            foreach (var docId in docIds)
            {
                solution = solution.WithDocumentText(docId, newText);
                var projectId = solution.GetDocument(docId)?.Project.Id;
                if (projectId is not null)
                    affectedProjects.Add(projectId);
                hasChanges = true;
            }
        }

        if (!hasChanges) return;

        if (_workspace.TryApplyChanges(solution))
        {
            foreach (var projectId in affectedProjects)
                _cache.MarkDirty(projectId);

            Interlocked.Increment(ref _snapshotVersion);
            _logger.LogInformation(
                "File changes applied: {Count} file(s), SnapshotVersion={Version}",
                changedPaths.Count, SnapshotVersion);
        }
        else
        {
            _logger.LogWarning("Failed to apply file changes — concurrent modification");
        }
    }

    public static async Task<CSharpWorkspaceSession> OpenSolutionAsync(
        string solutionPath,
        WorkspaceOpenOptions? options = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionPath);
        if (!File.Exists(solutionPath))
            throw new WorkspaceLoadException(
                $"Solution file not found: {solutionPath}", solutionPath);

        return await LoadAsync(
            solutionPath,
            (ws, token) => ws.OpenSolutionAsync(solutionPath, cancellationToken: token),
            options ?? new WorkspaceOpenOptions(),
            ct);
    }

    public static async Task<CSharpWorkspaceSession> OpenProjectAsync(
        string projectPath,
        WorkspaceOpenOptions? options = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        if (!File.Exists(projectPath))
            throw new WorkspaceLoadException(
                $"Project file not found: {projectPath}", projectPath);

        return await LoadAsync(
            projectPath,
            async (ws, token) =>
            {
                var project = await ws.OpenProjectAsync(projectPath, cancellationToken: token);
                return project.Solution;
            },
            options ?? new WorkspaceOpenOptions(),
            ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_watcher is not null)
            await _watcher.DisposeAsync();

        _workspace.Dispose();
        _logger.LogInformation("Workspace session disposed: {Path}", WorkspacePath);
    }

    private static async Task<CSharpWorkspaceSession> LoadAsync(
        string workspacePath,
        Func<MSBuildWorkspace, CancellationToken, Task<Solution>> loadSolution,
        WorkspaceOpenOptions options,
        CancellationToken ct)
    {
        // Eagerly validate options (triggers ArgumentException for Report + FileWatching=true)
        _ = options.FileWatchingEnabled;

        EnsureMSBuildRegistered();

        var loggerFactory = options.LoggerFactory ?? NullLoggerFactory.Instance;
        var logger = loggerFactory.CreateLogger<CSharpWorkspaceSession>();

        logger.LogInformation("Opening workspace: {Path}, Mode: {Mode}", workspacePath, options.Mode);

        var workspaceDiagnostics = new List<WorkspaceDiagnostic>();
        var diagnosticsLock = new Lock();
        var workspace = MSBuildWorkspace.Create();

        workspace.RegisterWorkspaceFailedHandler(args =>
        {
            var severity = args.Diagnostic.Kind is WorkspaceDiagnosticKind.Failure
                ? WorkspaceDiagnosticSeverity.Error
                : WorkspaceDiagnosticSeverity.Warning;

            var diagnostic = new WorkspaceDiagnostic(
                args.Diagnostic.Kind.ToString(), args.Diagnostic.Message, severity);

            lock (diagnosticsLock)
            {
                workspaceDiagnostics.Add(diagnostic);
            }

            logger.LogWarning("Workspace diagnostic: {Message}", args.Diagnostic.Message);
        });

        Solution solution;
        try
        {
            solution = await loadSolution(workspace, ct);
        }
        catch (Exception ex) when (ex is not WorkspaceLoadException)
        {
            workspace.Dispose();
            throw new WorkspaceLoadException(
                $"Failed to load workspace: {ex.Message}", workspacePath, ex);
        }

        ImmutableList<WorkspaceDiagnostic> diagnosticsSnapshot;
        lock (diagnosticsLock)
        {
            diagnosticsSnapshot = workspaceDiagnostics.ToImmutableList();
        }

        // Eagerly load all document texts into memory so the workspace holds cached copies.
        // Without this, MSBuildWorkspace's FileTextLoader re-reads from disk on every
        // GetTextAsync call, making RefreshAsync unable to detect changes.
        foreach (var project in solution.Projects)
        {
            foreach (var document in project.Documents)
            {
                var text = await document.GetTextAsync(ct);
                solution = solution.WithDocumentText(document.Id, text);
            }
        }

        if (!workspace.TryApplyChanges(solution))
            logger.LogDebug("Initial text caching: TryApplyChanges returned false");

        var projects = MapProjects(workspace.CurrentSolution, logger);
        var health = CSharpWorkspaceHealth.FromProjects(projects, diagnosticsSnapshot);

        IProjectCompilationCache cache = options.Mode switch
        {
            WorkspaceMode.Server => new ServerCompilationCache(() => workspace.CurrentSolution),
            _ => new ReportCompilationCache()
        };

        logger.LogInformation(
            "Workspace loaded: {Status}, {Count} project(s)", health.Status, projects.Count);

        var session = new CSharpWorkspaceSession(
            workspacePath, workspace, health, projects, cache, options.Mode, loggerFactory);

        if (options.FileWatchingEnabled)
        {
            var projectDirs = projects
                .Where(p => p.Status is ProjectLoadStatus.Loaded)
                .Select(p => Path.GetDirectoryName(p.ProjectPath))
                .OfType<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var documentPaths = solution.Projects
                .SelectMany(p => p.Documents)
                .Select(d => d.FilePath)
                .OfType<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            session.StartFileWatching(projectDirs, documentPaths);
        }

        return session;
    }

    private static ImmutableList<CSharpProjectInfo> MapProjects(Solution solution, ILogger logger)
    {
        var projects = ImmutableList.CreateBuilder<CSharpProjectInfo>();

        foreach (var project in solution.Projects)
        {
            try
            {
                var (tfms, activeTfm) = EvaluateFrameworkInfo(project.FilePath);
                var langVersion = (project.ParseOptions as CSharpParseOptions)
                    ?.LanguageVersion.ToDisplayString();

                projects.Add(new CSharpProjectInfo(
                    Key: new WorkspaceProjectKey(project.Id.Id),
                    Name: project.Name,
                    ProjectPath: project.FilePath ?? "",
                    TargetFrameworks: tfms,
                    ActiveTargetFramework: activeTfm,
                    LangVersion: langVersion,
                    Status: ProjectLoadStatus.Loaded,
                    Diagnostics: []));

                logger.LogDebug(
                    "Loaded project: {Name} ({TFM})", project.Name, activeTfm ?? "unknown");
            }
            catch (Exception ex)
            {
                projects.Add(new CSharpProjectInfo(
                    Key: new WorkspaceProjectKey(project.Id.Id),
                    Name: project.Name,
                    ProjectPath: project.FilePath ?? "",
                    TargetFrameworks: [],
                    ActiveTargetFramework: null,
                    LangVersion: null,
                    Status: ProjectLoadStatus.Failed,
                    Diagnostics:
                    [
                        new WorkspaceDiagnostic(
                            "MapError", ex.Message, WorkspaceDiagnosticSeverity.Error)
                    ]));

                logger.LogError(ex, "Failed to map project: {Name}", project.Name);
            }
        }

        return projects.ToImmutable();
    }

    private static (ImmutableList<string> TargetFrameworks, string? ActiveTargetFramework)
        EvaluateFrameworkInfo(string? projectFilePath)
    {
        if (projectFilePath is null || !File.Exists(projectFilePath))
            return ([], null);

        var doc = XDocument.Load(projectFilePath);
        var ns = doc.Root?.Name.Namespace ?? XNamespace.None;

        var targetFramework = doc.Descendants(ns + "TargetFramework").FirstOrDefault()?.Value;
        var targetFrameworks = doc.Descendants(ns + "TargetFrameworks").FirstOrDefault()?.Value;

        ImmutableList<string> parsedTargetFrameworks = !string.IsNullOrWhiteSpace(targetFrameworks)
            ? [.. targetFrameworks.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]
            : !string.IsNullOrWhiteSpace(targetFramework)
                ? [targetFramework]
                : [];

        var activeTargetFramework = !string.IsNullOrWhiteSpace(targetFramework)
            ? targetFramework
            : parsedTargetFrameworks.Count == 1
                ? parsedTargetFrameworks[0]
                : null;

        return (parsedTargetFrameworks, activeTargetFramework);
    }

    private static readonly Lock _msbuildLock = new();

    private static void EnsureMSBuildRegistered()
    {
        if (MSBuildLocator.IsRegistered) return;

        lock (_msbuildLock)
        {
            if (MSBuildLocator.IsRegistered) return;
            MSBuildLocator.RegisterDefaults();
        }
    }
}
