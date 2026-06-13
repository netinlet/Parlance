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

    public SourceLoadResult Load(string targetFramework, string repoPath)
    {
        // The bundled loaders only ship analyzer slices for SupportedFrameworks and throw on
        // anything else. Degrade an unsupported TFM (net9.0, net7.0, net48, netstandard2.0, …)
        // to net10.0 so analyze/code-fix still produce results instead of crashing the whole call.
        var tfm = AnalyzerDllScanner.SupportedFrameworks.Contains(targetFramework)
            ? targetFramework
            : "net10.0";

        return new(new AnalyzerComponents(
                AnalyzerLoader.LoadAll(tfm),
                FixProviderLoader.LoadAll(tfm),
                RefactoringProviderLoader.LoadAll(tfm)),
            []);
    }

    public ImmutableArray<string> Probe(string repoPath) => [];
}
