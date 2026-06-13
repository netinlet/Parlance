using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Server;
using Parlance.Analysis;
using Parlance.Analysis.Curation;
using Parlance.Analysis.Tests;
using Parlance.CSharp.Workspace;
using Parlance.CSharp.Workspace.Tests.Integration;
using Parlance.Mcp.Tools;

namespace Parlance.Mcp.Tests.Tools;

/// <summary>
/// The end-to-end stamping guard. <see cref="AllResultsStampedTests"/> only invokes result factories
/// with a sentinel — it never drives a tool method, so it cannot catch a tool that forgets to capture
/// <c>session.SnapshotVersion</c> (ships the not-loaded <c>0</c>) or captures the wrong one. This drives
/// every loaded tool against the shared workspace with args that reach a stampable outcome (a
/// not_found/empty result is fine — the assertion is on the STAMP, not the payload) and requires the
/// returned <c>SnapshotVersion</c> to equal the live session's. The completeness check fails if a new
/// <c>[McpServerToolType]</c> is added without a row here, so the coverage can't silently rot.
/// </summary>
[Trait("Category", "Integration")]
public sealed class EveryToolStampsLiveSnapshotTests(WorkspaceFixture fixture) : IClassFixture<WorkspaceFixture>
{
    private readonly AnalysisService _analysis = new(
        fixture.Holder, fixture.Query,
        new CurationSetProvider(NullLogger<CurationSetProvider>.Instance),
        AnalyzerProviderTestFactory.CreateWithBundled(),
        NullLogger<AnalysisService>.Instance);
    private readonly CodeActionService _codeActions = new(fixture.Holder, AnalyzerProviderTestFactory.CreateWithBundled(), NullLogger<CodeActionService>.Instance);
    private readonly ParlanceMcpConfiguration _config = new("/fake/path.sln", "/tmp");

    // A name nothing in the loaded solution resolves to, so every symbol/type tool lands on its
    // stamped not_found/ambiguous/empty path rather than a real payload.
    private const string NoSuchName = "Zzz_NoSuchSymbol_For_StampGuard";

    [Fact]
    public async Task EveryLoadedTool_StampsTheLiveSnapshotVersion()
    {
        var h = fixture.Holder;
        var q = fixture.Query;
        var ct = CancellationToken.None;
        var expected = fixture.Session.SnapshotVersion;
        // A real, in-solution file so analyze hits its stamped success path deterministically.
        var realFile = Path.Combine(TestPaths.RepoRoot, "src", "Parlance.Abstractions", "RepoPath.cs");

        var invocations = new Dictionary<string, Func<Task<long>>>
        {
            ["AnalyzeTool"] = async () => Stamp(await AnalyzeTool.Analyze(h, _analysis, [realFile], ct: ct)),
            ["ApplyCodeActionTool"] = async () => Stamp(await ApplyCodeActionTool.ApplyCodeAction(h, _codeActions, "zzz-no-such-action", ct: ct)),
            ["CallHierarchyTool"] = async () => Stamp(await CallHierarchyTool.GetCallHierarchy(h, q, NoSuchName, ct)),
            ["DecompileTypeTool"] = async () => Stamp(await DecompileTypeTool.DecompileType(h, q, NullLogger<DecompileTypeTool>.Instance, NoSuchName, ct)),
            ["DescribeTypeTool"] = async () => Stamp(await DescribeTypeTool.DescribeType(h, q, NoSuchName, ct)),
            ["FindImplementationsTool"] = async () => Stamp(await FindImplementationsTool.FindImplementations(h, q, NoSuchName, ct)),
            ["FindReferencesTool"] = async () => Stamp(await FindReferencesTool.FindReferences(h, q, NoSuchName, ct: ct)),
            ["GetCodeFixesTool"] = async () => Stamp(await GetCodeFixesTool.GetCodeFixes(h, _codeActions, "src/Zzz_NoSuchFile.cs", 1, null, ct)),
            ["GetRefactoringsTool"] = async () => Stamp(await GetRefactoringsTool.GetRefactorings(h, _codeActions, "src/Zzz_NoSuchFile.cs", 1, 1, null, null, ct)),
            ["GetSymbolDocsTool"] = async () => Stamp(await GetSymbolDocsTool.GetSymbolDocs(h, q, NullLogger<GetSymbolDocsTool>.Instance, NoSuchName, ct)),
            ["GetTypeAtTool"] = async () => Stamp(await GetTypeAtTool.GetTypeAt(h, q, "src/Zzz_NoSuchFile.cs", 1, 1, ct)),
            ["GetTypeDependenciesTool"] = async () => Stamp(await GetTypeDependenciesTool.GetTypeDependencies(h, q, NoSuchName, ct)),
            ["GotoDefinitionTool"] = async () => Stamp(await GotoDefinitionTool.GotoDefinition(h, q, NoSuchName, null, null, null, ct)),
            ["OutlineFileTool"] = async () => Stamp(await OutlineFileTool.OutlineFile(h, q, "src/Zzz_NoSuchFile.cs", ct)),
            ["PreviewCodeActionTool"] = async () => Stamp(await PreviewCodeActionTool.PreviewCodeAction(h, _codeActions, "zzz-no-such-action", ct: ct)),
            ["SafeToDeleteTool"] = async () => Stamp(await SafeToDeleteTool.CheckSafeToDelete(h, q, NoSuchName, ct)),
            ["SearchSymbolsTool"] = async () => Stamp(await SearchSymbolsTool.SearchSymbols(h, q, NoSuchName, ct: ct)),
            ["TypeHierarchyTool"] = async () => Stamp(await TypeHierarchyTool.TypeHierarchy(h, q, NoSuchName, ct: ct)),
            ["WorkspaceStatusTool"] = () => Task.FromResult(
                WorkspaceStatusTool.GetStatus(h, _config, NullLogger<WorkspaceStatusTool>.Instance).SnapshotVersion),
        };

        // The buffer-overlay tool is the one [McpServerToolType] that cannot be exercised here: its
        // loaded path mutates a Server-mode session and throws in the Report-mode shared fixture (whose
        // contract is read-only-tests-only). Its live-snapshot stamping is covered against an isolated
        // Server session in SyncBufferToolTests instead. Exempt by name so the completeness check below
        // still fails for any *other* newly-added tool that forgets to stamp.
        var serverOnlyCoveredElsewhere = new HashSet<string> { nameof(SyncBufferTool) };

        // Completeness: every [McpServerToolType] must be exercised above, so a newly-added tool that
        // forgets to stamp can't slip through by simply not being covered.
        var declared = typeof(AnalyzeTool).Assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .Select(t => t.Name)
            .ToHashSet();
        var uncovered = declared.Except(invocations.Keys).Except(serverOnlyCoveredElsewhere).ToList();
        Assert.True(uncovered.Count == 0, "Tool(s) with no stamp coverage here: " + string.Join(", ", uncovered));

        var failures = new List<string>();
        foreach (var (name, invoke) in invocations)
        {
            var stamped = await invoke();
            // A loaded session's version is >= 1 (snapshots start at 1 and only increment), so the
            // not-loaded 0 sentinel from a live workspace is the defect — and it must equal the
            // version current for this fixture (Report mode: no file watching, so it never advances).
            if (stamped != expected)
                failures.Add($"{name} -> {stamped} (expected {expected})");
        }

        Assert.True(failures.Count == 0,
            "Loaded tools not stamping the live snapshot version: " + string.Join(", ", failures));
    }

    private static long Stamp(object result) =>
        (long)result.GetType().GetProperty("SnapshotVersion")!.GetValue(result)!;
}
