using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Parlance.Analyzers.Upstream;

/// <summary>
/// Shared implementation for external sources that load analyzer DLLs from a directory, gated by an
/// <see cref="AnalyzerTrustFile"/>. Subclasses supply only the directory and trust-file locations;
/// the trust check, <c>.resources.dll</c> filter, scan, and instance discovery live here so a fix
/// to the trust/scan path is applied once instead of drifting across copies.
/// <para>
/// Untrusted or hash-mismatched DLLs are reported as <see cref="DllLoadFailure"/>s; their code is
/// never executed. Results are cached per <c>(trust-file, trust-fingerprint)</c> so an out-of-band
/// <c>parlance trust</c> grant takes effect on the next call without restart.
/// </para>
/// </summary>
public abstract class ExternalDirectoryAnalyzerSource : IAnalyzerSource, ITrustNoticeSource
{
    private readonly ConcurrentDictionary<string, SourceLoadResult> _cache = new(StringComparer.Ordinal);

    public abstract string Name { get; }
    public SourceTrust Trust => SourceTrust.External;
    public abstract int Priority { get; }

    /// <summary>The directory to scan for analyzer DLLs for the given repo.</summary>
    protected abstract string ResolveDirectory(string repoPath);

    /// <summary>The trust-store path that gates the directory for the given repo.</summary>
    protected abstract string ResolveTrustFile(string repoPath);

    public SourceLoadResult Load(string targetFramework, string repoPath)
    {
        _ = targetFramework; // external DLLs are not TFM-sliced
        var trustFile = ResolveTrustFile(repoPath);
        var trust = new AnalyzerTrustFile(trustFile);
        // The trust-file path already encodes the repo (local) or is fixed (global), so it
        // distinguishes the two source kinds without a separate repoPath component.
        var key = $"{Path.GetFullPath(trustFile)}|{trust.Fingerprint()}";
        return _cache.GetOrAdd(key, _ => LoadCore(repoPath, trust));
    }

    public ImmutableArray<string> Probe(string repoPath)
    {
        var dir = ResolveDirectory(repoPath);
        if (!Directory.Exists(dir)) return [];
        return [.. AnalyzerTrustFile.EnumerateAnalyzerDlls(dir).Select(Path.GetFullPath)];
    }

    /// <summary>
    /// Returns human-readable notices for DLLs that are not trusted or have a hash mismatch,
    /// WITHOUT loading any analyzer code. Used to surface actionable messages in <c>workspace-status</c>.
    /// </summary>
    public ImmutableList<string> GetTrustNotices(string repoPath)
    {
        var dlls = Probe(repoPath);
        if (dlls.IsEmpty) return [];

        var trust = new AnalyzerTrustFile(ResolveTrustFile(repoPath));
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
        new AnalyzerTrustFile(ResolveTrustFile(repoPath)).Fingerprint();

    private SourceLoadResult LoadCore(string repoPath, AnalyzerTrustFile trust)
    {
        var dir = ResolveDirectory(repoPath);
        if (!Directory.Exists(dir)) return SourceLoadResult.Empty;

        var failures = ImmutableArray.CreateBuilder<DllLoadFailure>();
        var trustedPaths = ImmutableList.CreateBuilder<string>();

        foreach (var dll in AnalyzerTrustFile.EnumerateAnalyzerDlls(dir))
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
}
