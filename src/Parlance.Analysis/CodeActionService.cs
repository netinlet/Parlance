using System.Collections.Concurrent;
using System.Collections.Immutable;
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

    private ImmutableArray<CodeFixProvider> GetFixProviders() =>
        _fixProviders ??= AnalyzerLoader.LoadCodeFixProviders(ResolveTargetFramework());

    private ImmutableArray<CodeRefactoringProvider> GetRefactoringProviders() =>
        _refactoringProviders ??= AnalyzerLoader.LoadCodeRefactoringProviders(ResolveTargetFramework());

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

        // Get diagnostics on this line
        var analyzers = AnalyzerLoader.LoadAll(ResolveTargetFramework());
        var compilationWithAnalyzers = compilation.WithAnalyzers(analyzers);
        var allDiags = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync(ct);

        var lineDiags = allDiags
            .Where(d => d.Location.IsInSource && d.Location.SourceTree == tree)
            .Where(d => d.Location.SourceSpan.IntersectsWith(lineSpan))
            .Where(d => diagnosticId is null || d.Id == diagnosticId)
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

                    var scope = provider.GetFixAllProvider() is not null ? "document" : "document";
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

        TextSpan span;
        if (endLine is not null && endColumn is not null)
        {
            var startPos = text.Lines.GetPosition(new LinePosition(zeroLine, zeroCol));
            var endPos = text.Lines.GetPosition(new LinePosition(endLine.Value - 1, endColumn.Value - 1));
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

        var operations = await cached.Action.GetOperationsAsync(ct);
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
}

public sealed record CachedCodeAction(CodeAction Action, long SnapshotVersion);

public sealed record CodeFixEntry(
    string Id, string Title, string DiagnosticId, string DiagnosticMessage, string Scope);

public sealed record RefactoringEntry(string Id, string Title, string? Category);

public sealed record CodeActionPreview(
    string ActionId, string Title, ImmutableList<FileChange> Changes, bool IsExpired = false)
{
    public static CodeActionPreview Expired(string actionId, string title) =>
        new(actionId, title, [], IsExpired: true);
}

public sealed record FileChange(string FilePath, ImmutableList<TextEdit> Edits);

public sealed record TextEdit(int StartLine, int EndLine, string OriginalText, string NewText);
