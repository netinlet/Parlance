namespace Parlance.Analyzers.Upstream;

/// <summary>
/// External source that loads analyzer DLLs from <c>&lt;repo&gt;/.parlance/analyzers/local/</c>,
/// gated by <c>&lt;repo&gt;/.parlance/trusted_analyzers.json</c>. See
/// <see cref="ExternalDirectoryAnalyzerSource"/> for the trust/scan behavior.
/// </summary>
public sealed class LocalDirectoryAnalyzerSource : ExternalDirectoryAnalyzerSource
{
    public override string Name => "local";
    public override int Priority => 100;

    protected override string ResolveDirectory(string repoPath) =>
        Path.Combine(repoPath, ".parlance", "analyzers", "local");

    protected override string ResolveTrustFile(string repoPath) =>
        AnalyzerTrustFile.ProjectPath(repoPath);
}
