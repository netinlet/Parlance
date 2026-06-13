using System.Collections.Immutable;

namespace Parlance.Analyzers.Upstream;

public enum SourceTrust { FirstParty, External }

public interface IAnalyzerSource
{
    string Name { get; }            // doubles as the trust "source kind": "bundled", "roslyn-features", "local"
    SourceTrust Trust { get; }
    int Priority { get; }           // higher wins type-name collisions (local=100, bundled=20, roslyn-features=10)
    SourceLoadResult Load(string targetFramework, string repoPath);  // executes analyzer code — only called when allowed
    ImmutableArray<string> Probe(string repoPath);                   // lists candidate DLLs WITHOUT loading; [] for first-party
}

public sealed record SourceLoadResult(
    AnalyzerComponents Components,
    ImmutableArray<DllLoadFailure> Failures)
{
    public static readonly SourceLoadResult Empty = new(AnalyzerComponents.Empty, []);
}

public sealed record DllLoadFailure(string DllPath, string Reason);
