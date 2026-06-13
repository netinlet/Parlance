using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Parlance.Analyzers.Upstream;

/// <summary>
/// External source that loads analyzer DLLs from <c>~/.parlance/analyzers/local/</c>.
/// Each DLL is verified against <c>~/.parlance/trusted_analyzers.json</c> before loading.
/// Untrusted or hash-mismatched DLLs are reported as <see cref="DllLoadFailure"/>s; their code
/// is never executed. Results are cached per trust-fingerprint so an out-of-band
/// <c>parlance trust</c> grant takes effect on the next call without restart.
/// </summary>
public sealed class GlobalDirectoryAnalyzerSource : IAnalyzerSource, ITrustNoticeSource
{
    private readonly string _globalDir;
    private readonly string _trustFilePath;
    private readonly ConcurrentDictionary<string, SourceLoadResult> _cache = new(StringComparer.Ordinal);

    public GlobalDirectoryAnalyzerSource()
    {
        _globalDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".parlance", "analyzers", "local");
        _trustFilePath = AnalyzerTrustFile.GlobalPath;
    }

    /// <summary>Internal constructor for testing — overrides both the global dir and trust file path.</summary>
    internal GlobalDirectoryAnalyzerSource(string globalDir, string trustFilePath)
    {
        _globalDir = globalDir;
        _trustFilePath = trustFilePath;
    }

    public string Name => "global";
    public SourceTrust Trust => SourceTrust.External;
    public int Priority => 50;

    public SourceLoadResult Load(string targetFramework, string repoPath)
    {
        _ = targetFramework; // local DLLs are not TFM-sliced
        _ = repoPath;        // global dir is fixed; not repo-relative
        var trust = new AnalyzerTrustFile(_trustFilePath);
        var key = trust.Fingerprint();
        return _cache.GetOrAdd(key, _ => LoadCore(trust));
    }

    public ImmutableArray<string> Probe(string repoPath)
    {
        _ = repoPath; // global dir is fixed
        if (!Directory.Exists(_globalDir)) return [];
        return [.. Directory.EnumerateFiles(_globalDir, "*.dll")
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
        _ = repoPath; // global dir is fixed
        var dlls = Probe(repoPath);
        if (dlls.IsEmpty) return [];

        var trust = new AnalyzerTrustFile(_trustFilePath);
        var notices = ImmutableList.CreateBuilder<string>();
        foreach (var dll in dlls)
        {
            var notice = AnalyzerTrustMessages.TrustFailureMessage(trust.Check(dll), dll);
            if (notice is not null)
                notices.Add(notice);
        }
        return notices.ToImmutable();
    }

    public string TrustFingerprint(string repoPath)
    {
        _ = repoPath; // global trust file is fixed; not repo-relative
        return new AnalyzerTrustFile(_trustFilePath).Fingerprint();
    }

    private SourceLoadResult LoadCore(AnalyzerTrustFile trust)
    {
        if (!Directory.Exists(_globalDir)) return SourceLoadResult.Empty;

        var failures = ImmutableArray.CreateBuilder<DllLoadFailure>();
        var trustedPaths = ImmutableList.CreateBuilder<string>();

        foreach (var dll in Directory.EnumerateFiles(_globalDir, "*.dll")
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
}
