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

    // Documents with a live client buffer overlay -> per-document version. Overlay wins over disk
    // until CloseBufferAsync (D3). Keyed on the canonical Roslyn DocumentId rather than the raw
    // client-supplied path string: overlay-suppression is probed from the file watcher (FullPath)
    // and from Roslyn (document.FilePath), which need not be byte-identical to the sync caller's
    // path (./ segments, separators, symlink/short-name forms). DocumentId is the one identity all
    // three resolve to, so suppression is robust rather than spelling-dependent.
    // Lock ordering invariant: always acquire _solutionLock BEFORE this lock to avoid deadlock.
    private readonly Dictionary<DocumentId, long> _openBufferVersions = new();

    // Highest per-document buffer version ever issued, retained across close. The open-version map above
    // is cleared on CloseBufferAsync, so a close/reopen would otherwise restart at 1 and collide with the
    // DocumentVersion stamped on an in-flight applied edit. Versions are drawn from this water-mark so they
    // are strictly monotonic per document for the whole session and a reused number can never pass the guard.
    // Guarded by the same lock as _openBufferVersions.
    private readonly Dictionary<DocumentId, long> _bufferVersionWatermark = new();

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

    /// <summary>
    /// Normalizes a client-supplied file path to the absolute form Roslyn document lookups expect.
    /// Tool output serializes paths workspace-relative (<see cref="Abstractions.RepoPath"/>), so a
    /// client naturally feeds a relative <c>src/...</c> value from one tool's result straight into the
    /// next tool's file argument. Rooted inputs are normalized in place (collapsing <c>.</c>/<c>..</c>);
    /// relative inputs are resolved against the workspace root. The single boundary every file-input
    /// tool/query funnels through so chained calls resolve instead of returning <c>not_found</c>.
    /// </summary>
    public string NormalizeInputPath(string filePath) =>
        string.IsNullOrEmpty(filePath)
            ? filePath
            : Path.IsPathRooted(filePath)
                ? Path.GetFullPath(filePath)
                : Path.GetFullPath(filePath, RepoPath);

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

            // Snapshot the open-buffer ids once (this runs under _solutionLock, which sync/close also take,
            // so the set is stable for the loop). Common case: no overlays → null, and the per-document
            // overlay check below is skipped entirely instead of a solution-wide lookup per document.
            var openDocs = SnapshotOpenBufferDocuments();

            foreach (var project in solution.Projects)
            {
                foreach (var document in project.Documents)
                {
                    if (document.FilePath is null || !File.Exists(document.FilePath))
                        continue;

                    // D3: overlay wins until close — skip disk revert for paths with a live buffer.
                    if (openDocs?.Contains(document.Id) == true)
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

            // Snapshot the open-buffer ids once and reuse the docIds already resolved per path, rather than
            // a second solution-wide lookup inside IsBufferOpen for every changed file.
            var openDocs = SnapshotOpenBufferDocuments();

            foreach (var filePath in changedPaths)
            {
                var docIds = solution.GetDocumentIdsWithFilePath(filePath);
                if (docIds.IsEmpty) continue;

                // D3: overlay wins until close — skip disk changes for paths with a live buffer.
                if (openDocs is not null && docIds.Any(openDocs.Contains))
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

    /// <summary>
    /// The set of documents with a live overlay, or <c>null</c> when there are none. Callers already holding
    /// <c>_solutionLock</c> (refresh / file-watch) snapshot once and membership-test in hand instead of a
    /// solution-wide path lookup per document.
    /// </summary>
    private HashSet<DocumentId>? SnapshotOpenBufferDocuments()
    {
        lock (_openBufferVersions)
            return _openBufferVersions.Count == 0 ? null : [.. _openBufferVersions.Keys];
    }

    /// <summary>True while <paramref name="filePath"/> has a client buffer overlay (disk is ignored for it).</summary>
    public bool IsBufferOpen(string filePath)
    {
        // Resolve the path to its canonical DocumentId(s) through Roslyn so that any spelling of the
        // same file (./, separators, symlink forms) maps to the same overlay state.
        var docIds = _currentSolution.GetDocumentIdsWithFilePath(filePath);
        if (docIds.IsEmpty) return false;
        lock (_openBufferVersions)
            return docIds.Any(_openBufferVersions.ContainsKey);
    }

    /// <summary>
    /// The live client-buffer overlay version for <paramref name="filePath"/>, or <c>null</c> when the
    /// path is not a workspace document or has no open overlay. This is the per-document (didChange-style)
    /// version that <c>sync-buffer</c> returns; an applied edit stamps it so a client can detect when its
    /// in-flight buffer moved out from under a computed edit.
    /// </summary>
    public long? BufferVersion(string filePath)
    {
        var docIds = _currentSolution.GetDocumentIdsWithFilePath(filePath);
        if (docIds.IsEmpty) return null;
        lock (_openBufferVersions)
        {
            long? version = null;
            foreach (var docId in docIds)
                if (_openBufferVersions.TryGetValue(docId, out var v))
                    version = version is { } existing ? Math.Max(existing, v) : v;
            return version;
        }
    }

    /// <summary>
    /// Overlays client buffer text onto the live solution (open or update). Returns the new
    /// per-document version, or 0 if the path is not a document in this workspace. Disk is untouched.
    /// </summary>
    public async Task<long> SyncBufferAsync(string filePath, string text, CancellationToken ct = default)
    {
        if (_mode is WorkspaceMode.Report)
            throw new InvalidOperationException("SyncBufferAsync is not supported in Report mode");

        // Resolve a workspace-relative input (a client echoing a serialized RepoPath) to the absolute form
        // Roslyn document lookup expects — the same boundary every other file-input tool funnels through.
        filePath = NormalizeInputPath(filePath);

        await _solutionLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var solution = _currentSolution;
            var docIds = solution.GetDocumentIdsWithFilePath(filePath);
            if (docIds.IsEmpty) return 0;

            var existing = solution.GetDocument(docIds[0]);
            var currentText = existing is not null ? await existing.GetTextAsync(ct) : null;
            // SourceText.Encoding is nullable (common for in-memory/added documents). Falling through
            // to SourceText.From(text, null) silently substitutes UTF-8-no-BOM; default to the
            // document's on-disk encoding, or UTF-8 when unknown.
            var encoding = currentText?.Encoding ?? Encoding.UTF8;
            var textChanged = currentText is null || currentText.ToString() != text;

            long version;
            lock (_openBufferVersions)
            {
                var alreadyOpen = docIds.Any(_openBufferVersions.ContainsKey);

                // Re-syncing identical text (focus/save/idle echoes) must not advance the snapshot or
                // recompile (RefreshAsync guards the same way). For an already-open buffer that is a
                // pure no-op, return the current version untouched.
                if (!textChanged && alreadyOpen)
                    return docIds.Max(id => _openBufferVersions.TryGetValue(id, out var v) ? v : 0L);

                // Draw the next version from the session-scoped water-mark, not the open-version map, so it
                // stays monotonic across a close/reopen of the same document.
                var previous = docIds.Max(id => _bufferVersionWatermark.TryGetValue(id, out var v) ? v : 0L);
                version = previous + 1;
                foreach (var docId in docIds)
                {
                    _openBufferVersions[docId] = version;
                    _bufferVersionWatermark[docId] = version;
                }
            }

            if (!textChanged)
            {
                // Newly opened buffer whose text already matches the document: record the overlay so
                // disk writes are suppressed, but skip recompilation and the snapshot bump.
                _logger.LogInformation(
                    "Buffer opened (text matches document, no recompile): {Path} (docVersion={DocVersion})",
                    filePath, version);
                return version;
            }

            var newText = SourceText.From(text, encoding);
            foreach (var docId in docIds)
            {
                solution = solution.WithDocumentText(docId, newText);
                _cache.MarkDirty(docId.ProjectId);
            }
            _currentSolution = solution;

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
    public async Task<CloseBufferOutcome> CloseBufferAsync(string filePath, CancellationToken ct = default)
    {
        if (_mode is WorkspaceMode.Report)
            throw new InvalidOperationException("CloseBufferAsync is not supported in Report mode");

        // Resolve a workspace-relative input to absolute, matching SyncBufferAsync; otherwise a client
        // echoing a serialized RepoPath would silently no-op (NotOpen) while the real overlay leaks.
        filePath = NormalizeInputPath(filePath);

        await _solutionLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var solution = _currentSolution;
            var docIds = solution.GetDocumentIdsWithFilePath(filePath);

            // Check (do NOT remove) whether the path is open; if not, return early with no snapshot bump.
            bool wasOpen;
            lock (_openBufferVersions)
                wasOpen = docIds.Any(_openBufferVersions.ContainsKey);
            if (!wasOpen) return CloseBufferOutcome.NotOpen;

            // No disk file to revert to (deleted while open). Reverting is impossible, so leave the
            // overlay AND its keys in place: the buffer stays open and retryable rather than being
            // reported "closed" while phantom unsaved text lingers in _currentSolution. Removing the
            // key only happens AFTER a successful revert (a disk-read failure likewise keeps it open).
            if (!File.Exists(filePath))
            {
                _logger.LogWarning(
                    "close-buffer: disk file missing, cannot revert; buffer left open: {Path}", filePath);
                return CloseBufferOutcome.RevertUnavailable;
            }

            var existing = solution.GetDocument(docIds[0]);
            var currentText = existing is not null ? await existing.GetTextAsync(ct) : null;
            var encoding = currentText?.Encoding ?? Encoding.UTF8;
            var diskContent = await File.ReadAllTextAsync(filePath, ct);

            // Closing a buffer whose overlay text already equals disk (e.g. opened with matching text, or
            // saved before close) is a no-op revert: drop the overlay keys but skip the solution swap,
            // recompile and snapshot bump, mirroring SyncBufferAsync's identical-text guard. Otherwise the
            // version churns for nothing and spuriously invalidates still-valid cached actions.
            var textChanged = currentText is null || currentText.ToString() != diskContent;
            if (textChanged)
            {
                var diskText = SourceText.From(diskContent, encoding);
                foreach (var docId in docIds)
                {
                    solution = solution.WithDocumentText(docId, diskText);
                    _cache.MarkDirty(docId.ProjectId);
                }
                _currentSolution = solution;
            }

            lock (_openBufferVersions)
                foreach (var docId in docIds)
                    _openBufferVersions.Remove(docId);

            if (textChanged)
            {
                Interlocked.Increment(ref _snapshotVersion);
                _logger.LogInformation("Buffer closed, reverted to disk: {Path}", filePath);
            }
            else
            {
                _logger.LogInformation(
                    "Buffer closed (overlay already matched disk, no recompile): {Path}", filePath);
            }

            return CloseBufferOutcome.Closed;
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
