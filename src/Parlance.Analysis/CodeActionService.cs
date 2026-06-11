using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using Parlance.Analyzers.Upstream;
using Parlance.CSharp.Workspace;

namespace Parlance.Analysis;

public sealed class CodeActionService(
    WorkspaceSessionHolder holder, ILogger<CodeActionService> logger)
{
    private readonly ConcurrentDictionary<string, CachedCodeAction> _actionCache = new();
    private readonly ConcurrentDictionary<string, ImmutableArray<CodeFixProvider>> _fixProvidersByTfm = new();
    private readonly ConcurrentDictionary<string, ImmutableArray<CodeRefactoringProvider>> _refactoringProvidersByTfm = new();
    private int _nextFixId;
    private int _nextRefactorId;

    private CSharpWorkspaceSession Session => holder.LoadedSession;

    private sealed record ResolvedDocument(Document Document, string TargetFramework);

    private ResolvedDocument? ResolveDocument(string filePath)
    {
        // Resolve workspace-relative inputs (a client echoing a serialized RepoPath) to absolute.
        filePath = Session.NormalizeInputPath(filePath);
        var docId = Session.CurrentSolution.GetDocumentIdsWithFilePath(filePath).FirstOrDefault();
        if (docId is null) return null;

        var document = Session.CurrentSolution.GetDocument(docId);
        if (document is null) return null;

        var tfm = Session.Projects
            .FirstOrDefault(p => p.Name == document.Project.Name)
            ?.ActiveTargetFramework ?? "net10.0";

        return new ResolvedDocument(document, tfm);
    }

    private ImmutableArray<CodeFixProvider> GetFixProviders(string targetFramework) =>
        _fixProvidersByTfm.GetOrAdd(targetFramework, tfm =>
        {
            var providers = new List<CodeFixProvider>();
            providers.AddRange(FixProviderLoader.LoadAll(tfm));
            providers.AddRange(DiscoverFromAssembly<CodeFixProvider>("Microsoft.CodeAnalysis.CSharp.Features"));
            providers.AddRange(DiscoverFromAssembly<CodeFixProvider>("Microsoft.CodeAnalysis.Features"));
            return [.. providers];
        });

    private ImmutableArray<CodeRefactoringProvider> GetRefactoringProviders(string targetFramework) =>
        _refactoringProvidersByTfm.GetOrAdd(targetFramework, tfm =>
        {
            var providers = new List<CodeRefactoringProvider>();
            providers.AddRange(RefactoringProviderLoader.LoadAll(tfm));
            providers.AddRange(DiscoverFromAssembly<CodeRefactoringProvider>("Microsoft.CodeAnalysis.CSharp.Features"));
            providers.AddRange(DiscoverFromAssembly<CodeRefactoringProvider>("Microsoft.CodeAnalysis.Features"));
            return [.. providers];
        });

    public async Task<ImmutableList<CodeFixEntry>> GetCodeFixesAsync(
        string filePath, int line, string? diagnosticId = null, CancellationToken ct = default)
    {
        EvictStaleEntries(Session.SnapshotVersion);

        var resolvedDoc = ResolveDocument(filePath);
        if (resolvedDoc is null) return [];

        var compilation = await resolvedDoc.Document.Project.GetCompilationAsync(ct);
        if (compilation is null) return [];

        var tree = await resolvedDoc.Document.GetSyntaxTreeAsync(ct);
        if (tree is null) return [];

        var text = await tree.GetTextAsync(ct);
        var zeroLine = line - 1;
        if (zeroLine < 0 || zeroLine >= text.Lines.Count) return [];

        var lineSpan = text.Lines[zeroLine].Span;

        // Every diagnostic in this file (tree-wide). Two uses: filter down to the requested line, and count
        // how often each rule fires across the document — a fix-all is only worth offering when a rule
        // appears more than once.
        var treeCompilerDiags = compilation.GetDiagnostics(ct)
            .Where(d => d.Location.IsInSource && d.Location.SourceTree == tree);

        var analyzers = AnalyzerLoader.LoadAll(resolvedDoc.TargetFramework);
        var treeAnalyzerDiags = (await compilation
                .WithProjectAnalyzers(analyzers, resolvedDoc.Document.Project).GetAnalyzerDiagnosticsAsync(ct))
            .Where(d => d.Location.IsInSource && d.Location.SourceTree == tree);

        var treeDiags = treeCompilerDiags.Concat(treeAnalyzerDiags).ToImmutableList();

        var occurrencesByRule = treeDiags
            .GroupBy(d => d.Id)
            .ToDictionary(g => g.Key, g => g.DistinctBy(d => d.Location.SourceSpan).Count());

        var lineDiags = treeDiags
            .Where(d => d.Location.SourceSpan.IntersectsWith(lineSpan))
            .Where(d => diagnosticId is null || d.Id == diagnosticId)
            .DistinctBy(d => (d.Id, d.Location.SourceSpan))
            .ToImmutableList();

        if (lineDiags.IsEmpty) return [];

        var fixes = new List<CodeFixEntry>();
        var snapshotVersion = Session.SnapshotVersion;

        // The same provider is discovered from more than one features assembly, so one fixable diagnostic
        // can register the same action twice. Dedupe on (rule, equivalence, title) to drop the doubles.
        var seenActions = new HashSet<(string, string, string)>();
        // One fix-all per (rule, equivalence) whose rule fires more than once and whose provider advertises a
        // FixAllProvider. Collected during the per-occurrence pass, emitted once after it.
        var fixAllCandidates = new Dictionary<(string Rule, string Equivalence), FixAllCandidate>();

        foreach (var diagnostic in lineDiags)
        {
            foreach (var provider in GetFixProviders(resolvedDoc.TargetFramework))
            {
                if (!provider.FixableDiagnosticIds.Contains(diagnostic.Id))
                    continue;

                var codeActions = new List<CodeAction>();
                var context = new CodeFixContext(resolvedDoc.Document, diagnostic,
                    (action, _) => codeActions.Add(action), ct);

                try
                {
                    await provider.RegisterCodeFixesAsync(context);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "CodeFixProvider {Provider} failed for {DiagId}",
                        provider.GetType().Name, diagnostic.Id);
                    continue;
                }

                foreach (var action in codeActions)
                {
                    var equivalence = action.EquivalenceKey ?? action.Title;
                    if (!seenActions.Add((diagnostic.Id, equivalence, action.Title)))
                        continue;

                    var id = $"fix-{Interlocked.Increment(ref _nextFixId)}";
                    _actionCache[id] = new CachedCodeAction(action, snapshotVersion);

                    var scope = DetermineScope(provider);
                    fixes.Add(new CodeFixEntry(id, action.Title, diagnostic.Id,
                        diagnostic.GetMessage(), scope));

                    if (occurrencesByRule.GetValueOrDefault(diagnostic.Id) > 1 &&
                        provider.GetFixAllProvider() is not null)
                    {
                        fixAllCandidates.TryAdd((diagnostic.Id, equivalence),
                            new FixAllCandidate(provider, diagnostic.Id, action.EquivalenceKey, action.Title));
                    }
                }
            }
        }

        // Emit one fix-all per candidate. The merge is lazy — it only runs when the action is previewed or
        // applied (CodeAction.Create defers createChangedSolution), so listing fixes stays cheap.
        foreach (var candidate in fixAllCandidates.Values)
        {
            var occurrences = occurrencesByRule[candidate.Rule];
            var action = CreateDocumentFixAllAction(resolvedDoc, candidate, occurrences);
            var id = $"fix-{Interlocked.Increment(ref _nextFixId)}";
            _actionCache[id] = new CachedCodeAction(action, snapshotVersion);
            fixes.Add(new CodeFixEntry(id, action.Title, candidate.Rule, "", "document", IsFixAll: true));
        }

        return [.. fixes];
    }

    private sealed record FixAllCandidate(
        CodeFixProvider Provider, string Rule, string? EquivalenceKey, string SingleTitle);

    // Builds a lazy "fix all in document" action. Roslyn's FixAllContext takes an internal DiagnosticProvider
    // we cannot subclass, so instead of driving its batch engine we merge the provider's own per-occurrence
    // fixes: re-find every diagnostic of the rule in the file, register the matching fix for each, and combine
    // their (independent, against-original) text changes into one solution. The fixes touch disjoint spans, so
    // the merge is a straight non-overlapping splice.
    private CodeAction CreateDocumentFixAllAction(
        ResolvedDocument resolvedDoc, FixAllCandidate candidate, int occurrences)
    {
        var document = resolvedDoc.Document;
        var tfm = resolvedDoc.TargetFramework;
        var title = $"Fix all {occurrences} '{candidate.Rule}' in document";

        return CodeAction.Create(title, async cancel =>
        {
            var solution = document.Project.Solution;
            var compilation = await document.Project.GetCompilationAsync(cancel);
            var tree = await document.GetSyntaxTreeAsync(cancel);
            if (compilation is null || tree is null) return solution;

            var analyzers = AnalyzerLoader.LoadAll(tfm);
            var diagnostics = (await compilation
                    .WithProjectAnalyzers(analyzers, document.Project).GetAnalyzerDiagnosticsAsync(cancel))
                .Concat(compilation.GetDiagnostics(cancel))
                .Where(d => d.Id == candidate.Rule && d.Location.IsInSource && d.Location.SourceTree == tree)
                .DistinctBy(d => d.Location.SourceSpan)
                .ToImmutableList();

            var changesByDocument = new Dictionary<DocumentId, List<TextChange>>();

            foreach (var diagnostic in diagnostics)
            {
                var actions = new List<CodeAction>();
                var context = new CodeFixContext(document, diagnostic, (a, _) => actions.Add(a), cancel);
                try
                {
                    await candidate.Provider.RegisterCodeFixesAsync(context);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Fix-all: provider {Provider} failed for {DiagId}",
                        candidate.Provider.GetType().Name, candidate.Rule);
                    continue;
                }

                // Apply the same fix variant the single-occurrence entry would, identified by equivalence key
                // (falling back to title when a provider leaves the key null).
                var pick = actions.FirstOrDefault(a =>
                               string.Equals(a.EquivalenceKey, candidate.EquivalenceKey, StringComparison.Ordinal))
                           ?? actions.FirstOrDefault(a => a.Title == candidate.SingleTitle);
                if (pick is null) continue;

                var operation = (await pick.GetOperationsAsync(cancel)).OfType<ApplyChangesOperation>().FirstOrDefault();
                if (operation is null) continue;

                foreach (var projectChange in operation.ChangedSolution.GetChanges(solution).GetProjectChanges())
                    foreach (var docId in projectChange.GetChangedDocuments())
                    {
                        var before = solution.GetDocument(docId);
                        var after = operation.ChangedSolution.GetDocument(docId);
                        if (before is null || after is null) continue;
                        var textChanges = await after.GetTextChangesAsync(before, cancel);
                        if (!changesByDocument.TryGetValue(docId, out var list))
                            changesByDocument[docId] = list = [];
                        list.AddRange(textChanges);
                    }
            }

            var merged = solution;
            foreach (var (docId, changes) in changesByDocument)
            {
                var oldText = await merged.GetDocument(docId)!.GetTextAsync(cancel);
                merged = merged.WithDocumentText(docId, oldText.WithChanges(MergeNonOverlapping(changes)));
            }

            return merged;
        }, equivalenceKey: $"FixAll:{candidate.Rule}:{candidate.EquivalenceKey}");
    }

    // Sorts text changes by position and drops exact duplicates and any change overlapping one already kept,
    // so SourceText.WithChanges never sees the overlapping spans it would throw on. Fixes for distinct
    // diagnostics target disjoint spans in practice; this only guards the pathological case.
    internal static ImmutableArray<TextChange> MergeNonOverlapping(IEnumerable<TextChange> changes)
    {
        var ordered = changes.Distinct().OrderBy(c => c.Span.Start).ThenBy(c => c.Span.End).ToImmutableArray();
        var kept = ImmutableArray.CreateBuilder<TextChange>();
        var lastEnd = -1;
        foreach (var change in ordered)
        {
            if (change.Span.Start < lastEnd) continue;
            kept.Add(change);
            lastEnd = change.Span.End;
        }
        return kept.ToImmutable();
    }

    public async Task<ImmutableList<RefactoringEntry>> GetRefactoringsAsync(
        string filePath, int line, int column, int? endLine = null, int? endColumn = null,
        CancellationToken ct = default)
    {
        EvictStaleEntries(Session.SnapshotVersion);

        var resolvedDoc = ResolveDocument(filePath);
        if (resolvedDoc is null) return [];

        var text = await resolvedDoc.Document.GetTextAsync(ct);
        var zeroLine = line - 1;
        var zeroCol = column - 1;
        if (zeroLine < 0 || zeroLine >= text.Lines.Count) return [];

        var lineLength = text.Lines[zeroLine].Span.Length;
        if (zeroCol < 0 || zeroCol > lineLength) return [];

        // Validate range inputs: both must be provided together
        if (endLine is not null != endColumn is not null) return [];

        TextSpan span;
        if (endLine is not null && endColumn is not null)
        {
            var zeroEndLine = endLine.Value - 1;
            var zeroEndCol = endColumn.Value - 1;
            if (zeroEndLine < 0 || zeroEndLine >= text.Lines.Count) return [];
            var endLineLength = text.Lines[zeroEndLine].Span.Length;
            if (zeroEndCol < 0 || zeroEndCol > endLineLength) return [];

            var startPos = text.Lines.GetPosition(new LinePosition(zeroLine, zeroCol));
            var endPos = text.Lines.GetPosition(new LinePosition(zeroEndLine, zeroEndCol));
            if (endPos < startPos) return [];
            span = TextSpan.FromBounds(startPos, endPos);
        }
        else
        {
            var position = text.Lines.GetPosition(new LinePosition(zeroLine, zeroCol));
            var root = await resolvedDoc.Document.GetSyntaxRootAsync(ct);
            if (root is null) return [];
            var token = root.FindToken(position);
            span = token.Span;
        }

        var refactorings = new List<RefactoringEntry>();
        var snapshotVersion = Session.SnapshotVersion;

        foreach (var provider in GetRefactoringProviders(resolvedDoc.TargetFramework))
        {
            var codeActions = new List<CodeAction>();
            var context = new CodeRefactoringContext(resolvedDoc.Document, span,
                action => codeActions.Add(action), ct);

            try
            {
                await provider.ComputeRefactoringsAsync(context);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "CodeRefactoringProvider {Provider} failed",
                    provider.GetType().Name);
                continue;
            }

            foreach (var action in codeActions)
            {
                var id = $"refactor-{Interlocked.Increment(ref _nextRefactorId)}";
                _actionCache[id] = new CachedCodeAction(action, snapshotVersion);
                refactorings.Add(new RefactoringEntry(id, action.Title, null));
            }
        }

        return [.. refactorings];
    }

    public async Task<CodeActionPreview?> PreviewAsync(string actionId, CancellationToken ct = default)
    {
        var resolution = await ResolveApplyOperationAsync(actionId, ct);
        return resolution switch
        {
            null => null,
            ActionResolution.Expired e => CodeActionPreview.Expired(actionId, e.Action.Title),
            ActionResolution.Failed f => CodeActionPreview.Failed(actionId, f.Action.Title, f.Message),
            ActionResolution.Resolved r => await BuildPreviewAsync(actionId, r.Action.Title, r, ct),
            _ => null,
        };
    }

    /// <summary>
    /// Computes the complete, applyable <see cref="CodeActionEdit"/> (LSP <c>WorkspaceEdit</c>) for a cached
    /// action — text edits plus create/delete/rename resource operations. Returns <c>null</c> when the
    /// action ID is unknown. Parlance never writes the result to disk; the caller applies and persists it.
    /// </summary>
    public async Task<CodeActionEdit?> ApplyAsync(string actionId, CancellationToken ct = default)
    {
        var resolution = await ResolveApplyOperationAsync(actionId, ct);
        return resolution switch
        {
            null => null,
            ActionResolution.Expired e => CodeActionEdit.Expired(actionId, e.Action.Title),
            ActionResolution.Failed f => CodeActionEdit.Failed(actionId, f.Action.Title, f.Message),
            ActionResolution.Resolved r => await WorkspaceEditBuilder.BuildAsync(
                actionId, r.Action.Title, r.Solution, r.ChangedSolution,
                Session.BufferVersion, r.SnapshotVersion, ct),
            _ => null,
        };
    }

    private async Task<CodeActionPreview> BuildPreviewAsync(
        string actionId, string title, ActionResolution.Resolved resolved, CancellationToken ct)
    {
        // Diff against the same solution snapshot the action was resolved against (not a re-read of
        // Session.CurrentSolution), so a concurrent refresh can't shift the preview baseline. The changed
        // side is the formatted result, so the preview diff matches what apply will emit.
        var changedSolution = resolved.ChangedSolution;
        var currentSolution = resolved.Solution;

        var changes = new List<FileChange>();
        foreach (var projectChanges in changedSolution.GetChanges(currentSolution).GetProjectChanges())
        {
            foreach (var docId in projectChanges.GetChangedDocuments())
            {
                var oldDoc = currentSolution.GetDocument(docId);
                var newDoc = changedSolution.GetDocument(docId);
                if (oldDoc is null || newDoc is null) continue;

                var oldText = await oldDoc.GetTextAsync(ct);
                var newText = await newDoc.GetTextAsync(ct);

                // Preview is a judgment surface: render the change as a unified diff (hunks with context) so
                // the agent can read how the result actually reads — long lines, nesting, a switch expression
                // gone ugly — before applying. The minimal applyable edits come from apply-code-action.
                var diff = UnifiedDiff.Render(oldText, newText);
                if (diff.Length == 0) continue;

                changes.Add(new FileChange(oldDoc.FilePath ?? "", diff));
            }
        }

        return new CodeActionPreview(actionId, title, [.. changes]);
    }

    // Shared lookup → operation resolution behind both preview and apply: cache hit, snapshot match,
    // GetOperationsAsync, and extraction of the ApplyChangesOperation. Returns null when the ID is unknown.
    private async Task<ActionResolution?> ResolveApplyOperationAsync(string actionId, CancellationToken ct)
    {
        if (!_actionCache.TryGetValue(actionId, out var cached))
            return null;

        // Capture the diff baseline (solution) and the version to stamp as one consistent read: read the
        // version, the solution, then the version again, and require all three to agree with the snapshot
        // the action was cached against. A concurrent file-watch / sync-buffer that advances the solution
        // mid-resolution surfaces as a mismatch → Expired (best-effort staleness, never a false success),
        // rather than an edit diffed against the wrong base or stamped with a newer version than computed on.
        var versionBefore = Session.SnapshotVersion;
        var solution = Session.CurrentSolution;
        var versionAfter = Session.SnapshotVersion;
        if (cached.SnapshotVersion != versionBefore || versionBefore != versionAfter)
            return new ActionResolution.Expired(cached.Action);

        ImmutableArray<CodeActionOperation> operations;
        try
        {
            operations = await cached.Action.GetOperationsAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (ReflectionTypeLoadException ex)
        {
            logger.LogError(ex, "Code action '{ActionId}' failed due to missing assembly dependencies", actionId);
            return new ActionResolution.Failed(cached.Action,
                "Code action failed: missing runtime dependency. " +
                string.Join("; ", ex.LoaderExceptions?.Select(e => e?.Message).Where(m => m is not null).Distinct() ?? []));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Code action '{ActionId}' failed during resolution", actionId);
            return new ActionResolution.Failed(cached.Action, "Code action failed: " + ex.Message);
        }

        var applyOp = operations.OfType<ApplyChangesOperation>().FirstOrDefault();
        if (applyOp is null)
            return new ActionResolution.Failed(cached.Action,
                "Code action does not produce text changes that can be applied.");

        var formatted = await FormatChangedDocumentsAsync(solution, applyOp.ChangedSolution, ct);
        return new ActionResolution.Resolved(cached.Action, applyOp, solution, formatted, versionBefore);
    }

    // Runs Roslyn's formatter over the regions a code action tagged with Formatter.Annotation — the same
    // post-processing an LSP host (OmniSharp, Roslyn LSP) applies before returning a WorkspaceEdit. It is
    // scoped to the annotated spans, so it normalises only what the action touched and never reformats the
    // rest of the file. Documents the action did not annotate come back unchanged.
    internal static async Task<Solution> FormatChangedDocumentsAsync(
        Solution current, Solution changed, CancellationToken ct)
    {
        var result = changed;
        foreach (var projectChange in changed.GetChanges(current).GetProjectChanges())
            foreach (var docId in projectChange.GetChangedDocuments())
            {
                var document = result.GetDocument(docId);
                if (document is null) continue;
                var formatted = await Formatter.FormatAsync(document, Formatter.Annotation, options: null, ct);
                result = formatted.Project.Solution;
            }

        return result;
    }

    private abstract record ActionResolution
    {
        // Solution/SnapshotVersion are the consistent capture the action was diffed against and is stamped
        // with. ChangedSolution is the action's result after the annotation-scoped formatting pass — preview
        // and apply both diff against it so what an agent previews is exactly what it applies.
        public sealed record Resolved(
            CodeAction Action, ApplyChangesOperation Operation, Solution Solution, Solution ChangedSolution,
            long SnapshotVersion)
            : ActionResolution;
        public sealed record Expired(CodeAction Action) : ActionResolution;
        public sealed record Failed(CodeAction Action, string Message) : ActionResolution;
    }

    // Drop cached actions from superseded snapshots. Cached entries pin Roslyn CodeAction
    // objects; without eviction a long-running server session grows unbounded as files change.
    // Entries from the current snapshot are still previewable and are kept.
    internal void EvictStaleEntries(long currentVersion)
    {
        foreach (var (id, cached) in _actionCache)
            if (cached.SnapshotVersion < currentVersion)
                _actionCache.TryRemove(id, out _);
    }

    internal int CacheCount => _actionCache.Count;

    internal void AddToCacheForTest(string id, long snapshotVersion) =>
        _actionCache[id] = new CachedCodeAction(
            CodeAction.Create("test", _ => Task.FromResult<Document>(null!)), snapshotVersion);

    // Builds a TextEdit with full start/end line+column. Columns are computed regardless,
    // so edits on the same line stay distinguishable in previews.
    internal static TextEdit ToTextEdit(SourceText oldText, TextChange change)
    {
        var start = oldText.Lines.GetLinePosition(change.Span.Start);
        var end = oldText.Lines.GetLinePosition(change.Span.End);
        var range = new TextRange(
            start.Line + 1, start.Character + 1,
            end.Line + 1, end.Character + 1);
        var originalText = oldText.GetSubText(change.Span).ToString();
        return new TextEdit(range, originalText, change.NewText ?? "");
    }

    private static string DetermineScope(CodeFixProvider provider)
    {
        var fixAll = provider.GetFixAllProvider();
        if (fixAll is null) return "document";

        var scopes = fixAll.GetSupportedFixAllScopes();
        if (scopes.Contains(FixAllScope.Solution)) return "solution";
        if (scopes.Contains(FixAllScope.Project)) return "project";
        if (scopes.Contains(FixAllScope.Document)) return "document";
        return "document";
    }

    private List<T> DiscoverFromAssembly<T>(string assemblyName) where T : class
    {
        var results = new List<T>();
        Assembly assembly;
        try
        {
            assembly = Assembly.Load(assemblyName);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not load assembly {Name} for provider discovery", assemblyName);
            return results;
        }

        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types.Where(t => t is not null).ToArray()!;
        }
        catch
        {
            return results;
        }

        foreach (var type in types)
        {
            if (type.IsAbstract || !typeof(T).IsAssignableFrom(type))
                continue;

            try
            {
                if (Activator.CreateInstance(type) is T instance)
                    results.Add(instance);
            }
            catch
            {
                // Skip types that can't be instantiated
            }
        }

        if (results.Count == 0)
            logger.LogWarning("Discovered 0 {Type} from {Assembly} — provider loading may have silently failed", typeof(T).Name, assemblyName);
        else
            logger.LogDebug("Discovered {Count} {Type} from {Assembly}", results.Count, typeof(T).Name, assemblyName);
        return results;
    }
}

public sealed record CachedCodeAction(CodeAction Action, long SnapshotVersion);

public sealed record CodeFixEntry(
    string Id, string Title, string DiagnosticId, string DiagnosticMessage, string Scope,
    bool IsFixAll = false);

public sealed record RefactoringEntry(string Id, string Title, string? Category);

public sealed record CodeActionPreview(
    string ActionId, string Title, ImmutableList<FileChange> Changes,
    bool IsExpired = false, string? ErrorMessage = null)
{
    public static CodeActionPreview Expired(string actionId, string title) =>
        new(actionId, title, [], IsExpired: true);
    public static CodeActionPreview Failed(string actionId, string title, string errorMessage) =>
        new(actionId, title, [], ErrorMessage: errorMessage);
}

/// <summary>One changed file in a preview: its path plus a unified diff (hunks with context) of the change.</summary>
public sealed record FileChange(string FilePath, string Diff);

public sealed record TextEdit(TextRange Range, string OriginalText, string NewText);

public sealed record TextRange(int StartLine, int StartColumn, int EndLine, int EndColumn);
