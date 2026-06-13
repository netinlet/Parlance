using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Parlance.Analyzers.Upstream;

/// <summary>
/// External source that loads analyzer DLLs from <c>&lt;repo&gt;/.parlance/analyzers/local/</c>.
/// Each DLL is verified against <c>&lt;repo&gt;/.parlance/trusted_analyzers.json</c> before loading.
/// Untrusted or hash-mismatched DLLs are reported as <see cref="DllLoadFailure"/>s; their code
/// is never executed. Results are cached per <c>(canonical-repo-path, trust-fingerprint)</c> so
/// an out-of-band <c>parlance trust</c> grant takes effect on the next call without restart.
/// </summary>
public sealed class LocalDirectoryAnalyzerSource : IAnalyzerSource, ITrustNoticeSource
{
    private readonly ConcurrentDictionary<string, SourceLoadResult> _cache = new(StringComparer.Ordinal);

    public string Name => "local";
    public SourceTrust Trust => SourceTrust.External;
    public int Priority => 100;

    public SourceLoadResult Load(string targetFramework, string repoPath)
    {
        _ = targetFramework; // local DLLs are not TFM-sliced; repoPath determines the directory
        var trust = new AnalyzerTrustFile(AnalyzerTrustFile.ProjectPath(repoPath));
        var key = $"{Canonical(repoPath)}|{trust.Fingerprint()}";
        return _cache.GetOrAdd(key, _ => LoadCore(repoPath, trust));
    }

    public ImmutableArray<string> Probe(string repoPath)
    {
        var dir = LocalDirectory(repoPath);
        if (!Directory.Exists(dir)) return [];
        return [.. Directory.EnumerateFiles(dir, "*.dll")
            .Where(f => !f.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFullPath)];
    }

    /// <summary>
    /// Returns human-readable notices for DLLs that are not trusted or have a hash mismatch,
    /// WITHOUT loading any analyzer code. Used by <see cref="AnalyzerLoader"/> to surface
    /// actionable messages in <c>workspace-status</c>.
    /// </summary>
    public ImmutableList<string> GetTrustNotices(string repoPath)
    {
        var dlls = Probe(repoPath);
        if (dlls.IsEmpty) return [];

        var trust = new AnalyzerTrustFile(AnalyzerTrustFile.ProjectPath(repoPath));
        var notices = ImmutableList.CreateBuilder<string>();
        foreach (var dll in dlls)
        {
            var notice = AnalyzerTrustMessages.TrustFailureMessage(trust.Check(dll), dll);
            if (notice is not null)
                notices.Add(notice);
        }
        return notices.ToImmutable();
    }

    public string TrustFingerprint(string repoPath) =>
        new AnalyzerTrustFile(AnalyzerTrustFile.ProjectPath(repoPath)).Fingerprint();

    private static SourceLoadResult LoadCore(string repoPath, AnalyzerTrustFile trust)
    {
        var dir = LocalDirectory(repoPath);
        if (!Directory.Exists(dir)) return SourceLoadResult.Empty;

        var failures = ImmutableArray.CreateBuilder<DllLoadFailure>();
        var trustedPaths = ImmutableList.CreateBuilder<string>();

        foreach (var dll in Directory.EnumerateFiles(dir, "*.dll")
            .Where(f => !f.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase)))
        {
            var fullPath = Path.GetFullPath(dll);
            var checkResult = trust.Check(fullPath);
            var msg = AnalyzerTrustMessages.TrustFailureMessage(checkResult, fullPath);
            if (msg is not null)
                failures.Add(new DllLoadFailure(fullPath, msg));
            else
                trustedPaths.Add(fullPath);
        }

        if (trustedPaths.Count == 0)
            return new SourceLoadResult(AnalyzerComponents.Empty, failures.ToImmutable());

        var scan = AnalyzerDllScanner.ScanAssembliesFromPathsReport(trustedPaths);
        failures.AddRange(scan.Failures);

        var analyzers = scan.Assemblies
            .SelectMany(a => a.DiscoverInstances<DiagnosticAnalyzer>())
            .ToImmutableArray();
        var fixes = scan.Assemblies
            .SelectMany(a => a.DiscoverInstances<CodeFixProvider>())
            .ToImmutableArray();
        var refactorings = scan.Assemblies
            .SelectMany(a => a.DiscoverInstances<CodeRefactoringProvider>())
            .ToImmutableArray();

        return new SourceLoadResult(
            new AnalyzerComponents(analyzers, fixes, refactorings),
            failures.ToImmutable());
    }

    private static string LocalDirectory(string repoPath) =>
        Path.Combine(repoPath, ".parlance", "analyzers", "local");

    private static string Canonical(string repoPath) => Path.GetFullPath(repoPath);
}
