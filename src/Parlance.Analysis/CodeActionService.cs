using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using Parlance.Analyzers.Upstream;
using Parlance.CSharp.Workspace;

namespace Parlance.Analysis;

public sealed class CodeActionService(
    WorkspaceSessionHolder holder, ILogger<CodeActionService> logger)
{
    private readonly ConcurrentDictionary<string, CachedCodeAction> _actionCache = new();
    private int _nextFixId;
    private int _nextRefactorId;

    private ImmutableArray<CodeFixProvider>? _fixProviders;
    private ImmutableArray<CodeRefactoringProvider>? _refactoringProviders;

    private CSharpWorkspaceSession Session => holder.Session;

    private string ResolveTargetFramework() =>
        Session.Projects.FirstOrDefault()?.ActiveTargetFramework ?? "net10.0";

    private ImmutableArray<CodeFixProvider> GetFixProviders()
    {
        if (_fixProviders is not null) return _fixProviders.Value;

        var providers = new List<CodeFixProvider>();

        // External analyzer-shipped providers
        providers.AddRange(AnalyzerLoader.LoadCodeFixProviders(ResolveTargetFramework()));

        // Built-in Roslyn providers (extract method, introduce variable, etc.)
        providers.AddRange(DiscoverFromAssembly<CodeFixProvider>("Microsoft.CodeAnalysis.CSharp.Features"));
        providers.AddRange(DiscoverFromAssembly<CodeFixProvider>("Microsoft.CodeAnalysis.Features"));

        _fixProviders = [.. providers];
        return _fixProviders.Value;
    }

    private ImmutableArray<CodeRefactoringProvider> GetRefactoringProviders()
    {
        if (_refactoringProviders is not null) return _refactoringProviders.Value;

        var providers = new List<CodeRefactoringProvider>();

        // External analyzer-shipped providers
        providers.AddRange(AnalyzerLoader.LoadCodeRefactoringProviders(ResolveTargetFramework()));

        // Built-in Roslyn providers
        providers.AddRange(DiscoverFromAssembly<CodeRefactoringProvider>("Microsoft.CodeAnalysis.CSharp.Features"));
        providers.AddRange(DiscoverFromAssembly<CodeRefactoringProvider>("Microsoft.CodeAnalysis.Features"));

        _refactoringProviders = [.. providers];
        return _refactoringProviders.Value;
    }

    public async Task<ImmutableList<CodeFixEntry>> GetCodeFixesAsync(
        string filePath, int line, string? diagnosticId = null, CancellationToken ct = default)
    {
        var document = GetDocument(filePath);
        if (document is null) return [];

        var compilation = await document.Project.GetCompilationAsync(ct);
        if (compilation is null) return [];

        var tree = await document.GetSyntaxTreeAsync(ct);
        if (tree is null) return [];

        var text = await tree.GetTextAsync(ct);
        var zeroLine = line - 1;
        if (zeroLine < 0 || zeroLine >= text.Lines.Count) return [];

        var lineSpan = text.Lines[zeroLine].Span;

        // Get compiler diagnostics (CS* errors/warnings)
        var compilerDiags = compilation.GetDiagnostics(ct)
            .Where(d => d.Location.IsInSource && d.Location.SourceTree == tree)
            .Where(d => d.Location.SourceSpan.IntersectsWith(lineSpan));

        // Get analyzer diagnostics
        var analyzers = AnalyzerLoader.LoadAll(ResolveTargetFramework());
        var compilationWithAnalyzers = compilation.WithAnalyzers(analyzers);
        var analyzerDiags = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync(ct);
        var filteredAnalyzerDiags = analyzerDiags
            .Where(d => d.Location.IsInSource && d.Location.SourceTree == tree)
            .Where(d => d.Location.SourceSpan.IntersectsWith(lineSpan));

        var lineDiags = compilerDiags.Concat(filteredAnalyzerDiags)
            .Where(d => diagnosticId is null || d.Id == diagnosticId)
            .DistinctBy(d => (d.Id, d.Location.SourceSpan))
            .ToImmutableList();

        if (lineDiags.IsEmpty) return [];

        var fixes = new List<CodeFixEntry>();
        var snapshotVersion = Session.SnapshotVersion;

        foreach (var diagnostic in lineDiags)
        {
            foreach (var provider in GetFixProviders())
            {
                if (!provider.FixableDiagnosticIds.Contains(diagnostic.Id))
                    continue;

                var codeActions = new List<CodeAction>();
                var context = new CodeFixContext(document, diagnostic,
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
                    var id = $"fix-{Interlocked.Increment(ref _nextFixId)}";
                    _actionCache[id] = new CachedCodeAction(action, snapshotVersion);

                    var scope = DetermineScope(provider);
                    fixes.Add(new CodeFixEntry(id, action.Title, diagnostic.Id,
                        diagnostic.GetMessage(), scope));
                }
            }
        }

        return [.. fixes];
    }

    public async Task<ImmutableList<RefactoringEntry>> GetRefactoringsAsync(
        string filePath, int line, int column, int? endLine = null, int? endColumn = null,
        CancellationToken ct = default)
    {
        var document = GetDocument(filePath);
        if (document is null) return [];

        var text = await document.GetTextAsync(ct);
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
            var root = await document.GetSyntaxRootAsync(ct);
            if (root is null) return [];
            var token = root.FindToken(position);
            span = token.Span;
        }

        var refactorings = new List<RefactoringEntry>();
        var snapshotVersion = Session.SnapshotVersion;

        foreach (var provider in GetRefactoringProviders())
        {
            var codeActions = new List<CodeAction>();
            var context = new CodeRefactoringContext(document, span,
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
        if (!_actionCache.TryGetValue(actionId, out var cached))
            return null;

        if (cached.SnapshotVersion != Session.SnapshotVersion)
            return CodeActionPreview.Expired(actionId, cached.Action.Title);

        ImmutableArray<CodeActionOperation> operations;
        try
        {
            operations = await cached.Action.GetOperationsAsync(ct);
        }
        catch (ReflectionTypeLoadException ex)
        {
            logger.LogError(ex, "Code action '{ActionId}' failed due to missing assembly dependencies", actionId);
            return CodeActionPreview.Failed(actionId, cached.Action.Title,
                "Code action failed: missing runtime dependency. " +
                string.Join("; ", ex.LoaderExceptions?.Select(e => e?.Message).Where(m => m is not null).Distinct() ?? []));
        }

        var applyOp = operations.OfType<ApplyChangesOperation>().FirstOrDefault();
        if (applyOp is null) return null;

        var changedSolution = applyOp.ChangedSolution;
        var currentSolution = Session.CurrentSolution;

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
                var textChanges = newText.GetTextChanges(oldText);

                var edits = textChanges.Select(change =>
                {
                    var startLine = oldText.Lines.GetLinePosition(change.Span.Start).Line + 1;
                    var endLine2 = oldText.Lines.GetLinePosition(change.Span.End).Line + 1;
                    var originalText = oldText.GetSubText(change.Span).ToString();
                    return new TextEdit(startLine, endLine2, originalText, change.NewText ?? "");
                }).ToImmutableList();

                changes.Add(new FileChange(oldDoc.FilePath ?? "", edits));
            }
        }

        return new CodeActionPreview(actionId, cached.Action.Title, [.. changes]);
    }

    private Document? GetDocument(string filePath)
    {
        var docId = Session.CurrentSolution.GetDocumentIdsWithFilePath(filePath).FirstOrDefault();
        return docId is null ? null : Session.CurrentSolution.GetDocument(docId);
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
    string Id, string Title, string DiagnosticId, string DiagnosticMessage, string Scope);

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

public sealed record FileChange(string FilePath, ImmutableList<TextEdit> Edits);

public sealed record TextEdit(int StartLine, int EndLine, string OriginalText, string NewText);
