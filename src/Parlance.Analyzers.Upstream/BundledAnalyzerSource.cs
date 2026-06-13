using System.Collections.Immutable;

namespace Parlance.Analyzers.Upstream;

/// <summary>
/// First-party source wrapping Parlance's shipped analyzer set (NetAnalyzers, CodeStyle,
/// Roslynator, PARL rules). Trusted by distribution; the underlying loaders cache per TFM.
/// </summary>
public sealed class BundledAnalyzerSource : IAnalyzerSource
{
    public string Name => "bundled";
    public SourceTrust Trust => SourceTrust.FirstParty;
    public int Priority => 20;

    public SourceLoadResult Load(string targetFramework, string repoPath) =>
        new(new AnalyzerComponents(
                AnalyzerLoader.LoadAll(targetFramework),
                FixProviderLoader.LoadAll(targetFramework),
                RefactoringProviderLoader.LoadAll(targetFramework)),
            []);

    public ImmutableArray<string> Probe(string repoPath) => [];
}
