using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.Diagnostics;
using Parlance.Analysis;
using Parlance.Analyzers.Upstream;

namespace Parlance.Analysis.Tests;

public sealed class AnalyzerProviderTests
{
    // ---------------------------------------------------------------------------
    // Stubs
    // ---------------------------------------------------------------------------

    private sealed class StubAnalyzerSource(
        string name,
        SourceTrust trust,
        int priority,
        SourceLoadResult result) : IAnalyzerSource
    {
        public string Name => name;
        public SourceTrust Trust => trust;
        public int Priority => priority;
        public SourceLoadResult Load(string targetFramework, string repoPath) => result;
        public ImmutableArray<string> Probe(string repoPath) => [];
    }

    private sealed class StubTrustNoticeSource(
        string name,
        SourceTrust trust,
        int priority,
        SourceLoadResult result,
        ImmutableList<string> notices) : IAnalyzerSource, ITrustNoticeSource
    {
        // Mutable so a test can simulate an out-of-band `parlance trust` change: flip the result
        // and bump the fingerprint, then assert the provider re-executes Load.
        public SourceLoadResult Result { get; set; } = result;
        public string Fingerprint { get; set; } = "fp0";
        public int LoadCalls { get; private set; }

        public string Name => name;
        public SourceTrust Trust => trust;
        public int Priority => priority;
        public SourceLoadResult Load(string targetFramework, string repoPath)
        {
            LoadCalls++;
            return Result;
        }
        public ImmutableArray<string> Probe(string repoPath) => [];
        public ImmutableList<string> GetTrustNotices(string repoPath) => notices;
        public string TrustFingerprint(string repoPath) => Fingerprint;
    }

    // A minimal concrete DiagnosticAnalyzer for testing dedup
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    private sealed class AnalyzerAlpha : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [];
        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        }
    }

    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    private sealed class AnalyzerBeta : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [];
        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        }
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static SourceLoadResult ResultWithAnalyzers(params DiagnosticAnalyzer[] analyzers) =>
        new(new AnalyzerComponents([.. analyzers], [], []), []);

    private static SourceLoadResult ResultWithFailure(string dll, string reason) =>
        new(AnalyzerComponents.Empty,
            [new DllLoadFailure(dll, reason)]);

    // ---------------------------------------------------------------------------
    // Tests
    // ---------------------------------------------------------------------------

    [Fact]
    public void GetComponents_ReturnsBundledAnalyzers()
    {
        var alpha = new AnalyzerAlpha();
        var source = new StubAnalyzerSource("bundled", SourceTrust.FirstParty, 20,
            ResultWithAnalyzers(alpha));
        var provider = new AnalyzerProvider([source]);

        var result = provider.GetComponents("net10.0", "/repo");

        Assert.Single(result.Components.Analyzers);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public void GetComponents_MergesMultipleSources()
    {
        var alpha = new AnalyzerAlpha();
        var beta = new AnalyzerBeta();
        var bundled = new StubAnalyzerSource("bundled", SourceTrust.FirstParty, 20,
            ResultWithAnalyzers(alpha));
        var extra = new StubAnalyzerSource("extra", SourceTrust.FirstParty, 10,
            ResultWithAnalyzers(beta));
        var provider = new AnalyzerProvider([bundled, extra]);

        var result = provider.GetComponents("net10.0", "/repo");

        Assert.Equal(2, result.Components.Analyzers.Length);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public void GetComponents_DeduplicatesByTypeName()
    {
        // Two sources returning the same concrete analyzer type
        var source1 = new StubAnalyzerSource("s1", SourceTrust.FirstParty, 20,
            ResultWithAnalyzers(new AnalyzerAlpha()));
        var source2 = new StubAnalyzerSource("s2", SourceTrust.FirstParty, 10,
            ResultWithAnalyzers(new AnalyzerAlpha()));
        var provider = new AnalyzerProvider([source1, source2]);

        var result = provider.GetComponents("net10.0", "/repo");

        Assert.Single(result.Components.Analyzers);
    }

    [Fact]
    public void GetComponents_AggregatesFailures()
    {
        var failSource = new StubAnalyzerSource("fail-source", SourceTrust.External, 50,
            ResultWithFailure("/path/to/bad.dll", "untrusted"));
        var goodSource = new StubAnalyzerSource("good-source", SourceTrust.FirstParty, 20,
            ResultWithAnalyzers(new AnalyzerAlpha()));
        var provider = new AnalyzerProvider([failSource, goodSource]);

        var result = provider.GetComponents("net10.0", "/repo");

        Assert.Single(result.Failures);
        Assert.Equal("/path/to/bad.dll", result.Failures[0].DllPath);
        Assert.Single(result.Components.Analyzers);
    }

    [Fact]
    public void GetComponents_HigherPriorityWinsOnCollision()
    {
        // Both sources provide AnalyzerAlpha; local (priority=100) should win
        var alphaFromLocal = new AnalyzerAlpha();
        var alphaFromBundled = new AnalyzerAlpha();

        var local = new StubAnalyzerSource("local", SourceTrust.External, 100,
            ResultWithAnalyzers(alphaFromLocal));
        var bundled = new StubAnalyzerSource("bundled", SourceTrust.FirstParty, 20,
            ResultWithAnalyzers(alphaFromBundled));
        var provider = new AnalyzerProvider([bundled, local]);

        var result = provider.GetComponents("net10.0", "/repo");

        Assert.Single(result.Components.Analyzers);
        Assert.Same(alphaFromLocal, result.Components.Analyzers[0]);
    }

    [Fact]
    public void GetComponents_CachesWhileTrustFingerprintIsStable()
    {
        var source = new StubTrustNoticeSource("local", SourceTrust.External, 100,
            ResultWithAnalyzers(new AnalyzerAlpha()), []);
        var provider = new AnalyzerProvider([source]);

        provider.GetComponents("net10.0", "/repo");
        provider.GetComponents("net10.0", "/repo");

        // Same fingerprint → served from cache; Load runs exactly once.
        Assert.Equal(1, source.LoadCalls);
    }

    [Fact]
    public void GetComponents_ReExecutesWhenTrustFingerprintChanges()
    {
        // Mirrors the PR's manual test: untrusted DLL surfaces a failure, then an out-of-band
        // `parlance trust` grant must take effect on the next call without a restart.
        var source = new StubTrustNoticeSource("local", SourceTrust.External, 100,
            ResultWithFailure("/path/a.dll", "Not trusted"), []);
        var provider = new AnalyzerProvider([source]);

        var beforeTrust = provider.GetComponents("net10.0", "/repo");
        Assert.Single(beforeTrust.Failures);
        Assert.Empty(beforeTrust.Components.Analyzers);

        // Grant trust: result flips and the fingerprint changes.
        source.Result = ResultWithAnalyzers(new AnalyzerAlpha());
        source.Fingerprint = "fp1";

        var afterTrust = provider.GetComponents("net10.0", "/repo");
        Assert.Equal(2, source.LoadCalls);
        Assert.Empty(afterTrust.Failures);
        Assert.Single(afterTrust.Components.Analyzers);
    }

    [Fact]
    public void GetExternalSourceNotices_AggregatesFromExternalSources()
    {
        var external = new StubTrustNoticeSource(
            "local", SourceTrust.External, 100,
            SourceLoadResult.Empty,
            ["untrusted: /path/a.dll", "hash-mismatch: /path/b.dll"]);
        var provider = new AnalyzerProvider([external]);

        var notices = provider.GetExternalSourceNotices("/repo");

        Assert.Equal(2, notices.Count);
        Assert.Contains("untrusted: /path/a.dll", notices);
        Assert.Contains("hash-mismatch: /path/b.dll", notices);
    }

    [Fact]
    public void GetExternalSourceNotices_IgnoresFirstPartySources()
    {
        // First-party sources don't implement ITrustNoticeSource; none should appear
        var firstParty = new StubAnalyzerSource("bundled", SourceTrust.FirstParty, 20,
            SourceLoadResult.Empty);
        var provider = new AnalyzerProvider([firstParty]);

        var notices = provider.GetExternalSourceNotices("/repo");

        Assert.Empty(notices);
    }
}
