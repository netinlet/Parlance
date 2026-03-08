using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Parlance.Analyzers.Upstream;
using Parlance.CSharp;

namespace Parlance.Cli.Analysis;

internal sealed record FixResult(IReadOnlyList<FixedFile> FixedFiles);

internal sealed record FixedFile(string FilePath, string OriginalContent, string NewContent);

internal static class WorkspaceFixer
{
    public static async Task<FixResult> FixAsync(
        IReadOnlyList<string> filePaths,
        string[]? suppressRules = null,
        string? languageVersion = null,
        string targetFramework = "net10.0",
        CancellationToken ct = default)
    {
        suppressRules ??= [];

        // Discover fix providers from the PARL assembly via reflection
        var fixProviders = DiscoverFixProviders();

        // Collect all fixable diagnostic IDs from the discovered fix providers
        var fixableIds = fixProviders
            .SelectMany(fp => fp.FixableDiagnosticIds)
            .ToHashSet();

        // Load all analyzers and keep only those that produce fixable diagnostics
        var allAnalyzers = AnalyzerLoader.LoadAll(targetFramework);
        var fixableAnalyzers = allAnalyzers
            .Where(a => a.SupportedDiagnostics.Any(d => fixableIds.Contains(d.Id)))
            .ToImmutableArray();

        var parseOptions = new CSharpParseOptions(
            languageVersion is not null && LanguageVersionFacts.TryParse(languageVersion, out var lv)
                ? lv
                : LanguageVersion.Latest);

        var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var projectInfo = ProjectInfo.Create(
            projectId, VersionStamp.Default, "ParlanceFixTarget", "ParlanceFixTarget",
            LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            parseOptions: parseOptions,
            metadataReferences: CompilationFactory.LoadReferences());

        var solution = workspace.CurrentSolution.AddProject(projectInfo);

        var documentPaths = new Dictionary<DocumentId, string>();
        var originalContents = new Dictionary<string, string>();

        foreach (var path in filePaths)
        {
            var content = await File.ReadAllTextAsync(path, ct);
            originalContents[path] = content;

            var docId = DocumentId.CreateNewId(projectId);
            solution = solution.AddDocument(docId, Path.GetFileName(path),
                content, filePath: path);
            documentPaths[docId] = path;
        }

        workspace.TryApplyChanges(solution);

        // Filter analyzers by suppress rules
        var analyzers = suppressRules.Length > 0
            ? fixableAnalyzers.Where(a => !a.SupportedDiagnostics.Any(d => suppressRules.Contains(d.Id))).ToImmutableArray()
            : fixableAnalyzers;

        if (analyzers.Length == 0)
            return new FixResult([]);

        // Iteratively apply fixes
        var currentSolution = workspace.CurrentSolution;
        const int maxIterations = 50;

        for (var iteration = 0; iteration < maxIterations; iteration++)
        {
            var project = currentSolution.GetProject(projectId)!;
            var compilation = (await project.GetCompilationAsync(ct))!;
            var compilationWithAnalyzers = compilation.WithAnalyzers(analyzers);
            var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync(ct);

            var fixableDiags = diagnostics
                .Where(d => !suppressRules.Contains(d.Id))
                .ToList();

            if (fixableDiags.Count == 0)
                break;

            var applied = false;
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

                if (applied) break; // Re-analyze after each fix
            }

            if (!applied) break;
        }

        // Diff original vs fixed
        var fixedFiles = new List<FixedFile>();
        foreach (var (docId, path) in documentPaths)
        {
            var doc = currentSolution.GetDocument(docId);
            if (doc is null) continue;

            var newText = (await doc.GetTextAsync(ct)).ToString();
            if (originalContents.TryGetValue(path, out var original) && original != newText)
            {
                fixedFiles.Add(new FixedFile(path, original, newText));
            }
        }

        return new FixResult(fixedFiles);
    }

    public static void ApplyFixes(FixResult result)
    {
        foreach (var file in result.FixedFiles)
        {
            var encoding = DetectEncoding(file.FilePath);
            File.WriteAllText(file.FilePath, file.NewContent, encoding);
        }
    }

    internal static Encoding DetectEncoding(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        Span<byte> bom = stackalloc byte[4];
        var bytesRead = stream.Read(bom);

        if (bytesRead >= 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF)
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

        if (bytesRead >= 2 && bom[0] == 0xFF && bom[1] == 0xFE)
            return new UnicodeEncoding(bigEndian: false, byteOrderMark: true);

        if (bytesRead >= 2 && bom[0] == 0xFE && bom[1] == 0xFF)
            return new UnicodeEncoding(bigEndian: true, byteOrderMark: true);

        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    }

    private static List<CodeFixProvider> DiscoverFixProviders()
    {
        var parlAssembly = typeof(Parlance.CSharp.Analyzers.Rules.PARL0001_PreferPrimaryConstructors).Assembly;
        var providers = new List<CodeFixProvider>();

        Type[] types;
        try
        {
            types = parlAssembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types.Where(t => t is not null).ToArray()!;
        }

        foreach (var type in types)
        {
            if (type.IsAbstract || !typeof(CodeFixProvider).IsAssignableFrom(type))
                continue;

            try
            {
                if (Activator.CreateInstance(type) is CodeFixProvider provider)
                    providers.Add(provider);
            }
            catch
            {
                // Skip types that can't be instantiated
            }
        }

        return providers;
    }
}
