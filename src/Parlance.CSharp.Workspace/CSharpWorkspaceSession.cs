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

public sealed class CSharpWorkspaceSession : IDisposable, IAsyncDisposable
{
    // Load-only after LoadAsync completes. Never use _workspace.CurrentSolution as a
    // source of truth — it is not updated after the initial load. Use CurrentSolution instead.
    private readonly MSBuildWorkspace _workspace;
    private readonly IProjectCompilationCache _cache;
    private readonly ILogger<CSharpWorkspaceSession> _logger;
    private readonly WorkspaceMode _mode;
    private readonly ILoggerFactory _loggerFactory;
    private WorkspaceFileWatcher? _watcher;
    private long _snapshotVersion = 1;
    private volatile Solution _currentSolution;
    private readonly SemaphoreSlim _solutionLock = new(1, 1);

    // Paths with a live client buffer overlay -> per-document version. Overlay wins over disk
    // until CloseBufferAsync (D3). Keyed case-insensitively to match GetDocumentIdsWithFilePath.
    // Lock ordering invariant: always acquire _solutionLock BEFORE this lock to avoid deadlock.
    private readonly Dictionary<string, long> _openBufferVersions =
        new(StringComparer.OrdinalIgnoreCase);

    private CSharpWorkspaceSession(
        string workspacePath,
        MSBuildWorkspace workspace,
        Solution initialSolution,
        CSharpWorkspaceHealth health,
        ImmutableList<CSharpProjectInfo> projects,
        WorkspaceMode mode,
        ILoggerFactory loggerFactory)
    {
        WorkspacePath = workspacePath;
        _workspace = workspace;
        _currentSolution = initialSolution;   // must precede _cache initialisation
        _cache = mode switch
        {
            WorkspaceMode.Server => new ServerCompilationCache(() => _currentSolution),
            _ => new ReportCompilationCache()
        };
        Health = health;
        Projects = projects;
        _mode = mode;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<CSharpWorkspaceSession>();
    }

    public string WorkspacePath { get; }

    /// <summary>
    /// The directory that owns the solution/project file — the root for repo-relative paths and
    /// the <c>.parlance/</c> convention directory. Single definition shared by every consumer.
    /// </summary>
    public string RepoPath => Path.GetDirectoryName(WorkspacePath) ?? WorkspacePath;

    public long SnapshotVersion => Interlocked.Read(ref _snapshotVersion);

    public CSharpWorkspaceHealth Health { get; }

    public ImmutableList<CSharpProjectInfo> Projects { get; }

    /// <summary>
    /// The live in-memory solution snapshot. Always use this — never <c>_workspace.CurrentSolution</c>.
    /// When calling <see cref="IProjectCompilationCache.GetAsync"/>, source the <c>Project</c>
    /// argument from this snapshot so compilation reflects the latest file changes.
    /// </summary>
    public Solution CurrentSolution => _currentSolution;

    internal Task<ProjectCompilationState> GetCompilationStateAsync(Project project, CancellationToken ct = default) =>
        _cache.GetAsync(project, ct);

    public CSharpProjectInfo? GetProject(WorkspaceProjectKey key) =>
        Projects.FirstOrDefault(p => p.Key == key);

    public CSharpProjectInfo? GetProjectByPath(string projectPath) =>
        Projects.FirstOrDefault(p =>
            string.Equals(p.ProjectPath, projectPath, StringComparison.OrdinalIgnoreCase));

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        if (_mode is WorkspaceMode.Report)
            throw new InvalidOperationException("RefreshAsync is not supported in Report mode");

        await _solutionLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var solution = _currentSolution;
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

            _currentSolution = solution;

            foreach (var projectId in affectedProjects)
                _cache.MarkDirty(projectId);

            Interlocked.Increment(ref _snapshotVersion);
            _logger.LogInformation(
                "RefreshAsync: {Count} project(s) updated, SnapshotVersion={Version}",
                affectedProjects.Count, SnapshotVersion);
        }
        finally
        {
            _solutionLock.Release();
        }
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
        await _solutionLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var solution = _currentSolution;
            var affectedProjects = new HashSet<ProjectId>();
            var hasChanges = false;

            foreach (var filePath in changedPaths)
            {
                var docIds = solution.GetDocumentIdsWithFilePath(filePath);
                if (docIds.IsEmpty) continue;

                // D3: overlay wins until close — skip disk changes for paths with a live buffer.
                if (IsBufferOpen(filePath))
                {
                    _logger.LogDebug("Skipping disk change for overlaid buffer: {Path}", filePath);
                    continue;
                }

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

            _currentSolution = solution;

            foreach (var projectId in affectedProjects)
                _cache.MarkDirty(projectId);

            Interlocked.Increment(ref _snapshotVersion);
            _logger.LogInformation(
                "File changes applied: {Count} file(s), SnapshotVersion={Version}",
                changedPaths.Count, SnapshotVersion);
        }
        finally
        {
            _solutionLock.Release();
        }
    }

    /// <summary>True while <paramref name="filePath"/> has a client buffer overlay (disk is ignored for it).</summary>
    public bool IsBufferOpen(string filePath)
    {
        lock (_openBufferVersions)
            return _openBufferVersions.ContainsKey(filePath);
    }

    /// <summary>
    /// Overlays client buffer text onto the live solution (open or update). Returns the new
    /// per-document version, or 0 if the path is not a document in this workspace. Disk is untouched.
    /// </summary>
    public async Task<long> SyncBufferAsync(string filePath, string text, CancellationToken ct = default)
    {
        if (_mode is WorkspaceMode.Report)
            throw new InvalidOperationException("SyncBufferAsync is not supported in Report mode");

        await _solutionLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var solution = _currentSolution;
            var docIds = solution.GetDocumentIdsWithFilePath(filePath);
            if (docIds.IsEmpty) return 0;

            var existing = solution.GetDocument(docIds[0]);
            var encoding = existing is not null
                ? (await existing.GetTextAsync(ct)).Encoding
                : Encoding.UTF8;
            var newText = SourceText.From(text, encoding);

            foreach (var docId in docIds)
            {
                solution = solution.WithDocumentText(docId, newText);
                var projectId = solution.GetDocument(docId)?.Project.Id;
                if (projectId is not null)
                    _cache.MarkDirty(projectId);
            }
            _currentSolution = solution;

            long version;
            lock (_openBufferVersions)
            {
                version = _openBufferVersions.TryGetValue(filePath, out var v) ? v + 1 : 1;
                _openBufferVersions[filePath] = version;
            }

            Interlocked.Increment(ref _snapshotVersion);
            _logger.LogInformation(
                "Buffer synced: {Path} (docVersion={DocVersion}, SnapshotVersion={Version})",
                filePath, version, SnapshotVersion);
            return version;
        }
        finally
        {
            _solutionLock.Release();
        }
    }

    /// <summary>Drops the overlay for <paramref name="filePath"/> and reverts that document to disk text.</summary>
    public async Task CloseBufferAsync(string filePath, CancellationToken ct = default)
    {
        if (_mode is WorkspaceMode.Report)
            throw new InvalidOperationException("CloseBufferAsync is not supported in Report mode");

        await _solutionLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Check (do NOT remove) whether the path is open; if not, return early with no snapshot bump.
            // Removing the key only happens AFTER a successful revert so that a disk-read failure
            // leaves the buffer open and retryable (overlay suppression preserved, _currentSolution
            // not in a half-reverted state).
            bool wasOpen;
            lock (_openBufferVersions)
                wasOpen = _openBufferVersions.ContainsKey(filePath);
            if (!wasOpen) return;

            var solution = _currentSolution;
            var docIds = solution.GetDocumentIdsWithFilePath(filePath);
            if (!docIds.IsEmpty && File.Exists(filePath))
            {
                var existing = solution.GetDocument(docIds[0]);
                var encoding = existing is not null
                    ? (await existing.GetTextAsync(ct)).Encoding
                    : Encoding.UTF8;
                var diskText = SourceText.From(await File.ReadAllTextAsync(filePath, ct), encoding);
                foreach (var docId in docIds)
                {
                    solution = solution.WithDocumentText(docId, diskText);
                    var projectId = solution.GetDocument(docId)?.Project.Id;
                    if (projectId is not null)
                        _cache.MarkDirty(projectId);
                }
                _currentSolution = solution;
            }

            // Revert succeeded (or file was deleted — nothing to revert). Remove the key and bump.
            lock (_openBufferVersions)
                _openBufferVersions.Remove(filePath);

            Interlocked.Increment(ref _snapshotVersion);
            _logger.LogInformation("Buffer closed, reverted to disk: {Path}", filePath);
        }
        finally
        {
            _solutionLock.Release();
        }
    }

    /// <summary>
    /// Opens a solution, returning the outcome as a value. A load failure
    /// (file-not-found or MSBuild load error) becomes <see cref="WorkspaceLoadResult.Failure"/>.
    /// Contract violations (null/blank path, Report mode + file watching) still throw;
    /// cancellation propagates as <see cref="OperationCanceledException"/>.
    /// </summary>
    public static async Task<WorkspaceLoadResult> TryOpenSolutionAsync(
        string solutionPath,
        WorkspaceOpenOptions? options = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionPath);
        if (!File.Exists(solutionPath))
            return new WorkspaceLoadResult.Failure(
                new WorkspaceLoadFailure($"Solution file not found: {solutionPath}", solutionPath));

        return await LoadAsync(
            solutionPath,
            (ws, token) => ws.OpenSolutionAsync(solutionPath, cancellationToken: token),
            options ?? new WorkspaceOpenOptions(),
            ct);
    }

    /// <summary>
    /// Opens a project, returning the outcome as a value. See
    /// <see cref="TryOpenSolutionAsync"/> for the failure-vs-throw split.
    /// </summary>
    public static async Task<WorkspaceLoadResult> TryOpenProjectAsync(
        string projectPath,
        WorkspaceOpenOptions? options = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        if (!File.Exists(projectPath))
            return new WorkspaceLoadResult.Failure(
                new WorkspaceLoadFailure($"Project file not found: {projectPath}", projectPath));

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

    public void Dispose()
    {
        _watcher?.Dispose();
        _solutionLock.Dispose();
        _workspace.Dispose();
        _logger.LogInformation("Workspace session disposed (sync): {Path}", WorkspacePath);
    }

    public async ValueTask DisposeAsync()
    {
        if (_watcher is not null)
            await _watcher.DisposeAsync();

        _solutionLock.Dispose();
        _workspace.Dispose();
        _logger.LogInformation("Workspace session disposed: {Path}", WorkspacePath);
    }

    private static async Task<WorkspaceLoadResult> LoadAsync(
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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            workspace.Dispose();
            return new WorkspaceLoadResult.Failure(
                new WorkspaceLoadFailure($"Failed to load workspace: {ex.Message}", workspacePath));
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

        var projects = MapProjects(solution, logger);
        var health = CSharpWorkspaceHealth.FromProjects(projects, diagnosticsSnapshot);

        logger.LogInformation(
            "Workspace loaded: {Status}, {Count} project(s)", health.Status, projects.Count);

        var session = new CSharpWorkspaceSession(
            workspacePath, workspace, solution, health, projects, options.Mode, loggerFactory);

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
                .Where(p => !WorkspaceFileWatcher.IsBuildOutputPath(p)) // #59: MSBuildWorkspace includes generated .cs in obj/ as Documents
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            session.StartFileWatching(projectDirs, documentPaths);
        }

        return new WorkspaceLoadResult.Success(session);
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

                var projectRefs = project.ProjectReferences
                    .Select(r => solution.GetProject(r.ProjectId)?.Name)
                    .OfType<string>()
                    .ToImmutableList();

                projects.Add(new CSharpProjectInfo(
                    Key: new WorkspaceProjectKey(project.Id.Id),
                    Name: project.Name,
                    ProjectPath: project.FilePath ?? "",
                    TargetFrameworks: tfms,
                    ActiveTargetFramework: activeTfm,
                    LangVersion: langVersion,
                    Status: ProjectLoadStatus.Loaded,
                    Diagnostics: [],
                    ProjectReferences: projectRefs));

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
                    ],
                    ProjectReferences: []));

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
